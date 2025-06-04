using Microsoft.AspNetCore.Mvc; // For [FromBody]
using MyTts.Services.Interfaces; // Assuming IAuthService is here
using System.Security.Claims;
using MyTts.Models.Auth;

namespace MyTts.Routes
{
    public static class AuthApiRoutes
    {
        private const string BaseRoute = "api/auth";
        private const string ApiName = "auth";
        private class AuthApiRoutesLoggerCategory { }

        public static void RegisterAuthRoutes(IEndpointRouteBuilder endpoints)
        {
            var authGroup = endpoints.MapGroup(BaseRoute)
                .WithTags("Authentication"); // Add tag for Swagger documentation

            // Register
            authGroup.MapPost("register",
                async ([FromBody] RegisterRequest request,
                       [FromServices] IAuthService authService,
                       [FromServices] ILogger<AuthApiRoutesLoggerCategory> logger) =>
                {
                    var result = await authService.RegisterAsync(request);
                    if (result == null)
                    {
                        logger.LogWarning("Registration failed for user: {Email}", request.Email);
                        return Results.BadRequest(new { message = "Registration failed" });
                    }

                    logger.LogInformation("User registered successfully: {Email}", request.Email);
                    return Results.Ok(result);
                })
                .WithName($"{ApiName}.register")
                .WithDisplayName("Register User")
                .Accepts<RegisterRequest>("application/json")
                .Produces(200)
                .ProducesProblem(400);

            // Login
            authGroup.MapPost("login",
                async ([FromBody] LoginRequest request,
                       [FromServices] IAuthService authService,
                       [FromServices] ILogger<AuthApiRoutesLoggerCategory> logger) =>
                {
                    var result = await authService.LoginAsync(request);
                    if (result == null)
                    {
                        logger.LogWarning("Login failed for user: {Email}", request.Email);
                        return Results.Unauthorized(); // 401
                    }

                    logger.LogInformation("User logged in successfully: {Email}", request.Email);
                    return Results.Ok(result);
                })
                .WithName($"{ApiName}.login")
                .WithDisplayName("Login User")
                .Accepts<LoginRequest>("application/json")
                .Produces(200)
                .ProducesProblem(401)
                .ProducesValidationProblem();

            // Refresh Token
            authGroup.MapPost("refresh",
                async ([FromBody] string refreshToken,
                       [FromServices] IAuthService authService,
                       [FromServices] ILogger<AuthApiRoutesLoggerCategory> logger) =>
                {
                    if (string.IsNullOrEmpty(refreshToken))
                    {
                        return Results.BadRequest(new { message = "Refresh token is required." });
                    }

                    var result = await authService.RefreshTokenAsync(refreshToken);
                    if (result == null)
                    {
                        logger.LogWarning("Invalid refresh token provided.");
                        return Results.Unauthorized(); // 401
                    }

                    logger.LogInformation("Token refreshed successfully.");
                    return Results.Ok(result);
                })
                .WithName($"{ApiName}.refresh")
                .WithDisplayName("Refresh Token")
                .Accepts<string>("text/plain") // Or "application/json" if sending an object { "refreshToken": "..." }
                .Produces(200)
                .ProducesProblem(401)
                .ProducesProblem(400);


            // Revoke Token (Requires Authorization)
            // Note: For authorization, you need to call .RequireAuthorization() on the endpoint.
            authGroup.MapPost("revoke",
                async ([FromBody] string refreshToken,
                       [FromServices] IAuthService authService,
                       [FromServices] ILogger<AuthApiRoutesLoggerCategory> logger) =>
                {
                    if (string.IsNullOrEmpty(refreshToken))
                    {
                        return Results.BadRequest(new { message = "Refresh token is required." });
                    }

                    await authService.RevokeTokenAsync(refreshToken);
                    logger.LogInformation("Token revoked successfully.");
                    return Results.Ok(new { message = "Token revoked successfully" });
                })
                .RequireAuthorization() // Applies the [Authorize] attribute functionality
                .WithName($"{ApiName}.revoke")
                .WithDisplayName("Revoke Token")
                .Accepts<string>("text/plain")
                .Produces(200)
                .ProducesProblem(401)
                .ProducesProblem(400);

            // Get Current User Info (Requires Authorization)
            authGroup.MapGet("me",
                async (HttpContext context,
                       [FromServices] IAuthService authService,
                       [FromServices] ILogger<AuthApiRoutesLoggerCategory> logger) =>
                {
                    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        logger.LogWarning("Attempted to get user info without a valid User ID in claims.");
                        return Results.Unauthorized();
                    }

                    var userInfo = await authService.GetUserInfoAsync(userId);
                    if (userInfo == null)
                    {
                        logger.LogWarning("User info not found for userId: {UserId}", userId);
                        return Results.NotFound();
                    }

                    logger.LogInformation("Retrieved user info for userId: {UserId}", userId);
                    return Results.Ok(userInfo);
                })
                .RequireAuthorization() // Applies the [Authorize] attribute functionality
                .WithName($"{ApiName}.me")
                .WithDisplayName("Get Current User Info")
                .Produces(200)
                .ProducesProblem(401)
                .ProducesProblem(404);
        }
    }
}