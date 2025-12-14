namespace ArchiveProject.Domain.Entities.Auth
{
    public class UserRefreshToken
    {
        public Guid Id { get; set; }           // المفتاح الأساسي
        public string UserId { get; set; }     // FK إلى Users
        public string Token { get; set; }      // نص الـ Refresh Token
        public DateTime ExpiryDate { get; set; }  // تاريخ انتهاء الصلاحية
        public bool IsRevoked { get; set; }       // هل تم إلغاؤه
    }
}

