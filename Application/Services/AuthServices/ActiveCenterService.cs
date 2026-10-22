using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;

namespace Application.Services.AuthServices;

public class ActiveCenterService : IActiveCenterService
{
    private readonly IAccessContextRepository _access;
    private readonly ICurrentUserService _currentUser;
    private readonly ITokenService _tokens;

    public ActiveCenterService(
        IAccessContextRepository access,
        ICurrentUserService currentUser,
        ITokenService tokens)
    {
        _access = access;
        _currentUser = currentUser;
        _tokens = tokens;
    }

    public async Task<IReadOnlyList<AssignedCenterDto>> GetMyCentersAsync(CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        return await _access.GetActiveCentersAsync(userId, cancellationToken);
    }

    public async Task<TokenGenerationResultDto> SelectAsync(Guid? centerId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUser();
        var snapshot = await _access.LoadAsync(userId, cancellationToken);

        if (centerId is null)
        {
            if (!CenterPermissionEvaluator.HasCentralAuthority(snapshot.Roles, snapshot.Directs))
                throw new ForbiddenPermissionException("لا توجد صلاحية مركزية تسمح بالعمل دون مركز.");
        }
        else if (!await _access.IsActivelyAssignedAsync(userId, centerId.Value, cancellationToken))
        {
            throw new ForbiddenPermissionException("المستخدم غير مسند إلى هذا المركز أو أن الإسناد غير فعال.");
        }

        return await _tokens.GenerateTokensAsync(userId, centerId);
    }

    private string RequireUser()
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId) || _currentUser.UserId == "0")
            throw new UnauthenticatedUserException();
        return _currentUser.UserId;
    }
}
