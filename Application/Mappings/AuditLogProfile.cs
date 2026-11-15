using Application.Dtos.DashboardDtos;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities.Audit;
using Mapster;

namespace Application.Mappings
{
    public class AuditLogProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<AuditEvent, AuditLogListDto>()
                .Map(dest => dest.LogId, src => src.EventId)
                .Map(dest => dest.UserId, src => src.ActorUserId ?? string.Empty)
                .Map(dest => dest.UserName, src => string.Empty)
                .Map(dest => dest.ActionType, src => src.EventType)
                .Map(dest => dest.TableName, src => src.EntityType ?? string.Empty)
                .Map(dest => dest.EntityId, src => src.EntityId)
                .Map(dest => dest.CenterId, src => src.CenterId)
                .Map(dest => dest.Result, src => src.Result)
                .Map(dest => dest.ContextScope, src => src.ContextScope)
                .Map(dest => dest.ChangeSummary, src => src.ReasonText)
                .Map(dest => dest.Timestamp, src => src.OccurredAt)
                .Map(dest => dest.IPAddress, src => src.IPAddress == null ? null : src.IPAddress.ToString());

            config.NewConfig<AuditEvent, AuditLogDetailsDto>()
                .Map(dest => dest.LogId, src => src.EventId)
                .Map(dest => dest.UserId, src => src.ActorUserId ?? string.Empty)
                .Map(dest => dest.UserName, src => string.Empty)
                .Map(dest => dest.ActionType, src => src.EventType)
                .Map(dest => dest.TableName, src => src.EntityType ?? string.Empty)
                .Map(dest => dest.EntityId, src => src.EntityId)
                .Map(dest => dest.CenterId, src => src.CenterId)
                .Map(dest => dest.Result, src => src.Result)
                .Map(dest => dest.ContextScope, src => src.ContextScope)
                .Map(dest => dest.ChangeSummary, src => src.ReasonText)
                .Map(dest => dest.Timestamp, src => src.OccurredAt)
                .Map(dest => dest.IPAddress, src => src.IPAddress == null ? null : src.IPAddress.ToString())
                .Map(dest => dest.OldValue, src => src.OldValues)
                .Map(dest => dest.NewValue, src => src.NewValues)
                .Ignore(dest => dest.ChangedFields);

            config.NewConfig<AuditEvent, RecentActivityDto>()
                .Map(dest => dest.Id, src => src.EventId)
                .Map(dest => dest.UserId, src => src.ActorUserId ?? string.Empty)
                .Map(dest => dest.UserName, src => string.Empty)
                .Map(dest => dest.Action, src => src.EventType)
                .Map(dest => dest.EntityId, src => src.EntityId)
                .Map(dest => dest.CenterId, src => src.CenterId)
                .Map(dest => dest.Result, src => src.Result)
                .Map(dest => dest.Timestamp, src => src.OccurredAt.ToString("O"));
        }
    }
}
