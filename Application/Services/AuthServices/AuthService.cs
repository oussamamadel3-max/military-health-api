using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
            IPermissionRepository permissionRepository)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _signInManager = signInManager;
            _passwordHasher = passwordHasher;
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _permissionRepository = permissionRepository;
        }

        public async Task<TokenGenerationResultDto> LoginAsync(RequestToken requestToken)
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
                throw new UserAccountLockedException();
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
                throw new UserAccountDisabledException();
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            return await _tokenService.GenerateTokensAsync(user.Id);
        }

        public async Task<MeResponseDto> GetMeAsync(string userId)
        {
            var user = await _userRepository.QueryUsers(asNoTracking: true)
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    u.Email,
                    u.CreationDate,
                    u.IsActive
                })
                .FirstOrDefaultAsync();

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

            return new MeResponseDto
            {
                UserId = user.Id,
                UserName = user.UserName ?? "",
                Email = user.Email ?? "",
                CreationDate = user.CreationDate,
                Roles = roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Permissions = permissions
            };
        }
    }
}
