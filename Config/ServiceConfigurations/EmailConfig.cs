using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services;
using MyTts.Services.Interfaces;

namespace MyTts.Config.ServiceConfigurations;

public static class EmailServiceConfig
{
    public static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailConfig>(configuration.GetSection("Email"));
        services.AddScoped<IEmailService, EmailService>();
        return services;
    }
} 