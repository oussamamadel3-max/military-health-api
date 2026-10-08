namespace Application.Interfaces.Services;

public interface ICrossCenterAccessService
{
    Task RecordAsync(
        Guid targetCenterId,
        string accessReason,
        string dataType,
        string? queryCriteria,
        string? entityType,
        string? entityId,
        CancellationToken cancellationToken = default);
}
