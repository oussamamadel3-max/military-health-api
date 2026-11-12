using Application.Attributes;
using Application.Interfaces.Services;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos
{
    public class RequestToken
    {
        /// <summary>اسم المستخدم للدخول. اسم الخاصية JSON يبقى UserName.</summary>
        [Required(ErrorMessage = "اسم المستخدم مطلوب.")]
        [StringLength(256, MinimumLength = 3, ErrorMessage = "اسم المستخدم يجب أن يكون بين 3 و 256 حرفاً.")]
        public string UserName { get; set; }

        /// <summary>كلمة المرور للدخول. اسم الخاصية JSON يبقى PassWord.</summary>
        [Required(ErrorMessage = "كلمة المرور مطلوبة.")]
        [StringLength(128, MinimumLength = 5, ErrorMessage = "كلمة المرور يجب أن تكون بين 5 و 128 حرفاً.")]
        public string PassWord { get; set; }
    }

    public class RefreshTokenRequest
    {
        [Required(ErrorMessage = "رمز التحديث مطلوب.")]
        [MinLength(16, ErrorMessage = "رمز التحديث غير صالح.")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string CsrfToken { get; set; } = string.Empty;
    }

    public sealed class LoginSessionDto
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public List<AssignedCenterDto> Centers { get; set; } = new();
        public string CenterStatus { get; set; } = string.Empty;
        public AssignedCenterDto? ActiveCenter { get; set; }
        public bool OperationalAccess { get; set; }
        public bool CentralAuthority { get; set; }
        public bool MustChangePassword { get; set; }
        public string? AccountMessage { get; set; }
    }

    public sealed class LoginResult
    {
        public TokenGenerationResultDto Tokens { get; init; } = new();
        public string UserId { get; init; } = string.Empty;
        public LoginSessionDto Session { get; init; } = new();
    }

    public class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class CsrfResponseDto
    {
        public string CsrfToken { get; set; } = string.Empty;
    }

    public class MeResponseDto
    {
        public string UserId { get; set; } = string.Empty;
        public DateTime CreationDate { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }

        /// <summary>
        /// معرّف المالك كنص (long) لتجنب فقدان الدقة في JavaScript عند التعامل مع Attachments.
        /// </summary>
        public Guid? ProfileFileAssetId { get; set; }
        public string? ProfileFileUrl { get; set; }

        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
        public List<AssignedCenterDto> Centers { get; set; } = new();
        public string CenterStatus { get; set; } = string.Empty;
        public AssignedCenterDto? ActiveCenter { get; set; }
        public bool OperationalAccess { get; set; }
        public bool CentralAuthority { get; set; }
        public bool MustChangePassword { get; set; }
        public string? AccountMessage { get; set; }
    }

    public class UpdateMyProfileRequest
    {
        [Required(ErrorMessage = "الاسم الكامل مطلوب.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "الاسم الكامل يجب أن يكون بين 3 و 100 حرف.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "البريد الإلكتروني مطلوب.")]
        [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صالح.")]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [StringLength(20)]
        [RegularExpression(@"^[0-9+ -]*$", ErrorMessage = "رقم الهاتف غير صالح.")]
        public string? Phone { get; set; }
    }

}

