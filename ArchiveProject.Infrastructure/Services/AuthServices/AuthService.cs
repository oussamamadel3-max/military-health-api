using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;

    public AuthService(
        UserManager<User> userManager,
        ITokenService tokenService) // ✅ حقن TokenService
    {
        _userManager = userManager;
        _tokenService = tokenService;
    }

    public async Task<Result<LoginResponseDto>> LoginAsync(RequestToken requestToken)
    {
        // 1️⃣ التحقق من المستخدم وكلمة المرور
        var user = await _userManager.FindByEmailAsync(requestToken.Email);
        if (user == null)
        {
            return Result<LoginResponseDto>.Failure(new List<string> { "المستخدم غير موجود." }, null);
        }
        if (!await _userManager.CheckPasswordAsync(user, requestToken.PassWord))
        {
            return Result<LoginResponseDto>.Failure(new List<string> { "خطأ في اسم المستخدم أو كلمة المرور." }, null);
        }
        if (!user.IsActive)
        {
            return Result<LoginResponseDto>.Failure(new List<string> { "الحساب غير نشط." }, null);
        }

        // 2️⃣ توليد Access Token و Refresh Token + بيانات الصلاحيات من خدمة التوكن
        var tokenResult = await _tokenService.GenerateTokensAsync(user.Id);

        // 3️⃣ تفاصيل المستخدم
        var userDetail = new UserDetailCreateModel
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            CreationDate = user.CreationDate,
            Permissions = tokenResult.Permissions
        };

        // 4️⃣ إعداد الاستجابة
        var response = new LoginResponseDto
        {
            AccessToken = tokenResult.AccessToken,          // Access Token
            RefreshToken = tokenResult.RefreshToken,        // Refresh Token جديد
            User = userDetail,
            Roles = tokenResult.Roles,
        };

        return Result<LoginResponseDto>.Success(response);
    }
}


