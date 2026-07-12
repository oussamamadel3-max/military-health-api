using Application.Common.Results;
using Application.Dtos.AuthDtos;
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

        private const string GenericFailMsg = "بيانات الدخول غير صحيحة";
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

        public async Task<Result<TokenGenerationResultDto>> LoginAsync(RequestToken requestToken)
        {
            var username = (requestToken.UserName ?? string.Empty).Trim();
            var password = requestToken.PassWord ?? string.Empty;

            // 1) ابحث عن المستخدم، لكن لا ترجع رسالة مختلفة
            var user = await _userManager.FindByNameAsync(username);

            if (user == null)
            {
                // ✅ Dummy verify لتوحيد الزمن (تقريباً)
                _passwordHasher.VerifyHashedPassword(null!, DummyHash, password);

                await Task.Delay(FailDelay);
                return Result<TokenGenerationResultDto>.Failure(new List<string> { GenericFailMsg }, null);
            }

            // 2) إذا الحساب مقفول (Lockout) لا تكشف، نفس الرسالة
            if (await _userManager.IsLockedOutAsync(user))
            {
                await Task.Delay(FailDelay);
                return Result<TokenGenerationResultDto>.Failure(new List<string> { GenericFailMsg }, null);
            }

            // 3) تحقق كلمة المرور مع lockoutOnFailure=true
            var signIn = await _signInManager.CheckPasswordSignInAsync(
                user, password, lockoutOnFailure: true);

            // فشل كلمة مرور => نفس الرسالة (وتم احتساب محاولة فاشلة تلقائياً)
            if (!signIn.Succeeded)
            {
                await Task.Delay(FailDelay);
                return Result<TokenGenerationResultDto>.Failure(new List<string> { GenericFailMsg }, null);
            }

            // 4) تحقق من التفعيل (لا تكشف أيضاً)
            if (!user.IsActive)
            {
                await Task.Delay(FailDelay);
                return Result<TokenGenerationResultDto>.Failure(new List<string> { GenericFailMsg }, null);
            }

            // ✅ نجاح: صفّر عداد الفشل (احتياط)
            await _userManager.ResetAccessFailedCountAsync(user);

            // 5) توليد التوكن
            var tokenResult = await _tokenService.GenerateTokensAsync(user.Id);
            return Result<TokenGenerationResultDto>.Success(tokenResult);
        }

        public async Task<Result<MeResponseDto>> GetMeAsync(string userId)
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

            if (user == null)
                return Result<MeResponseDto>.Failure(new[] { "غير مسموح." }, null);

            if (!user.IsActive)
                return Result<MeResponseDto>.Failure(new[] { "غير مسموح." }, null);

            var associations = await _userRepository.GetRoleAssociationsAsync(userId, asNoTracking: true);
            var roleIds = associations.Select(ura => ura.RoleId).Distinct().ToList();

            // Roles
            var roles = await _roleRepository.QueryRoles()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Name!)
                .Where(x => x != null && x != "")
                .Distinct()
                .ToListAsync();

            // Role Permissions
            var rolePerms = await _permissionRepository.GetPermissionNamesViaRolesAsync(userId);

            // Direct Permissions
            var directPerms = await _userRepository.GetDirectPermissionNamesAsync(userId, activeOnly: false);

            var permissions = directPerms
                .Concat(rolePerms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dto = new MeResponseDto
            {
                UserId = user.Id,
                UserName = user.UserName ?? "",
                Email = user.Email ?? "",
                CreationDate = user.CreationDate,
                Roles = roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Permissions = permissions
            };

            return Result<MeResponseDto>.Success(dto);
        }
    }
}
