using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using MyTts.Data.Context;
using MyTts.Models.Auth;
using MyTts.Services;
using MyTts.Services.Interfaces;

namespace MyTts.Config.ServiceConfigurations
{
    public static class AuthConfig
    {
        public static IServiceCollection AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure Identity with your existing AuthDbContext
            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;

                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User settings
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
                options.User.RequireUniqueEmail = true;

                // For API, we typically don't require email confirmation
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

            // Configure JWT for API authentication
            var jwtSettings = configuration.GetSection("Jwt");
            var key = jwtSettings["Key"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("JWT Key not configured");
            }

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
                options.RequireAuthenticatedSignIn = false;  // Make authentication optional by default
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ClockSkew = TimeSpan.Zero
                };

                // Handle token from query string for streaming endpoints
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
            {
                // Only log and handle failures for routes where authentication IS expected
                if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                {
                    context.Response.Headers.Add("Token-Expired", "true");
                }
                // Log other authentication failures for debugging protected routes
                return Task.CompletedTask;
            },
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                // Allow token via query string ONLY for designated streaming endpoints
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/mp3/stream") || path.StartsWithSegments("/mp3/download")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // Customize 401/403 responses if needed, for PROTECTED routes
                // context.Response.ContentType = "application/json";
                // context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                // return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Authentication Required" }));
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                // Customize 403 response if needed, for PROTECTED routes
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // Add custom logic after a token is successfully validated, for PROTECTED routes
                // Example: Add custom claims, refresh token logic etc.
                return Task.CompletedTask;
            }
                };
            });

            // Configure authorization policies
            services.AddAuthorization(options =>
            {
                options.AddPolicy("RequireAuthenticatedUser", policy =>
                    policy.RequireAuthenticatedUser());

                options.AddPolicy("AdminOnly", policy =>
                    policy.RequireRole("Admin"));

                options.AddPolicy("UserOrAdmin", policy =>
                    policy.RequireRole("User", "Admin"));
            });

            // Register authentication services
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();

            return services;
        }
    }
}