using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos
{
    public class RequestToken
    {
        [Required]
        [StringLength(256, MinimumLength = 3)]
        public string UserName { get; set; }

        [Required]
        [StringLength(128, MinimumLength = 5)]
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
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public List<string> Roles { get; set; } = new();
        public List<string> Permissions { get; set; } = new();
    }

}

