namespace ArchiveProject.Application.Dtos.AuthDtos
{
    public class RequestToken
    {
        public string Email { get; set; } = string.Empty;
        public string PassWord { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public UserDetailCreateModel User { get; set; }
        public List<string> Roles { get; set; }
    }
    public class TokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }

}

