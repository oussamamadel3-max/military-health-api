using API.Infrastructure;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;
        private readonly AuthCookieService _cookieService;
        private readonly ICsrfService _csrfService;

        public AuthController(
            IAuthService authService,
            ITokenService tokenService,
            AuthCookieService cookieService,
            ICsrfService csrfService)
        {
            _authService = authService;
            _tokenService = tokenService;
            _cookieService = cookieService;
            _csrfService = csrfService;
        }

        [EnableRateLimiting("login")]
        [HttpPost("[action]")]
        public async Task<IResult> Login([FromBody] RequestToken requestToken)
        {
            var tokens = await _authService.LoginAsync(requestToken);
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

            var userId = ExtractUserIdFromAccessToken(tokens.AccessToken)
                ?? throw new InvalidSessionException("تعذر إنشاء الجلسة.");

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return ApiResponse.Success("تم تسجيل الدخول بنجاح.", new LoginResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IResult> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? throw new UnauthenticatedUserException();

            var meData = await _authService.GetMeAsync(userId);
            return ApiResponse.Success("User profile retrieved successfully", meData);
        }

        [Authorize]
        [HttpGet("csrf")]
        public IResult GetCsrf()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? throw new UnauthenticatedUserException();

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);
            return ApiResponse.Success("CSRF token generated", new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [HttpPost("[action]")]
        public async Task<IResult> Refresh()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request)
                ?? throw new InvalidSessionException();

            var tokens = await _tokenService.RefreshAccessTokenAsync(refreshToken);
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

            var userId = ExtractUserIdFromAccessToken(tokens.AccessToken)
                ?? throw new InvalidSessionException();

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return ApiResponse.Success("Session refreshed successfully", new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpPost("[action]")]
        public async Task<IResult> Logout()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _tokenService.RevokeRefreshTokenAsync(refreshToken);

            _cookieService.ClearAuthCookies(Response);
            return ApiResponse.Success("تم تسجيل الخروج بنجاح.");
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
