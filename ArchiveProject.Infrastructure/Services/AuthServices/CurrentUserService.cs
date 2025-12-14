using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;

namespace ArchiveProject.Infrastructure.Services.AuthServices
{
    public class CurrentUserService : ICurrentUserService
    {
        public string UserId { get; private set; } = "0";
        public string UserName { get; private set; } = "Anonymous";
        public string IPAddress { get; private set; } = "::1";

        public void SetUser(string userId, string userName, string ipAddress)
        {
            UserId = userId;
            UserName = userName;
            IPAddress = ipAddress;
        }
    }
}

