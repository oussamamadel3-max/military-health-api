namespace ArchiveProject.Application.Dtos.AuthDtos;

public class TokenGenerationResultDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}


