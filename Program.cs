using MyTts.Config.ServiceConfigurations;
using MyTts.Routes;
using Microsoft.AspNetCore.HttpOverrides;
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
using MyTts.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging.Console;

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

// Configure logging
//builder.Logging.ClearProviders();
//builder.Logging.AddDebug(); // Add debug output
//builder.Logging.AddConsole(options =>
//{
//    options.IncludeScopes = true;
//    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
//    options.UseUtcTimestamp = false;
//});

//// Set minimum log level for the application
//builder.Logging.SetMinimumLevel(LogLevel.Debug);

//// Add test log to verify logging is working
//var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
//logger.LogInformation("Application starting up...");
//logger.LogDebug("Debug logging is enabled");
//logger.LogWarning("Warning logging is enabled");
//logger.LogError("Error logging is enabled");

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
    .AddApiServices()
    .AddHttpContextAccessor();
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

app.UseErrorHandlerMiddleware(middleware =>
{
    // Optional: Add or override custom exception mappings here
    // For example, if you want a specific custom exception to return a 409 Conflict
    // middleware.AddExceptionMapping(typeof(MyCustomConflictException), HttpStatusCode.Conflict);
});

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

// Configure forwarded headers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();
app.UseCors("AllowLocalDevelopment");

// Add custom middleware to skip authentication for specific paths
// app.Use(async (context, next) =>
// {
//     var path = context.Request.Path.Value?.ToLower();
//     if (path?.StartsWith("/testerror") == true || 
//         path?.StartsWith("/.well-known") == true ||
//         path?.StartsWith("/api/mp3/merged") == true)
//     {
//         // Skip authentication for test routes, Chrome DevTools, and merged MP3
//         context.Features.Set<IAuthenticationFeature>(new AuthenticationFeature());
//         await next();
//         return;
//     }
//     await next();
// });
//app.UseWhen(context =>
//{
//    var path = context.Request.Path;
//    var excludeFromAuth =
//        path.StartsWithSegments("/testerror") ||
//        path.StartsWithSegments("/.well-known") ||
//        path.StartsWithSegments("/api/mp3/merged") ||
//        path.StartsWithSegments("/static");

//    // THIS LINE IS CRITICAL FOR DIAGNOSIS
//    Console.WriteLine($"[DEBUG UseWhen Predicate] Path: '{path}', Exclude from Auth: {excludeFromAuth}");

//    return !excludeFromAuth; 
//}, appBuilder =>
//{
    // Only apply authentication and authorization middleware to paths that are NOT excluded
    app.UseAuthentication();
    app.UseAuthorization();
//});
// Configure endpoints
ApiRoutes.RegisterMp3Routes(app);
// Register your new Auth routes
AuthApiRoutes.RegisterAuthRoutes(app);
// Register test error routes
TestErrorRoutes.RegisterTestErrorRoutes(app);
// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
// Log listening addresses
foreach (var address in app.Urls)
{
    Console.WriteLine($"Application listening on: {address}");
}

app.Run();