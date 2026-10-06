namespace Domain.Interfaces
{
    /// <summary>
    /// علامة على الكيانات التي يجب تسجيل إضافتها وتعديلها وحذفها.
    /// المفتاح يُستخرج من بيانات Entity Framework حتى يدعم Guid وlong وstring.
    /// </summary>
    public interface IAuditableEntity
    {
    }
}
