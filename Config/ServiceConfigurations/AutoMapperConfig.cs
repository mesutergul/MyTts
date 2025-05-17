using MyTts.Data;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;

namespace MyTts.Config.ServiceConfigurations;

public static class AutoMapperConfig
{
    public static IServiceCollection AddAutoMapperServices(this IServiceCollection services)
    {
        services.AddAutoMapper(config =>
        {
            config.AddProfile<Mp3MappingProfile>();
            config.AddProfile<HaberMappingProfile>();
            config.CreateMap<News, NewsDto>().ReverseMap();
        }, typeof(AutoMapperConfig).Assembly);

        return services;
    }
} 
