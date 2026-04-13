using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly MainDbContext _context;


    private const string GenericFailMsg = "بيانات الدخول غير صحيحة";
    private static readonly TimeSpan FailDelay = TimeSpan.FromMilliseconds(250);
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(null!, "Dummy@123456");

    public AuthService(UserManager<User> userManager,SignInManager<User> signInManager,IPasswordHasher<User> passwordHasher,ITokenService tokenService, MainDbContext context) 
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
        _context = context;
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(RequestToken requestToken)
    {
        var email = (requestToken.Email ?? string.Empty).Trim();
        var password = requestToken.PassWord ?? string.Empty;

        // 1) ابحث عن المستخدم، لكن لا ترجع رسالة مختلفة
        var user = await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            // ✅ Dummy verify لتوحيد الزمن (تقريباً)
            _passwordHasher.VerifyHashedPassword(null!, DummyHash, password);

            await Task.Delay(FailDelay);
            return Result<LoginResponseDto>.Failure(new List<string> { GenericFailMsg }, null);
        }

        // 2) إذا الحساب مقفول (Lockout) لا تكشف، نفس الرسالة
        if (await _userManager.IsLockedOutAsync(user))
        {
            await Task.Delay(FailDelay);
            return Result<LoginResponseDto>.Failure(new List<string> { GenericFailMsg }, null);
        }

        // 3) تحقق كلمة المرور مع lockoutOnFailure=true
        var signIn = await _signInManager.CheckPasswordSignInAsync(
            user, password, lockoutOnFailure: true);

        // فشل كلمة مرور => نفس الرسالة (وتم احتساب محاولة فاشلة تلقائياً)
        if (!signIn.Succeeded)
        {
            await Task.Delay(FailDelay);
            return Result<LoginResponseDto>.Failure(new List<string> { GenericFailMsg }, null);
        }

        // 4) تحقق من التفعيل (لا تكشف أيضاً)
        if (!user.IsActive)
        {
            await Task.Delay(FailDelay);
            return Result<LoginResponseDto>.Failure(new List<string> { GenericFailMsg }, null);
        }

        // ✅ نجاح: صفّر عداد الفشل (احتياط)
        await _userManager.ResetAccessFailedCountAsync(user);

        // 5) توليد التوكن
        var tokenResult = await _tokenService.GenerateTokensAsync(user.Id);

        var response = new LoginResponseDto
        {
            AccessToken = tokenResult.AccessToken,
            RefreshToken = tokenResult.RefreshToken
        };

        return Result<LoginResponseDto>.Success(response);
    }

    public async Task<Result<MeResponseDto>> GetMeAsync(string userId)
    {
        var user = await _userManager.Users.AsNoTracking()
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

        // Roles
        var roles = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == userId)
            .Select(ura => ura.Role.Name!)
            .Where(x => x != null && x != "")
            .Distinct()
            .ToListAsync();

        // RoleIds
        var roleIds = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == userId)
            .Select(ura => ura.RoleId)
            .Distinct()
            .ToListAsync();

        // Role Permissions
        var rolePerms = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name!)
            .Where(x => x != null && x != "")
            .ToListAsync();

        // Direct Permissions
        var directPerms = await _context.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == userId)
            .Select(up => up.Permission.Name!)
            .Where(x => x != null && x != "")
            .ToListAsync();

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
