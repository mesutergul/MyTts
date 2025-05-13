using Microsoft.AspNetCore.Mvc;
using MyTts.Controllers;
using MyTts.Models;

namespace MyTts.Routes
{
    public static class ApiRoutes
    {
        public static void RegisterMp3Routes(IEndpointRouteBuilder endpoints)
        {
            // Group all routes under the same base path
            var mp3Group = endpoints.MapGroup("api/mp3")
                .WithTags("MP3"); // Add tag for Swagger documentation

            // Routes that require CORS policy
            // Define a separate group for CORS routes
            var corsRoutes = mp3Group.MapGroup(string.Empty)
                .RequireCors("AllowLocalDevelopment");

            // Feed endpoint
            mp3Group.MapGet("feed/{language}",
                async (HttpContext context, string language, [FromQuery] int? limit,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.Feed(context, language, limit ?? 20, token);
                })
                .WithName("elevenlabs.mp3.feed")
                .WithDisplayName("Get MP3 Feed")
                .Produces(200)
                .ProducesProblem(500);

            // One endpoint (create a single MP3)
            mp3Group.MapPost("one",
                async (HttpContext context, [FromBody] OneRequest request,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.One(context, request, token);
                })
                .WithName("elevenlabs.mp3.one")
                .WithDisplayName("Create Single MP3")
                .Accepts<OneRequest>("application/json")
                .Produces(200)
                .ProducesProblem(400)
                .ProducesProblem(500);

            // List endpoint (create multiple MP3s)
            mp3Group.MapPost("list",
                async (HttpContext context, [FromBody] ListRequest request,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.List(context, request, token);
                })
                .WithName("elevenlabs.mp3.list")
                .WithDisplayName("Create Multiple MP3s")
                .Accepts<ListRequest>("application/json")
                .Produces(200)
                .ProducesProblem(400)
                .ProducesProblem(500);

            // Get file endpoint
            corsRoutes.MapGet("one/{id}",
                async (HttpContext context, int id,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetFile(context, id, token);
                })
                .WithName("elevenlabs.mp3.getone")
                .WithDisplayName("Get MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            corsRoutes.MapGet("merged",
                async (HttpContext context,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetFilem(context, token);
                })
                .WithName("elevenlabs.mp3.getmergedone")
                .WithDisplayName("Get merged MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Get last MP3 for language endpoint
            mp3Group.MapGet("last/{language}",
                async (HttpContext context, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetLast(context, language, token);
                })
                .WithName("elevenlabs.mp3.getlast")
                .WithDisplayName("Get Last MP3 For Language")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Download file endpoint
            corsRoutes.MapGet("ones/{id}",
                async (HttpContext context, int id,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.DownloadFile(context, id, token);
                })
                .WithName("elevenlabs.mp3.getones")
                .WithDisplayName("Download MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Say text endpoint
            corsRoutes.MapGet("onesay/{id}",
                async (HttpContext context, int id,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.SayText(context, id, token);
                })
                .WithName("elevenlabs.mp3.getonesay")
                .WithDisplayName("Say Text")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);
            corsRoutes.MapGet("onesaysit/{id}",
                async (HttpContext context, int id,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.SayitText(context, id, token);
                })
                .WithName("elevenlabs.mp3.getonesaysit")
                .WithDisplayName("Saysit Text")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);
        }
    }
}