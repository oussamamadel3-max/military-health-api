namespace Domain.Interfaces
{
    /// <summary>
    /// علامة على الكيانات التي يجب تسجيل إضافتها/تعديلها/حذفها في AuditLog.
    /// </summary>
    public interface IAuditableEntity
    {
        /// <summary>
        /// معرّف رقمي مستقر للسجل (مشتق من المفتاح الأساسي حتى لو كان Guid/string).
        /// </summary>
        long GetPrimaryKey();
    }
}
