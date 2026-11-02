using Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// تطبيق GUCs الخاصة بمعاملات القرار النهائي على PostgreSQL فقط.
/// </summary>
public sealed class PostgresSessionGuard : IPostgresSessionGuard
{
    private readonly AppDbContext _context;

    public PostgresSessionGuard(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public bool DefersFinalDecisionHistoryToDatabase => IsNpgsql();

    /// <inheritdoc />
    public async Task ApplyForFinalDecisionWriteAsync(
        string currentUserId,
        string? changeReason = null,
        bool suppressRevision = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsNpgsql())
            return;

        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Postgres session GUCs for final decision require an open database transaction.");
        }

        if (string.IsNullOrWhiteSpace(currentUserId))
            throw new ArgumentException("currentUserId is required.", nameof(currentUserId));

        // المعامل الثالث لـ set_config = true ⇒ LOCAL للمعاملة الحالية فقط.
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.currentUserId', {0}, true)",
            parameters: new object[] { currentUserId.Trim() },
            cancellationToken: cancellationToken);

        if (suppressRevision)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.suppressRevision', 'true', true)",
                cancellationToken: cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(changeReason))
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.changeReason', {0}, true)",
                parameters: new object[] { changeReason.Trim() },
                cancellationToken: cancellationToken);
        }
    }

    private bool IsNpgsql()
        => _context.Database.ProviderName is { } name
           && name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
}
