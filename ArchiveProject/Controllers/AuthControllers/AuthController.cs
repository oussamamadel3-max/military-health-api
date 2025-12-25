using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace ArchiveProject.API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;

        public AuthController(IAuthService authService, ITokenService tokenService)
        {
            _authService = authService;
            _tokenService = tokenService;
        }

        [EnableRateLimiting("login")]
        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromBody] RequestToken requestToken)
        {
            var result = await _authService.LoginAsync(requestToken);

            if (!result.Succeeded || result.Value == null)
                return Unauthorized(result.Errors.FirstOrDefault() ?? "بيانات الدخول غير صحيحة");

            return Ok(result.Value);
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


        /// <summary>
        /// تجديد Access Token باستخدام Refresh Token
        /// </summary>
        [HttpPost("[action]")]
        public async Task<ActionResult<TokenResponseDto>> Refresh([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var tokens = await _tokenService.RefreshAccessTokenAsync(request.RefreshToken);
                return Ok(tokens);
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }


        /// <summary>
        /// إبطال Refresh Token
        /// </summary>
        [HttpPost("[action]")]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
        {
            var revoked = await _tokenService.RevokeRefreshTokenAsync(request.RefreshToken);
            if (!revoked) return NotFound("Refresh token not found");

            return Ok("Logged out successfully");
        }
    }
}
