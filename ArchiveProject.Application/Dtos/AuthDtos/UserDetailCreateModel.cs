namespace ArchiveProject.Application.Dtos.AuthDtos;
public class UserDetailCreateModel
{
    public string? UserId { get; set; }
    public DateTime CreationDate { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public IList<string> Permissions { get; set; } = null!;
}
