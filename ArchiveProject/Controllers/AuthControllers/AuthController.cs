using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Mvc;

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

        [HttpPost("[action]")]
        public async Task<IActionResult> Login([FromBody] RequestToken requestToken)
        {
            var result = await _authService.LoginAsync(requestToken);

            if (!result.Succeeded || result.Value == null)
                return Unauthorized(result.Errors.FirstOrDefault());

            return Ok(result.Value);
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
