using Application.Interfaces.Services;

namespace Application.Services.AuthServices
{
    public class CurrentUserService : ICurrentUserService
    {
        public string UserId { get; private set; } = "0";
        public string UserName { get; private set; } = "Anonymous";
        /// <summary>Empty until middleware or seed context sets a real value; never invent a loopback client IP.</summary>
        public string IPAddress { get; private set; } = string.Empty;
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

