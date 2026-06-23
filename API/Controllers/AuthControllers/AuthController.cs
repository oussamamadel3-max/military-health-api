using Infrastructure.Services.Auth;
using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
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
        public async Task<IActionResult> Login([FromBody] RequestToken requestToken)
        {
            var result = await _authService.LoginAsync(requestToken);

            if (!result.Succeeded || result.Value == null)
                return Unauthorized(result.Errors.FirstOrDefault() ?? "بيانات الدخول غير صحيحة");

            var tokens = result.Value;
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

            var userId = ExtractUserIdFromAccessToken(tokens.AccessToken);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized("تعذر إنشاء الجلسة.");

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return Ok(new LoginResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var userId =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized("Unauthorized");

            var res = await _authService.GetMeAsync(userId);

            if (!res.Succeeded || res.Value == null)
                return Unauthorized(res.Errors.FirstOrDefault());

            return Ok(res.Value);
        }

        [Authorize]
        [HttpGet("csrf")]
        public IActionResult GetCsrf()
        {
            var userId =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);
            return Ok(new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [HttpPost("[action]")]
        public async Task<ActionResult<CsrfResponseDto>> Refresh()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized(new { message = "فشل تجديد الجلسة." });

            try
            {
                var tokens = await _tokenService.RefreshAccessTokenAsync(refreshToken);
                _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

                var userId = ExtractUserIdFromAccessToken(tokens.AccessToken);
                if (string.IsNullOrWhiteSpace(userId))
                    return Unauthorized(new { message = "فشل تجديد الجلسة." });

                _csrfService.IssueCsrf(Response, userId, out var csrfToken);

                return Ok(new CsrfResponseDto { CsrfToken = csrfToken });
            }
            catch
            {
                return Unauthorized(new { message = "فشل تجديد الجلسة." });
            }
        }

        [Authorize]
        [HttpPost("[action]")]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _tokenService.RevokeRefreshTokenAsync(refreshToken);

            _cookieService.ClearAuthCookies(Response);
            return Ok("Logged out successfully");
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
