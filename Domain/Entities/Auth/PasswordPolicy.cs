namespace Domain.Entities.Auth;

public class PasswordPolicy
{
    public Guid PasswordPolicyId { get; set; }
    public int VersionNo { get; set; }
    public string NameAr { get; set; } = null!;
    public short MinLength { get; set; }
    public short MaxLength { get; set; }
    public bool RequireUppercase { get; set; }
    public bool RequireLowercase { get; set; }
    public bool RequireDigit { get; set; }
    public bool RequireSpecial { get; set; }
    public short HistoryDepth { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? UpdatedByUser { get; set; }
}
