

using System.Text.Json;
using MyTts.Controllers;
using MyTts.Models;

namespace MyTts.Routes
{
    public static class ApiRoutes
    {
        public static void RegisterMp3Routes(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("api/mp3/feed/{language}", async (HttpContext context, CancellationToken token) =>
            {
                var controller = context.RequestServices.GetRequiredService<Mp3Controller>();
                var language = context.Request.RouteValues["language"]?.ToString();
                var limit = int.TryParse(context.Request.Query["limit"], out var parsedLimit) ? parsedLimit : 20;
                await controller.Feed(context, language, limit, token);
            }).WithMetadata(new { Name = "elevenlabs.mp3.feed" });

            endpoints.MapPost("api/mp3/one", async (HttpContext context, CancellationToken token) =>
            {
                var controller = context.RequestServices.GetRequiredService<Mp3Controller>();

                using var reader = new StreamReader(context.Request.Body);
                var requestBody = await reader.ReadToEndAsync(token);
                var oneRequest = JsonSerializer.Deserialize<OneRequest>(requestBody,
                     new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                await controller.One(context, oneRequest, token);
            }).WithMetadata(new { Name = "elevenlabs.mp3.one" });

            endpoints.MapPost("api/mp3/list", async (HttpContext context, CancellationToken token) =>
            {
                var controller = context.RequestServices.GetRequiredService<Mp3Controller>();
                using var reader = new StreamReader(context.Request.Body);
                var requestBody = await reader.ReadToEndAsync(token);
                var listRequest = JsonSerializer.Deserialize<ListRequest>(requestBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                await controller.List(context, listRequest, token);
            }).WithMetadata(new { Name = "elevenlabs.mp3.list" });

            endpoints.MapGet("api/mp3/one/{id}", async (HttpContext context, CancellationToken token) =>
            {
                var controller = context.RequestServices.GetRequiredService<Mp3Controller>();
                var id = context.Request.RouteValues["id"]?.ToString();
                await controller.GetFile(context, id, token);
            }).WithMetadata(new { Name = "elevenlabs.mp3.getone" });

            endpoints.MapGet("api/mp3/last/{language}", async (HttpContext context, CancellationToken token) =>
            {
                var controller = context.RequestServices.GetRequiredService<Mp3Controller>();
                var language = context.Request.RouteValues["language"]?.ToString();
                await controller.GetLast(context, language, token);
            }).WithMetadata(new { Name = "elevenlabs.mp3.getlast" });
            // New endpoint for merging multiple MP3 files
            //endpoints.MapPost("api/mp3/merge", async (HttpContext context, CancellationToken cancellationToken) =>
            //{
            //    var controller = context.RequestServices.GetRequiredService<Mp3Controller>();

            //    // Read the request body to get the list of file IDs to merge
            //    using var reader = new StreamReader(context.Request.Body);
            //    var requestBody = await reader.ReadToEndAsync(cancellationToken);
            //    var fileIds = JsonSerializer.Deserialize<List<string>>(requestBody,
            //        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            //    await controller.MergeMp3Files(context, fileIds, cancellationToken);
            //}).WithMetadata(new { Name = "elevenlabs.mp3.merge" });
        }
    }
}