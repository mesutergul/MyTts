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
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        // Allow token via query string for streaming endpoints
                        if (!string.IsNullOrEmpty(accessToken) &&
                            (path.StartsWithSegments("/mp3/stream") || path.StartsWithSegments("/mp3/download")))
                        {
                            context.Token = accessToken;
                        }
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

                // Custom policy for API access
                options.AddPolicy("ApiAccess", policy =>
                    policy.RequireAuthenticatedUser()
                          .RequireClaim("scope", "api_access"));
            });

            // Register authentication services
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();

            return services;
        }
    }
}