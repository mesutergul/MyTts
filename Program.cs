using MyTts.Config.ServiceConfigurations;
using MyTts.Routes;
using Microsoft.AspNetCore.HttpOverrides;
using MyTts.Models.Auth;
using Microsoft.AspNetCore.Identity;
using MyTts.Extensions;

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
    .AddLoggingServices(builder.Configuration)
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

app.UseAuthentication();
app.UseAuthorization();

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