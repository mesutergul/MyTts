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
        private const string JwtSection = "Jwt";
        private const string KeyName = "Key";
        private const string IssuerName = "Issuer";
        private const string AudienceName = "Audience";
        private const string TokenExpiredHeader = "Token-Expired";
        private const string AccessTokenQueryParam = "access_token";
        private const string Mp3StreamPath = "/mp3/stream";
        private const string Mp3DownloadPath = "/mp3/download";

        public static IServiceCollection AddAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

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
            var jwtSettings = configuration.GetSection(JwtSection);
            var key = jwtSettings[KeyName];
            var issuer = jwtSettings[IssuerName];
            var audience = jwtSettings[AudienceName];

            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("JWT Key not configured");
            }

            if (string.IsNullOrEmpty(issuer))
            {
                throw new InvalidOperationException("JWT Issuer not configured");
            }

            if (string.IsNullOrEmpty(audience))
            {
                throw new InvalidOperationException("JWT Audience not configured");
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
                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers[TokenExpiredHeader] = "true";
                        }
                        return Task.CompletedTask;
                    },
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query[AccessTokenQueryParam].FirstOrDefault();
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) &&
                            (path.StartsWithSegments(Mp3StreamPath) || path.StartsWithSegments(Mp3DownloadPath)))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    OnChallenge = context => Task.CompletedTask,
                    OnForbidden = context => Task.CompletedTask,
                    OnTokenValidated = context => Task.CompletedTask
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