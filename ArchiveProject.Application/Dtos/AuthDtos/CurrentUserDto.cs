namespace ArchiveProject.Application.Dtos.AuthDtos
{
    public class CurrentUserDto
    {
        public string UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
    }
}

