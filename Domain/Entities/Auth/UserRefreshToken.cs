namespace Domain.Entities.Auth
{
    /// <summary>
    /// رمز التحديث الحالي بعد توسيعه بحقول العائلة والإلغاء.
    /// Token وExpiryDate وIsRevoked تبقى لأن مسارات الجلسة والاختبارات تعتمد عليها.
    /// Token يخزّن التجزئة، وRevokedAt يرافق IsRevoked.
    /// </summary>
    public class UserRefreshToken
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = null!;
        public string Token { get; set; } = null!;
        public Guid TokenFamilyId { get; set; } = Guid.NewGuid();
        public Guid? ParentRefreshTokenId { get; set; }
        public string? SecurityStamp { get; set; }
        public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTime ExpiryDate { get; set; }
        public bool IsRevoked { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
        public Guid? ReplacedByRefreshTokenId { get; set; }

        public User? User { get; set; }
        public UserRefreshToken? ParentRefreshToken { get; set; }
        public UserRefreshToken? ReplacedByRefreshToken { get; set; }
    }
}
