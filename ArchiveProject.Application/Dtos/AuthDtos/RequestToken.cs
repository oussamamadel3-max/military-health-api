using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.AuthDtos
{
    public class RequestToken
    {
        [Required]
        [EmailAddress]
        [StringLength(254, MinimumLength = 4)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 5)]
        public string PassWord { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required(ErrorMessage = "رمز التحديث مطلوب.")]
        [MinLength(16, ErrorMessage = "رمز التحديث غير صالح.")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        //public string AccessToken { get; set; }
        //public string RefreshToken { get; set; }
        //public UserDetailCreateModel User { get; set; }
        //public List<string> Roles { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
    public class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
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

