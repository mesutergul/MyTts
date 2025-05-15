using AutoMapper;
using MyTts.Data.Entities;
using MyTts.Models;

namespace MyTts.Data
{
    public class Mp3MappingProfile : Profile
    {
        public Mp3MappingProfile()
        {
            //CreateMap<Mp3Meta, Mp3Dto>();
            //CreateMap<Mp3Dto, Mp3Meta>();

            CreateMap<Mp3Meta, Mp3Dto>()
                .ForMember(dest => dest.FileId, opt => opt.MapFrom(src => src.FileId))
                .ForMember(dest => dest.FileUrl, opt => opt.MapFrom(src => src.FileUrl))
                .ForMember(dest => dest.Language, opt => opt.MapFrom(src => src.Language))
                .ForMember(dest => dest.CreatedDate, opt => opt.MapFrom(src => src.CreatedDate))
                .ForMember(dest => dest.Enabled, opt => opt.MapFrom(src => src.Enabled));

            CreateMap<Mp3Dto, Mp3Meta>()
                .ForMember(dest => dest.FileId, opt => opt.MapFrom(src => src.FileId))
                .ForMember(dest => dest.FileUrl, opt => opt.MapFrom(src => src.FileUrl))
                .ForMember(dest => dest.Language, opt => opt.MapFrom(src => src.Language))
                .ForMember(dest => dest.CreatedDate, opt => opt.MapFrom(src => src.CreatedDate))
                .ForMember(dest => dest.Enabled, opt => opt.MapFrom(src => src.Enabled));

        }
    }

}
