using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; // For [FromBody]
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // Needed for [FromServices]
using System;
using System.Threading;
using System.Threading.Tasks;
using MyTts.Models;
using MyTts.Services.Interfaces;

namespace MyTts.Routes
{
    public static class ApiRoutes
    {
        private const string BaseRoute = "api/mp3";
        private const string ApiName = "elevenlabs.mp3";
        private const string CorsPolicy = "AllowLocalDevelopment"; // Ensure this is defined in your CORS policies

        // For the logger category (similar to AuthApiRoutes)
        private class Mp3ApiRoutesLoggerCategory { }

        public static void RegisterMp3Routes(IEndpointRouteBuilder endpoints)
        {
            // Create base route group with common settings
            var mp3Group = endpoints.MapGroup(BaseRoute)
                .WithTags("MP3");

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
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        // No ModelState.IsValid in Minimal APIs, validate manually if needed
                        // if (!MiniValidation.MiniValidator.TryValidate(request, out var validationErrors)) { return Results.ValidationProblem(validationErrors); }

                        var result = await mp3Service.CreateSingleMp3Async(request, AudioType.M4a, token);
                        return Results.Ok(result);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing single MP3 request");
                        return Results.Problem("Failed to process MP3 request");
                    }
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
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var feed = await mp3Service.GetFeedByLanguageAsync(request, token);
                        return Results.Ok(feed);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error retrieving feed for language {Language}", request.Language);
                        return Results.Problem("Failed to retrieve feed");
                    }
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
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var mergedFilePath = await mp3Service.CreateMultipleMp3Async(language, limit ?? 20, AudioType.Mp3, token);

                        if (string.IsNullOrEmpty(mergedFilePath))
                        {
                            return Results.NotFound(new { message = "No files were processed." });
                        }

                        return Results.Ok(new
                        {
                            message = "Processed request of creation of voice files successfully.",
                            filePath = mergedFilePath
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogDebug("Client cancelled response for feed in language {Language}.", language);
                        return Results.StatusCode(499); // Client closed request
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing MP3 feed request");
                        return Results.Problem("An error occurred while processing the request.");
                    }
                })
                .RequireAuthorization("AdminOnly")
                .WithName($"{ApiName}.feed")
                .WithDisplayName("Get MP3 Feed")
                .Produces(200)
                .ProducesProblem(500)
                .ProducesProblem(404)
                .ProducesProblem(499); // Custom status code

            // Get last MP3 for language
            group.MapGet("last/{language}",
                async (HttpContext context, string language,
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var lastFile = await mp3Service.GetLastMp3ByLanguageAsync(language, AudioType.Mp3, token);
                        if (lastFile == null)
                        {
                            return Results.NotFound();
                        }
                        return Results.Ok(lastFile);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error retrieving last MP3 for language {Language}", language);
                        return Results.Problem("Failed to retrieve last MP3");
                    }
                })
                .WithName($"{ApiName}.getlast")
                .WithDisplayName("Get Last MP3 For Language")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Get single file (streaming)
            corsGroup.MapGet("one/{language}/{id}",
                async (HttpContext context, int id, string language,
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] IFileStreamingService streamer,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var stream = await mp3Service.GetAudioFileStream(id, language, AudioType.Mp3, false, token);
                        var fileName = $"speech_{id}.mp3";

                        // Directly use the streamer service, as it writes to HttpContext.Response
                        await streamer.StreamAsync(
                            context,
                            stream,
                            fileName,
                            "audio/mpeg",
                            token
                        );
                        return Results.Empty; // Indicate that response was handled directly
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error streaming MP3 file with ID {Id}", id);
                        return Results.Problem("Failed to stream MP3 file");
                    }
                })
                .WithName($"{ApiName}.getone")
                .WithDisplayName("Get MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Get merged file (streaming)
            corsGroup.MapGet("merged/{language}",
                async (HttpContext context, string language,
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] IFileStreamingService streamer,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var stream = await mp3Service.GetAudioFileStream(0, language, AudioType.Mp3, true, token);
                        var fileName = $"merged.mp3";

                        await streamer.StreamAsync(
                            context,
                            stream,
                            fileName,
                            "audio/mpeg",
                            token
                        );
                        return Results.Empty; // Indicate that response was handled directly
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error streaming merged MP3 file for language {Language}", language);
                        return Results.Problem("Failed to stream merged MP3 file");
                    }
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
                async (HttpContext context, int id, string language, // 'language' param was in controller, added here
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        var fileBytes = await mp3Service.GetMp3FileBytes(id, language, AudioType.Mp3, token); // 'id' maps to 'fileName'
                        if (fileBytes == null || fileBytes.Length == 0)
                        {
                            return Results.NotFound("File not found or is empty.");
                        }

                        // Results.File automatically handles content type and headers
                        return Results.File(fileBytes, "audio/mpeg", "callrecording.mp3", true);
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogInformation("Download operation cancelled for: {Id}", id);
                        return Results.StatusCode(499); // Request cancelled
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error downloading file: {Id}", id);
                        return Results.StatusCode(500);
                    }
                })
                .WithName($"{ApiName}.getones")
                .WithDisplayName("Download MP3 File")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500)
                .ProducesProblem(499); // Custom status code

            // Stream text-to-speech
            corsGroup.MapGet("onesay/{id}",
                async (HttpContext context, int id, string language,
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    var request = new OneRequest
                    {
                        News = id,
                        Language = language,
                    };
                    try
                    {
                        await using var resultStream = await mp3Service.CreateSingleMp3Async(request, AudioType.Mp3, token);
                        return Results.Stream(resultStream, "audio/mpeg", $"{id}.mp3", enableRangeProcessing: true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "SayText Error processing MP3 request");
                        return Results.Problem("Failed to process MP3 request");
                    }
                })
                .WithName($"{ApiName}.getonesay")
                .WithDisplayName("Stream Text-to-Speech")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);

            // Stream alternative text-to-speech
            corsGroup.MapGet("onesaysit/{id}",
                async (HttpContext context, int id,
                       [FromServices] IMp3Service mp3Service,
                       [FromServices] ILogger<Mp3ApiRoutesLoggerCategory> logger,
                       CancellationToken token) =>
                {
                    try
                    {
                        await mp3Service.GetNewsList(token); // This method currently doesn't return a stream or result
                                                             // If it's meant to trigger a background process, return OK.
                                                             // If it's meant to stream data, it needs to write to context.Response or return a stream.
                        logger.LogInformation("SayItText completed for ID {Id}. Assuming background process or no direct response.", id);
                        return Results.Ok(new { message = "SayItText process initiated." }); // Return a 200 OK
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "SayitText Error processing request for ID {Id}", id);
                        return Results.Problem("Failed to process SayitText request");
                    }
                })
                .WithName($"{ApiName}.getonesaysit")
                .WithDisplayName("Stream Alternative Text-to-Speech")
                .Produces(200)
                .ProducesProblem(404)
                .ProducesProblem(500);
        }
    }
}