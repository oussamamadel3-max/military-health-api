namespace Application.Interfaces.Services;

public interface ICurrentCenterService
{
    Guid? CenterId { get; }
    bool IsCentralContext { get; }
    string? UserId { get; }
}
