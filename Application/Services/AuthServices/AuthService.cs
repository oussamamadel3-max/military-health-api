using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionRepository _permissionRepository;
        private readonly IMapper _mapper;

        private static readonly TimeSpan FailDelay = TimeSpan.FromMilliseconds(250);
        private static readonly string DummyHash =
            new PasswordHasher<User>().HashPassword(null!, "Dummy@123456");

        public AuthService(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IPasswordHasher<User> passwordHasher,
            ITokenService tokenService,
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IPermissionRepository permissionRepository,
            IMapper mapper)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _signInManager = signInManager;
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _permissionRepository = permissionRepository;
            _mapper = mapper;
        }

        public async Task<(TokenGenerationResultDto Tokens, string UserId)> LoginAsync(RequestToken requestToken)
        {
            var username = (requestToken.UserName ?? string.Empty).Trim();
            var password = requestToken.PassWord ?? string.Empty;

            var user = await _userManager.FindByNameAsync(username);

            if (user == null)
            {
                _passwordHasher.VerifyHashedPassword(null!, DummyHash, password);
                await Task.Delay(FailDelay);
                throw new InvalidCredentialsException();
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                await Task.Delay(FailDelay);
                throw new InvalidCredentialsException();
            }

            var signIn = await _signInManager.CheckPasswordSignInAsync(
                user, password, lockoutOnFailure: true);

            if (!signIn.Succeeded)
            {
                await Task.Delay(FailDelay);
                throw new InvalidCredentialsException();
            }

            if (!user.IsActive)
            {
                await Task.Delay(FailDelay);
                throw new InvalidCredentialsException();
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            var tokens = await _tokenService.GenerateTokensAsync(user.Id);
            return (tokens, user.Id);
        }

        public async Task<MeResponseDto> GetMeAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthenticatedUserException();

            var user = await _userRepository.QueryUsers(asNoTracking: true)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null || !user.IsActive)
                throw new UnauthenticatedUserException();

            var associations = await _userRepository.GetRoleAssociationsAsync(userId, asNoTracking: true);
            var roleIds = associations.Select(ura => ura.RoleId).Distinct().ToList();

            var roles = await _roleRepository.QueryRoles()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Name!)
                .Where(x => x != null && x != "")
                .Distinct()
                .ToListAsync();

            var rolePerms = await _permissionRepository.GetPermissionNamesViaRolesAsync(userId);
            var directPerms = await _userRepository.GetDirectPermissionNamesAsync(userId, activeOnly: false);

            var permissions = directPerms
                .Concat(rolePerms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var me = _mapper.Map<MeResponseDto>(user);
            me.Roles = roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            me.Permissions = permissions;
            return me;
        }

        public Task<string> ValidateAndGetUserIdAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthenticatedUserException();

            return Task.FromResult(userId);
        }

        public async Task<(TokenResponseDto Tokens, string UserId)> RefreshSessionAsync(string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidSessionException("رمز التحديث مفقود.");

            var tokens = await _tokenService.RefreshAccessTokenAsync(refreshToken);
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
