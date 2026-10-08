using Domain.Entities.Core;

namespace Domain.Entities.Auth;

/// <summary>
/// امتداد واحد لواحد لـ User. ProfileFileAssetId يربط صورة المستخدم بـ FileAsset
/// وهو امتداد مقصود على التصميم الرسمي بدل OwnerType وOwnerId.
/// </summary>
public class UserProfile
{
    public string UserId { get; set; } = null!;
    public string EmployeeNumber { get; set; } = null!;
    public string RankOrTitle { get; set; } = null!;
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? ProfileFileAssetId { get; set; }

    public User User { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
    public FileAsset? ProfileFileAsset { get; set; }
}
