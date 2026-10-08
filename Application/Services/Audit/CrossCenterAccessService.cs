using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;

namespace Application.Services.Audit;

public class CrossCenterAccessService : ICrossCenterAccessService
{
    private readonly ICrossCenterAccessRepository _repository;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentCenterService _currentCenter;
    private readonly IUnitOfWork _unitOfWork;

    public CrossCenterAccessService(
        ICrossCenterAccessRepository repository,
        ICurrentUserService currentUser,
        ICurrentCenterService currentCenter,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _currentUser = currentUser;
        _currentCenter = currentCenter;
        _unitOfWork = unitOfWork;
    }

    public async Task RecordAsync(
        Guid targetCenterId,
        string accessReason,
        string dataType,
        string? queryCriteria,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessReason))
            throw new InvalidUserDataException("سبب الاطلاع العابر مطلوب.");
        if (string.IsNullOrWhiteSpace(dataType))
            throw new InvalidUserDataException("نوع البيانات مطلوب.");
        if (_currentCenter.CenterId == targetCenterId)
            throw new InvalidUserDataException("الاطلاع ضمن المركز النشط ليس اطلاعاً عابراً.");

        var actor = string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0"
            ? null
            : _currentUser.UserId;
        var sourceCenter = _currentCenter.IsCentralContext ? null : _currentCenter.CenterId;

        var auditEvent = new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actor,
            ContextScope = sourceCenter.HasValue ? CenterClaimTypes.Center : CenterClaimTypes.Central,
            CenterId = sourceCenter,
            EventType = "READ",
            Result = "SUCCESS",
            EntityType = entityType,
            EntityId = entityId,
            ReasonText = accessReason.Trim(),
            UserAgent = _currentUser.UserAgent,
            CorrelationId = _currentUser.CorrelationId == Guid.Empty ? Guid.NewGuid() : _currentUser.CorrelationId,
            IPAddress = System.Net.IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
        };

        _repository.Add(new CrossCenterAccess
        {
            AccessId = Guid.NewGuid(),
            AuditEvent = auditEvent,
            TargetCenterId = targetCenterId,
            AccessReason = accessReason.Trim(),
            DataType = dataType.Trim(),
            QueryCriteria = queryCriteria
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
