using Application.Interfaces.Services;

namespace Application.Services.AuthServices
{
    public class CurrentUserService : ICurrentUserService
    {
        public string UserId { get; private set; } = "0";
        public string UserName { get; private set; } = "Anonymous";
        public string IPAddress { get; private set; } = "::1";
        public string? UserAgent { get; private set; }
        public Guid CorrelationId { get; private set; }

        public void SetUser(string userId, string userName, string ipAddress)
        {
            UserId = userId;
            UserName = userName;
            IPAddress = ipAddress;
        }

        public void SetRequest(string? userAgent, Guid correlationId)
        {
            UserAgent = userAgent;
            CorrelationId = correlationId == Guid.Empty ? Guid.NewGuid() : correlationId;
        }
    }
}

