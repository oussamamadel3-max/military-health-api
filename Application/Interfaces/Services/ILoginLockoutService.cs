using Domain.Entities.Auth;

namespace Application.Interfaces.Services;

public interface ILoginLockoutService
{
    int GetRetryAfterSeconds(User user);

    /// <summary>يصفر الـ tier بعد فترة هدوء منذ آخر LockoutEnd إن لم يكن الحساب محظورًا.</summary>
    Task EnsureTierDecayAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// يطبّق حظرًا تصاعديًا آمنًا ضد السباق ويعيد Retry-After بالثواني (≥ 1).
    /// </summary>
    Task<int> ApplyProgressiveLockoutAsync(string userId, CancellationToken cancellationToken = default);

    Task ResetAfterSuccessfulLoginAsync(User user, CancellationToken cancellationToken = default);
}
