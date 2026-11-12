using System.Transactions;
using Application.Common.Security;
using Application.Exceptions;
using Application.Interfaces.Services;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// يحفظ رفض IAM في نطاق DI مستقل، مع Suppress لأي TransactionScope محيط،
/// حتى لا يُلغى الحدث مع Rollback لطلب الكتابة المرفوض.
/// </summary>
public sealed class IamAccessDeniedAuditor : IIamAccessDeniedAuditor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IamAccessDeniedAuditor> _logger;

    public IamAccessDeniedAuditor(
        IServiceScopeFactory scopeFactory,
        ILogger<IamAccessDeniedAuditor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task TryRecordAsync(
        ICurrentUserService currentUser,
        ICurrentCenterService? currentCenter,
        ForbiddenPermissionException exception,
        Guid correlationId,
        IDictionary<object, object?> requestItems,
        CancellationToken cancellationToken = default)
    {
        if (!exception.AuditAccessDenial)
            return;

        if (requestItems.ContainsKey(IamAccessDeniedAudit.HttpItemsDedupKey))
            return;

        requestItems[IamAccessDeniedAudit.HttpItemsDedupKey] = true;

        try
        {
            var fallbackCenter = currentCenter is { IsCentralContext: false }
                ? currentCenter.CenterId
                : null;

            var auditEvent = IamAccessDeniedAudit.Create(
                currentUser,
                exception,
                correlationId,
                fallbackCenter);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditEvents.Add(auditEvent);

            // استقلال عن أي TransactionScope محيط؛ Scope DI وحده لا يكفي إن وُجد Ambient Transaction.
            using (var suppress = new TransactionScope(
                       TransactionScopeOption.Suppress,
                       TransactionScopeAsyncFlowOption.Enabled))
            {
                await db.SaveChangesAsync(cancellationToken);
                suppress.Complete();
            }
        }
        catch (Exception ex)
        {
            // لا نخفي 403 عن العميل؛ نسجّل الفشل داخلياً فقط (FR-076 A1).
            _logger.LogError(
                ex,
                "Failed to persist IAM access-denied audit. CorrelationId={CorrelationId}, Operation={Operation}",
                correlationId,
                exception.DenialOperation);
        }
    }
}
