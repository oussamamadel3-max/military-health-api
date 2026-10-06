namespace Domain.Entities.Auth;

public class UserContact
{
    public Guid ContactId { get; set; }
    public string UserId { get; set; } = null!;
    public string ContactType { get; set; } = null!;
    public string ContactValue { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
