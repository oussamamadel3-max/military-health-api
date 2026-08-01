using Application.Dtos;
using Application.Dtos.ReportDtos;
using Application.ModelsDto.AuditLogDtos;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings
{
    public class AuditLogProfile : Profile
    {
        public AuditLogProfile()
        {
            CreateMap<AuditLog, AuditLogListDto>();
            CreateMap<AuditLog, AuditLogDetailsDto>();
            CreateMap<CreateAuditLogDto, AuditLog>()
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(_ => DateTime.Now));

            CreateMap<AuditLog, AuditLogReportRowDto>();
        }
    }
}
