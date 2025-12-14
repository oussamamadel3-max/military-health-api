namespace ArchiveProject.Application.Common.Interfaces.AuthInterfaces
{
    public interface ICurrentUserService
    {
        string UserId { get; }
        string UserName { get; }
        string IPAddress { get; }

        void SetUser(string userId, string userName, string ipAddress);
    }
}

