using Microsoft.AspNetCore.Mvc;
using MyTts.Controllers;
using MyTts.Models;

namespace MyTts.Routes
{
    public static class ApiRoutes
    {
        private const string BaseRoute = "api/mp3";
        private const string ApiName = "elevenlabs.mp3";
        private const string CorsPolicy = "AllowLocalDevelopment";

        public static void RegisterMp3Routes(IEndpointRouteBuilder endpoints)
        {
            // Create base route group with common settings
            var mp3Group = endpoints.MapGroup(BaseRoute)
                .WithTags("MP3");  // Add tag for Swagger documentation

            // Create CORS-enabled route group
            var corsRoutes = mp3Group.MapGroup(string.Empty)
                .RequireCors(CorsPolicy);

            RegisterCreationRoutes(mp3Group);
            RegisterRetrievalRoutes(mp3Group, corsRoutes);
            RegisterStreamingRoutes(corsRoutes);
        }

        /// <summary>
        /// Registers routes for MP3 creation operations
        /// </summary>
        private static void RegisterCreationRoutes(IEndpointRouteBuilder group)
        {
            // Create single MP3
            group.MapPost("one",
                async (HttpContext context, [FromBody] OneRequest request,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.One(context, request, token);
                })
                .WithName($"{ApiName}.one")
                .WithDisplayName("Create Single MP3")
                .Accepts<OneRequest>("application/json")
                .Produces(200)
                .ProducesProblem(400)
                .ProducesProblem(500);

            // Create multiple MP3s
            group.MapPost("list",
                async (HttpContext context, [FromBody] ListRequest request,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.List(context, request, token);
                })
                .WithName($"{ApiName}.list")
                .WithDisplayName("Create Multiple MP3s")
                .Accepts<ListRequest>("application/json")
                .Produces(200)
                .ProducesProblem(400)
                .ProducesProblem(500);
        }

        /// <summary>
        /// Registers routes for MP3 retrieval operations
        /// </summary>
        private static void RegisterRetrievalRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder corsGroup)
        {
            // Get feed
            group.MapGet("feed/{language}",
                async (HttpContext context, string language, [FromQuery] int? limit,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.Feed(context, language, limit ?? 20, token);
                })
                .WithName($"{ApiName}.feed")
                .WithDisplayName("Get MP3 Feed")
                .Produces(200)
                .ProducesProblem(500);

            // Get last MP3 for language
            group.MapGet("last/{language}",
                async (HttpContext context, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetLast(context, language, token);
                })
                .WithName($"{ApiName}.getlast")
                .WithDisplayName("Get Last MP3 For Language")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Get single file
            corsGroup.MapGet("one/{language}/{id}",
                async (HttpContext context, int id, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetFile(context, id, language, token);
                })
                .WithName($"{ApiName}.getone")
                .WithDisplayName("Get MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Get merged file
            corsGroup.MapGet("merged/{language}",
                async (HttpContext context, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.GetFilem(context, language, token);
                })
                .WithName($"{ApiName}.getmergedone")
                .WithDisplayName("Get Merged MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);
        }

        /// <summary>
        /// Registers routes for MP3 streaming and download operations
        /// </summary>
        private static void RegisterStreamingRoutes(IEndpointRouteBuilder corsGroup)
        {
            // Download file
            corsGroup.MapGet("ones/{id}",
                async (HttpContext context, int id, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.DownloadFile(context, id, language, token);
                })
                .WithName($"{ApiName}.getones")
                .WithDisplayName("Download MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Stream text-to-speech
            corsGroup.MapGet("onesay/{id}",
                async (HttpContext context, int id, string language,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.SayText(context, id, language, token);
                })
                .WithName($"{ApiName}.getonesay")
                .WithDisplayName("Stream Text-to-Speech")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Stream alternative text-to-speech
            corsGroup.MapGet("onesaysit/{id}",
                async (HttpContext context, int id,
                      [FromServices] Mp3Controller controller, CancellationToken token) =>
                {
                    await controller.SayitText(context, id, token);
                })
                .WithName($"{ApiName}.getonesaysit")
                .WithDisplayName("Stream Alternative Text-to-Speech")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);
        }
    }
}