using Microsoft.AspNetCore.Mvc;
using MyTts.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MyTts.Routes
{
    public static class TestErrorRoutes
    {
        private const string BaseRoute = "testerror";
        private const string ApiName = "testerror";
        private class TestErrorRoutesLoggerCategory { }

        public static void RegisterTestErrorRoutes(IEndpointRouteBuilder endpoints)
        {
            var testErrorGroup = endpoints.MapGroup(BaseRoute)
                .WithTags("Test Error")
                .WithMetadata(new AllowAnonymousAttribute())
                .DisableAntiforgery();

            // General runtime exception
            testErrorGroup.MapGet("general-error",
                async ([FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating general runtime error");
                    throw new InvalidOperationException("This is a simulated general runtime error in business logic.");
                })
                .WithName($"{ApiName}.general-error")
                .WithDisplayName("Simulate General Error")
                .Produces(500)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // I/O error
            testErrorGroup.MapGet("io-error",
                async ([FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating I/O error");
                    string path = Path.Combine(Path.GetTempPath(), "nonexistent_dir_12345", "test.txt");
                    System.IO.File.WriteAllText(path, "This should fail.");
                    return Results.Ok("This should not be reached.");
                })
                .WithName($"{ApiName}.io-error")
                .WithDisplayName("Simulate I/O Error")
                .Produces(500)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Network error
            testErrorGroup.MapGet("network-error",
                async ([FromServices] IHttpClientFactory httpClientFactory,
                       [FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating network error");
                    var client = httpClientFactory.CreateClient();
                    var response = await client.GetAsync("http://localhost:9999/non-existent-api");
                    response.EnsureSuccessStatusCode();
                    return Results.Ok("Network call successful (should not happen).");
                })
                .WithName($"{ApiName}.network-error")
                .WithDisplayName("Simulate Network Error")
                .Produces(502)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Timeout error
            testErrorGroup.MapGet("timeout-error",
                async ([FromServices] IHttpClientFactory httpClientFactory,
                       [FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating timeout error");
                    var client = httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromMilliseconds(10);
                    try
                    {
                        await client.GetAsync("https://httpbin.org/delay/1");
                    }
                    catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("External service call timed out.", ex);
                    }
                    return Results.Ok("Timeout test successful (should not happen).");
                })
                .WithName($"{ApiName}.timeout-error")
                .WithDisplayName("Simulate Timeout Error")
                .Produces(504)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Custom not found
            testErrorGroup.MapGet("custom-not-found",
                async ([FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating custom not found error");
                    throw new NotFoundException("The requested item was not found in our system.");
                })
                .WithName($"{ApiName}.custom-not-found")
                .WithDisplayName("Simulate Custom Not Found")
                .Produces(404)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Custom bad request
            testErrorGroup.MapGet("custom-bad-request",
                async ([FromQuery] string input,
                       [FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Testing custom bad request with input: {Input}", input);
                    if (string.IsNullOrEmpty(input))
                    {
                        throw new BadRequestException("Input parameter 'input' cannot be empty.");
                    }
                    return Results.Ok($"Input received: {input}");
                })
                .WithName($"{ApiName}.custom-bad-request")
                .WithDisplayName("Simulate Custom Bad Request")
                .Produces(200)
                .Produces(400)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Unauthorized access
            testErrorGroup.MapGet("unauthorized-access",
                async ([FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating unauthorized access error");
                    throw new UnauthorizedAccessException("You do not have sufficient permissions to access this resource.");
                })
                .WithName($"{ApiName}.unauthorized-access")
                .WithDisplayName("Simulate Unauthorized Access")
                .Produces(403)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());

            // Success case
            testErrorGroup.MapGet("success",
                async ([FromServices] ILogger<TestErrorRoutesLoggerCategory> logger) =>
                {
                    logger.LogInformation("Simulating successful operation");
                    return Results.Ok("Operation successful!");
                })
                .WithName($"{ApiName}.success")
                .WithDisplayName("Simulate Success")
                .Produces(200)
                .AllowAnonymous()
                .WithMetadata(new AllowAnonymousAttribute());
        }
    }
}