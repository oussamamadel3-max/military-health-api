using Application.Dtos.DashboardDtos;
using Application.Dtos.ReportDtos;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities;
using Mapster;

namespace Application.Mappings
{
    public class AuditLogProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<AuditLog, AuditLogListDto>();
            config.NewConfig<AuditLog, AuditLogDetailsDto>();
            config.NewConfig<CreateAuditLogDto, AuditLog>()
                .Map(dest => dest.Timestamp, _ => DateTime.Now);

            config.NewConfig<AuditLog, AuditLogReportRowDto>()
                .Map(dest => dest.LogId, src => src.LogId.ToString())
                .Map(dest => dest.UserName, src => src.UserName ?? string.Empty)
                .Map(dest => dest.ActionType, src => src.ActionType ?? string.Empty)
                .Map(dest => dest.TableName, src => src.TableName ?? string.Empty)
                .Map(dest => dest.Timestamp, src => src.Timestamp);

            config.NewConfig<AuditLog, RecentActivityDto>()
                .Map(dest => dest.Id, src => src.LogId)
                .Map(dest => dest.UserId, src => src.UserId ?? string.Empty)
                .Map(dest => dest.UserName, src => src.UserName ?? string.Empty)
                .Map(dest => dest.Action, src => src.ActionType ?? string.Empty)
                .Map(dest => dest.RecordId, src => src.RecordId)
                .Map(dest => dest.Timestamp, src => src.Timestamp.ToString("O"));
        }
    }
}
