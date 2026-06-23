namespace Domain.Common
{
    // فقط اجعل اي كلاس يرث منه لتحقيقه
    public abstract class BaseEntity
    {
        public DateTime CreatedAt { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string? UpdatedBy { get; set; }
    }
}

