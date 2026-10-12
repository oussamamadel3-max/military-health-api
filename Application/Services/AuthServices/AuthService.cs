using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;

namespace Application.Services.AuthServices
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly IUserRepository _userRepository;
        private readonly IAccessContextRepository _access;
        private readonly ICurrentCenterService _currentCenter;
        private readonly IAttachmentRepository _files;
        private readonly IMapper _mapper;
        private readonly ILoginLockoutService _loginLockout;
        private readonly IAuditLogRepository _auditLog;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<AuthService> _logger;

        private static readonly TimeSpan FailDelay = TimeSpan.FromMilliseconds(250);
        private static readonly string DummyHash =
            new PasswordHasher<User>().HashPassword(null!, "Dummy@123456");

        public AuthService(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IPasswordHasher<User> passwordHasher,
            ITokenService tokenService,
            IUserRepository userRepository,
            IAccessContextRepository access,
            ICurrentCenterService currentCenter,
            IAttachmentRepository files,
            IMapper mapper,
            ILoginLockoutService loginLockout,
            IAuditLogRepository auditLog,
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            ILogger<AuthService> logger)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _signInManager = signInManager;
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _access = access;
            _currentCenter = currentCenter;
            _files = files;
            _mapper = mapper;
            _loginLockout = loginLockout;
            _auditLog = auditLog;
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _logger = logger;
        }

        public async Task<LoginResult> LoginAsync(RequestToken requestToken)
        {
            var username = (requestToken.UserName ?? string.Empty).Trim();
            var password = requestToken.PassWord ?? string.Empty;

            var user = await _userManager.FindByNameAsync(username);

            if (user == null)
            {
                _passwordHasher.VerifyHashedPassword(null!, DummyHash, password);
                await Task.Delay(FailDelay);
                await RecordLoginAsync(null, false, "invalid_credentials", "فشل تسجيل الدخول", null);
                throw new InvalidCredentialsException();
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                await Task.Delay(FailDelay);
                await RecordLoginAsync(user.Id, false, "locked", "فشل تسجيل الدخول بسبب القفل المؤقت", null);
                throw new UserAccountLockedException(_loginLockout.GetRetryAfterSeconds(user));
            }

            await _loginLockout.EnsureTierDecayAsync(user);
            user = await _userManager.FindByIdAsync(user.Id);
            if (user == null)
            {
                await RecordLoginAsync(null, false, "invalid_credentials", "فشل تسجيل الدخول", null);
                throw new InvalidCredentialsException();
            }

            var signIn = await _signInManager.CheckPasswordSignInAsync(
                user, password, lockoutOnFailure: true);

            if (!signIn.Succeeded)
            {
                await Task.Delay(FailDelay);

                user = await _userManager.FindByIdAsync(user.Id);
                if (user == null)
                {
                    await RecordLoginAsync(null, false, "invalid_credentials", "فشل تسجيل الدخول", null);
                    throw new InvalidCredentialsException();
                }

                if (signIn.IsLockedOut || await _userManager.IsLockedOutAsync(user))
                {
                    var retryAfter = await _loginLockout.ApplyProgressiveLockoutAsync(user.Id);
                    await RecordLoginAsync(user.Id, false, "locked", "فشل تسجيل الدخول بسبب القفل المؤقت", null);
                    throw new UserAccountLockedException(retryAfter);
                }

                await RecordLoginAsync(user.Id, false, "invalid_credentials", "فشل تسجيل الدخول", null);
                throw new InvalidCredentialsException();
            }

            if (!user.IsActive)
            {
                await Task.Delay(FailDelay);
                await RecordLoginAsync(user.Id, false, "disabled", "فشل تسجيل الدخول لحساب غير فعّال", null);
                throw new InvalidCredentialsException();
            }

            await _loginLockout.ResetAfterSuccessfulLoginAsync(user);

            var centers = await _access.GetActiveCentersAsync(user.Id);
            var sessionCenterId = LoginCenterContext.CenterIdForNewSession(centers);
            var tokens = await _tokenService.GenerateTokensAsync(user.Id, sessionCenterId);
            var session = BuildSession(
                user,
                centers,
                sessionCenterId,
                tokens.Roles,
                tokens.Permissions);

            await RecordLoginAsync(
                user.Id,
                true,
                "succeeded",
                "تم تسجيل الدخول",
                session.ActiveCenter?.CenterId);

            return new LoginResult
            {
                Tokens = tokens,
                UserId = user.Id,
                Session = session
            };
        }

        public async Task<MeResponseDto> GetMeAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthenticatedUserException();

            var user = await _userRepository.QueryUsers(asNoTracking: true)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || !user.IsActive)
                throw new UnauthenticatedUserException();

            var centers = await _access.GetActiveCentersAsync(userId);
            var claimedCenterId = _currentCenter.IsCentralContext ? null : _currentCenter.CenterId;
            var centerId = LoginCenterContext.Describe(centers, claimedCenterId).ActiveCenter?.CenterId;
            var snapshot = await _access.LoadAsync(userId);
            var permissions = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, centerId).ToList();
            var scopedRoles = CenterPermissionEvaluator.ApplicableRoleNames(snapshot.Roles, centerId).ToList();
            var session = BuildSession(user, centers, centerId, scopedRoles, permissions);

            var me = _mapper.Map<MeResponseDto>(user);
            me.Roles = session.Roles;
            me.Permissions = session.Permissions;
            me.Centers = session.Centers;
            me.CenterStatus = session.CenterStatus;
            me.ActiveCenter = session.ActiveCenter;
            me.OperationalAccess = session.OperationalAccess;
            me.AccountMessage = session.AccountMessage;

            var profile = await _files.FindTrackedProfileAsync(user.Id);
            if (profile?.ProfileFileAssetId is Guid photoId)
            {
                var photo = await _files.FindByIdAsync(photoId);
                if (photo != null)
                {
                    me.ProfileFileAssetId = photo.FileAssetId;
                    me.ProfileFileUrl = $"/api/attachments/download/{photo.FileAssetId}";
                }
            }

            return me;
        }

        public Task<string> ValidateAndGetUserIdAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthenticatedUserException();

            return Task.FromResult(userId);
        }

        public async Task<(TokenResponseDto Tokens, string UserId)> RefreshSessionAsync(string? refreshToken, Guid? activeCenterId = null)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidSessionException("رمز التحديث مفقود.");

            var tokens = await _tokenService.RefreshAccessTokenAsync(refreshToken, activeCenterId);
            var userId = ExtractUserIdFromAccessToken(tokens.AccessToken)
                ?? throw new InvalidSessionException("تعذر استخراج بيانات المستخدم من التوكن.");

            return (tokens, userId);
        }

        public async Task LogoutAsync(string? refreshToken)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await _tokenService.RevokeRefreshTokenAsync(refreshToken);
            }
        }

        private static LoginSessionDto BuildSession(
            User user,
            IReadOnlyList<AssignedCenterDto> centers,
            Guid? sessionCenterId,
            IReadOnlyList<string> roles,
            IReadOnlyList<string> permissions)
        {
            var decision = LoginCenterContext.Describe(centers, sessionCenterId);
            var operational = LoginCenterContext.AllowsOperationalAccess(decision.Status, permissions.Count);
            return new LoginSessionDto
            {
                UserId = user.Id,
                UserName = user.UserName ?? string.Empty,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Phone = user.PhoneNumber,
                Roles = roles.ToList(),
                Permissions = permissions.ToList(),
                Centers = centers.ToList(),
                CenterStatus = decision.Status,
                ActiveCenter = decision.ActiveCenter,
                OperationalAccess = operational,
                AccountMessage = LoginCenterContext.AccountMessage(decision.Status, operational)
            };
        }

        private async Task RecordLoginAsync(
            string? userId,
            bool succeeded,
            string reasonCode,
            string reasonText,
            Guid? centerId)
        {
            if (succeeded)
                _logger.LogInformation("Login succeeded for user {UserId} with reason {Reason}", userId, reasonCode);
            else
                _logger.LogWarning("Login failed for user {UserId} with reason {Reason}", userId, reasonCode);

            var actor = string.IsNullOrWhiteSpace(userId) ? null : userId;
            var hasCenter = centerId.HasValue;
            _auditLog.Add(new AuditEvent
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActorUserId = actor,
                ContextScope = hasCenter ? CenterClaimTypes.Center : CenterClaimTypes.Central,
                CenterId = hasCenter ? centerId : null,
                EventType = "Login",
                Result = succeeded ? "SUCCESS" : "FAILURE",
                EntityType = actor == null ? null : nameof(User),
                EntityId = actor,
                ReasonText = reasonText,
                UserAgent = _currentUser.UserAgent,
                CorrelationId = _currentUser.CorrelationId == Guid.Empty
                    ? Guid.NewGuid()
                    : _currentUser.CorrelationId,
                IPAddress = IPAddress.TryParse(_currentUser.IPAddress, out var ip) ? ip : null
            });
            await _unitOfWork.SaveChangesAsync();
        }

        private static string? ExtractUserIdFromAccessToken(string accessToken)
        {
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(accessToken);
                return jwt.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")?.Value;
            }
            catch
            {
                return null;
            }
        }
    }
}
