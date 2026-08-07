using API.Infrastructure;
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

        [EnableRateLimiting("login")]
        [HttpPost("[action]")]
        public async Task<IResult> Login([FromBody] RequestToken requestToken)
        {
            var (tokens, userId) = await _authService.LoginAsync(requestToken);
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);
            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return ApiResponse.Success("تم تسجيل الدخول بنجاح.", new LoginResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IResult> Me()
        {
            var meData = await _authService.GetMeAsync(_userContextService.UserId);
            return ApiResponse.Success("User profile retrieved successfully", meData);
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
            var (tokens, userId) = await _authService.RefreshSessionAsync(refreshToken);
            _cookieService.SetAuthCookies(Response, tokens.AccessToken, tokens.RefreshToken);
            _csrfService.IssueCsrf(Response, userId, out var csrfToken);

            return ApiResponse.Success("Session refreshed successfully", new CsrfResponseDto { CsrfToken = csrfToken });
        }

        [Authorize]
        [HttpPost("[action]")]
        public async Task<IResult> Logout()
        {
            var refreshToken = _cookieService.TryGetRefreshToken(Request);
            await _authService.LogoutAsync(refreshToken);
            _cookieService.ClearAuthCookies(Response);
            return ApiResponse.Success("تم تسجيل الخروج بنجاح.");
        }
    }
}
