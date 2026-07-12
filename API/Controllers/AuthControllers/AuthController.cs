using API.Extensions;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
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
        public async Task<ActionResult<Result<LoginResponseDto>>> Login([FromBody] RequestToken requestToken)
        {
            var result = await _authService.LoginAsync(requestToken);

            if (!result.Succeeded || result.Value == null)
                return ApiResults.Unauthorized(
                    result.Errors.Length > 0 ? result.Errors : new[] { "بيانات الدخول غير صحيحة" });

            var tokens = result.Value;
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

            var userId = ExtractUserIdFromAccessToken(tokens.AccessToken);
            if (string.IsNullOrWhiteSpace(userId))
                return ApiResults.Unauthorized("تعذر إنشاء الجلسة.");

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return Ok(Result<LoginResponseDto>.Success(new LoginResponseDto { CsrfToken = csrfToken }));
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<Result<MeResponseDto>>> Me()
        {
            var userId =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
                return ApiResults.Unauthorized("غير مصرح.");

            var res = await _authService.GetMeAsync(userId);

            if (!res.Succeeded || res.Value == null)
                return ApiResults.Unauthorized(
                    res.Errors.Length > 0 ? res.Errors : new[] { "غير مصرح." });

            return Ok(res);
        }

        [Authorize]
        [HttpGet("csrf")]
        public ActionResult<Result<CsrfResponseDto>> GetCsrf()
        {
            var userId =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
                return ApiResults.Unauthorized("غير مصرح.");

            _csrfService.IssueCsrf(Response, userId, out var csrfToken);
            return Ok(Result<CsrfResponseDto>.Success(new CsrfResponseDto { CsrfToken = csrfToken }));
        }

        [HttpPost("[action]")]
        public async Task<ActionResult<Result<CsrfResponseDto>>> Refresh()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            if (string.IsNullOrWhiteSpace(refreshToken))
                return ApiResults.Unauthorized("فشل تجديد الجلسة.");

            try
            {
                var tokens = await _tokenService.RefreshAccessTokenAsync(refreshToken);
                _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);

                var userId = ExtractUserIdFromAccessToken(tokens.AccessToken);
                if (string.IsNullOrWhiteSpace(userId))
                    return ApiResults.Unauthorized("فشل تجديد الجلسة.");

                _csrfService.IssueCsrf(Response, userId, out var csrfToken);

                return Ok(Result<CsrfResponseDto>.Success(new CsrfResponseDto { CsrfToken = csrfToken }));
            }
            catch
            {
                return ApiResults.Unauthorized("فشل تجديد الجلسة.");
            }
        }

        [Authorize]
        [HttpPost("[action]")]
        public async Task<ActionResult<Result<string>>> Logout()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            if (!string.IsNullOrWhiteSpace(refreshToken))
                await _tokenService.RevokeRefreshTokenAsync(refreshToken);

            _cookieService.ClearAuthCookies(Response);
            return Ok(Result<string>.Success("تم تسجيل الخروج بنجاح."));
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
