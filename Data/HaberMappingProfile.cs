using AutoMapper;
using MyTts.Data.Entities;
using MyTts.Models;

namespace MyTts.Data
{
    public class HaberMappingProfile : Profile
    {
        public HaberMappingProfile()
        {
            // Mapping configuration for HaberKonumlari to HaberSummaryDto
            CreateMap<HaberKonumlari, HaberSummaryDto>()
                .ForMember(dest => dest.IlgiId, opt => opt.MapFrom(src => src.IlgiId))
                .ForMember(dest => dest.Baslik, opt => opt.MapFrom(src =>
                    src.News != null ? src.News.Title : string.Empty))
                .ForMember(dest => dest.Ozet, opt => opt.MapFrom(src =>
                    src.News != null ? src.News.Summary : string.Empty));
        }
    }
}
