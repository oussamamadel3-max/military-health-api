namespace Application.Interfaces.Services
{
    public interface ICurrentUserService
    {
        string UserId { get; }
        string UserName { get; }
        string IPAddress { get; }
        string? UserAgent { get; }
        Guid CorrelationId { get; }

        void SetUser(string userId, string userName, string ipAddress);
        void SetRequest(string? userAgent, Guid correlationId);
    }
}

