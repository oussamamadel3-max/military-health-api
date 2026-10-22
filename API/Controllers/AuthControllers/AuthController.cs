using System.IdentityModel.Tokens.Jwt;
using API.Infrastructure;
using Application.Common.Security;
using API.RateLimiting;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly AuthCookieService _cookieService;
        private readonly ICsrfService _csrfService;
        private readonly IUserContextService _userContextService;

        public AuthController(
            IAuthService authService,
            AuthCookieService cookieService,
            ICsrfService csrfService,
            IUserContextService userContextService)
        {
            _authService = authService;
            _cookieService = cookieService;
            _csrfService = csrfService;
            _userContextService = userContextService;
        }

        [EnableRateLimiting(RateLimitPolicies.Login)]
        [HttpPost("[action]")]
        public async Task<IResult> Login([FromBody] RequestToken requestToken)
        {
            var result = await _authService.LoginAsync(requestToken);
            _cookieService.SetAuthCookies(Response, result.Tokens.AccessToken, result.Tokens.RefreshToken);
            _csrfService.IssueCsrf(Response, result.UserId, out var csrfToken);

            return ApiResponse.Success("تم تسجيل الدخول بنجاح.", new LoginResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IResult> Me()
        {
            var meData = await _authService.GetMeAsync(_userContextService.UserId);
            return ApiResponse.Success("تم جلب سياق المستخدم.", meData);
        }

        [Authorize]
        [HttpGet("csrf")]
        public async Task<IResult> GetCsrf()
        {
            var userId = await _authService.ValidateAndGetUserIdAsync(_userContextService.UserId);
            _csrfService.IssueCsrf(Response, userId, out var csrfToken);
            return ApiResponse.Success("CSRF token generated", new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [HttpPost("[action]")]
        public async Task<IResult> Refresh()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            var activeCenterId = ReadActiveCenter(_cookieService.TryGetAccessToken(Request));
            var (tokens, userId) = await _authService.RefreshSessionAsync(refreshToken, activeCenterId);
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);
            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return ApiResponse.Success("تم تجديد الجلسة بنجاح.", new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpPost("[action]")]
        public async Task<IResult> Logout()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            var activeCenterId = ReadActiveCenter(_cookieService.TryGetAccessToken(Request));
            await _authService.LogoutAsync(refreshToken, activeCenterId);
            _cookieService.ClearAuthCookies(Response);
            return ApiResponse.Success("تم تسجيل الخروج بنجاح.");
        }

        private static Guid? ReadActiveCenter(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return null;
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(accessToken))
                return null;
            var jwt = handler.ReadJwtToken(accessToken);
            var scope = jwt.Claims.FirstOrDefault(c => c.Type == CenterClaimTypes.Scope)?.Value;
            if (!string.Equals(scope, CenterClaimTypes.Center, StringComparison.OrdinalIgnoreCase))
                return null;
            var raw = jwt.Claims.FirstOrDefault(c => c.Type == CenterClaimTypes.CenterId)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
