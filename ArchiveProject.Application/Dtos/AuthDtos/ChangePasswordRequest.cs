namespace ArchiveProject.Application.Dtos.AuthDtos;
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminChangePasswordRequest
{
    public string UserId { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

