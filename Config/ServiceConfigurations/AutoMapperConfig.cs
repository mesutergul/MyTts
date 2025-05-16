using AutoMapper;
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
            config.CreateMap<Mp3Meta, Mp3Dto>().ReverseMap();
            config.CreateMap<News, INews>().ReverseMap();
            config.CreateMap<News, HaberSummaryDto>();
        }, typeof(AutoMapperConfig).Assembly);

        return services;
    }
} 