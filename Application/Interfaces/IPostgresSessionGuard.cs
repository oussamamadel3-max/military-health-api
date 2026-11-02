namespace Application.Interfaces;

/// <summary>
/// يضبط إعدادات جلسة PostgreSQL المحلية للمعاملة كما تتطلب كائنات
/// <c>InitialDatabaseObjects</c> (مثل <c>app.suppressRevision</c> و <c>app.changeReason</c>).
/// على المزودات غير العلائقية (InMemory) يكون لا-عمليات.
/// </summary>
public interface IPostgresSessionGuard
{
    /// <summary>
    /// true عندما يكتب trigger قاعدة البيانات سجل <c>finalDecisionHistory</c>
    /// تلقائياً عند تغيّر <c>resultStatusId</c>؛ عندها يجب ألا يُدرج التطبيق صفاً موازياً.
    /// </summary>
    bool DefersFinalDecisionHistoryToDatabase { get; }

    /// <summary>
    /// يطبّق إعدادات الجلسة داخل المعاملة المفتوحة حالياً. يجب استدعاؤه بعد
    /// <see cref="IUnitOfWork.BeginTransactionAsync"/> وقبل <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    Task ApplyForFinalDecisionWriteAsync(
        string currentUserId,
        string? changeReason = null,
        bool suppressRevision = false,
        CancellationToken cancellationToken = default);
}
