using MyTts.Config.ServiceConfigurations;
using MyTts.Routes;
using Microsoft.AspNetCore.HttpOverrides;
using MyTts.Middleware;
using MyTts.Models.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MyTts.Services;
using Microsoft.Extensions.Options;
using Polly;
using StackExchange.Redis;
using MyTts.Services.Interfaces;

/*
ffmpeg -i input.mp3 -filter:a loudnorm output_normalized.mp3

set TOKEN $(curl -s -X POST http://localhost:5209/api/auth/login \
      -H "Content-Type: application/json" \
      -d '{"email": "admin@example.com", "password": "Admin123!"}' | jq -r '.token')
curl -X GET http://localhost:5209/api/auth/me \
          -H "Authorization: Bearer $TOKEN"
*/

//Configure FFmpeg with absolute path
 string baseDir = AppContext.BaseDirectory;
string ffmpegDir = Path.Combine(baseDir, "ffmpeg-bin");

// Ensure FFmpeg directory exists
if (!Directory.Exists(ffmpegDir))
{
    throw new DirectoryNotFoundException($"FFmpeg directory not found at: {ffmpegDir}");
}

// Ensure FFmpeg executables exist
string ffmpegExe = Path.Combine(ffmpegDir, "ffmpeg.exe");
string ffprobeExe = Path.Combine(ffmpegDir, "ffprobe.exe");

if (!File.Exists(ffmpegExe) || !File.Exists(ffprobeExe))
{
    throw new FileNotFoundException($"FFmpeg executables not found in: {ffmpegDir}");
}

FFMpegCore.GlobalFFOptions.Configure(new FFMpegCore.FFOptions
{
    BinaryFolder = ffmpegDir,
    TemporaryFilesFolder = Path.GetTempPath()
});

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services
    .AddAutoMapperServices()
    .AddApplicationServices(builder.Configuration)
    .AddDatabaseServices(builder.Configuration)
    .AddAuthenticationServices(builder.Configuration)
    .AddStorageServices(builder.Configuration)
    .AddElevenLabsServices(builder.Configuration)
    .AddCloudTtsConfiguration(builder.Configuration)
    .AddEmailServices(builder.Configuration)
    .AddHttpClientsServices()
    .AddRedisServices(builder.Configuration)
    .AddApiServices();
// Inside Program.cs, after all AddServices calls:
//builder.Services.AddSingleton<SharedPolicyFactory>();

// Debug step by step - Inside Program.cs, after all AddServices calls:
//var tempServiceProvider = builder.Services.BuildServiceProvider();
//try
//{
//    Console.WriteLine("Testing service resolution step by step...");
    
//    // Test 1: Basic logger
//    var logger = tempServiceProvider.GetRequiredService<ILogger<SharedPolicyFactory>>();
//    Console.WriteLine("✓ ILogger<SharedPolicyFactory> resolved");
    
//    // Test 2: INotificationService
//    // try
//    // {
//    //     var notificationService = tempServiceProvider.GetRequiredService<INotificationService>();
//    //     Console.WriteLine("✓ INotificationService resolved");
//    // }
//    // catch (Exception ex)
//    // {
//    //     Console.WriteLine($"✗ INotificationService failed: {ex.Message}");
//    //     throw; // This is likely the root cause
//    // }
    
//    // Test 3: SharedPolicyFactory
//    var policyFactory = tempServiceProvider.GetRequiredService<SharedPolicyFactory>();
//    Console.WriteLine("✓ SharedPolicyFactory resolved");
    
//    // Test 4: Redis config
//    var redisConfig = tempServiceProvider.GetRequiredService<IOptions<MyTts.Config.RedisConfig>>();
//    Console.WriteLine("✓ RedisConfig resolved");

//    // Test 5: Finally test RedisCacheService
//    var redisCacheService = tempServiceProvider.GetRequiredService<IRedisCacheService>();
//    Console.WriteLine("✓ IRedisCacheService resolved successfully!");
    
//}
//catch (Exception ex)
//{
//    Console.WriteLine($"✗ Service resolution failed: {ex.Message}");
    
//    // Print inner exception details
//    var innerEx = ex.InnerException;
//    while (innerEx != null)
//    {
//        Console.WriteLine($"Inner exception: {innerEx.Message}");
//        innerEx = innerEx.InnerException;
//    }
    
//    Console.WriteLine($"Stack trace: {ex.StackTrace}");
//}
//finally
//{
//    tempServiceProvider.Dispose();
//}
var app = builder.Build();

// ✅ SEED ADMIN ROLE & USER HERE
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    Console.WriteLine("🔧 Starting role and admin user seeding...");

    string[] roles = new[] { "Admin", "User" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
            Console.WriteLine($"✅ Role '{role}' created.");
        }
        else
        {
            Console.WriteLine($"ℹ️ Role '{role}' already exists.");
        }
    }

    var adminEmail = builder.Configuration["AdminUser:Email"] ?? "admin@example.com";
    var adminPassword = builder.Configuration["AdminUser:Password"] ?? "Admin123";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "User",
            IsActive = true,
            DailyRequestLimit = 1000 // Set a higher limit for admin
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            Console.WriteLine("✅ Admin user created and assigned to 'Admin' role.");
        }
        else
        {
            Console.WriteLine("❌ Failed to create admin user:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($" - {error.Description}");
            }
        }
    }
    else
    {
        Console.WriteLine("⚠️ Admin user already exists.");
    }
}


app.UseDefaultFiles(); 
app.UseStaticFiles();

// Register the new ErrorHandlerMiddleware early in the pipeline
app.UseMiddleware<ErrorHandlerMiddleware>();

// Configure forwarded headers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.UseCors("AllowLocalDevelopment");

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization(); // Move this after UseAuthentication


// Configure endpoints
ApiRoutes.RegisterMp3Routes(app);
// Register your new Auth routes
AuthApiRoutes.RegisterAuthRoutes(app);
app.MapControllers();

// Log listening addresses
foreach (var address in app.Urls)
{
    Console.WriteLine($"Application listening on: {address}");
}

app.Run();