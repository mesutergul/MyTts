using Microsoft.AspNetCore.Identity;
using MyTts.Data.Context;
using MyTts.Models.Auth;
using MyTts.Services;
using MyTts.Services.Interfaces;

namespace MyTts.Config.ServiceConfigurations;

public static class ApiConfig
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        // Add CORS policies
        services.AddCors(options =>
        {
            options.AddPolicy("AllowLocalDevelopment", policy =>
            {
                policy
                    .WithOrigins("http://127.0.0.1:5500", "http://localhost:5500")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("Content-Disposition")
                    .AllowCredentials();
            });
        });
 
        // Register services
        services.AddScoped<IFileStreamingService, FileStreamingService>();

        // Add API Explorer and Controllers
        services.AddEndpointsApiExplorer();
        services.AddOpenApi();
        
        // // Register controllers
        // services.AddControllers()
        //     .AddApplicationPart(typeof(Mp3Controller).Assembly)
        //     .AddControllersAsServices();

        return services;
    }
} 