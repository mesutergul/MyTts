using MyTts.Config.ServiceConfigurations;
using MyTts.Routes;
using Microsoft.AspNetCore.HttpOverrides;

// Configure FFmpeg with absolute path
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
    .AddStorageServices(builder.Configuration)
    .AddElevenLabsServices(builder.Configuration)
    .AddRedisServices(builder.Configuration)
    .AddEmailServices(builder.Configuration)
    .AddHttpClientsServices()
    .AddApiServices();

var app = builder.Build();

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
app.UseAuthorization();

// Configure endpoints
ApiRoutes.RegisterMp3Routes(app);
app.MapControllers();

// Log listening addresses
foreach (var address in app.Urls)
{
    Console.WriteLine($"Application listening on: {address}");
}

app.Run();