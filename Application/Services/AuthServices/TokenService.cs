using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Application.Services.AuthServices
{
    public class TokenService : ITokenService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly JwtSettings _jwtSettings;

        public TokenService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            IOptions<JwtSettings> jwtOptions)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _jwtSettings = jwtOptions.Value;
        }

        #region    توليد Access/Refresh Token وإرجاع بيانات الصلاحيات لاستخدامها خارجياً
        public async Task<TokenGenerationResultDto> GenerateTokensAsync(string userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new InvalidSessionException("المستخدم غير موجود.");

            var roleNames = await _userRepository.GetActiveRoleNamesAsync(user.Id);
            roleNames = roleNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var userPermissions = await _userRepository.GetDirectPermissionNamesAsync(user.Id, activeOnly: true);

            var roleIds = await _userRepository.GetActiveRoleIdsAsync(user.Id);
            var rolePermissions = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
                .Select(rp => rp.Permission!.Name)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            var allPermissions = userPermissions.Concat(rolePermissions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var userFieldPermissions = await _userRepository.GetUserFieldPermissionDetailsAsync(user.Id);
            var roleFieldPermissions = await _roleRepository.GetRoleFieldPermissionDetailsAsync(roleNames);

            var mergedFieldPermissions = new Dictionary<string, (string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>();

            foreach (var fp in roleFieldPermissions)
            {
                var fieldKey = $"{fp.EntityName}:{fp.FieldName}";
                if (!mergedFieldPermissions.ContainsKey(fieldKey))
                    mergedFieldPermissions[fieldKey] = (fp.EntityName, fp.FieldName, fp.AccessType);
            }

            foreach (var fp in userFieldPermissions)
            {
                var fieldKey = $"{fp.EntityName}:{fp.FieldName}";
                mergedFieldPermissions[fieldKey] = (fp.EntityName, fp.FieldName, fp.AccessType);
            }

            var fieldPermissions = mergedFieldPermissions.Values.ToList();

            var now = DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("security_stamp", user.SecurityStamp ?? Guid.NewGuid().ToString())
            };

            foreach (var permission in allPermissions)
            {
                if (!string.IsNullOrEmpty(permission))
                    claims.Add(new Claim("permissions", permission));
            }

            foreach (var roleName in roleNames)
            {
                if (!string.IsNullOrEmpty(roleName))
                {
                    claims.Add(new Claim("role", roleName));
                    claims.Add(new Claim(ClaimTypes.Role, roleName));
                }
            }

            foreach (var fp in fieldPermissions)
                claims.Add(new Claim("field_permission", $"{fp.EntityName}:{fp.FieldName}:{fp.AccessType}"));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(_jwtSettings.ExpirationInMinutes),
                signingCredentials: creds
            );
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            var rawRefreshToken = RefreshTokenProtector.GenerateRawToken();
            var refreshTokenHash = RefreshTokenProtector.HashToken(rawRefreshToken);

            var dbRefreshToken = new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = refreshTokenHash,
                ExpiryDate = now.AddDays(_jwtSettings.RefreshTokenExpirationInDays),
                IsRevoked = false
            };
            _refreshTokenRepository.Add(dbRefreshToken);
            await _unitOfWork.SaveChangesAsync();

            return new TokenGenerationResultDto
            {
                AccessToken = accessToken,
                RefreshToken = rawRefreshToken,
                Roles = roleNames,
                Permissions = allPermissions
            };
        }
        #endregion

        #region    استخدام Refresh Token للحصول على Access Token جديد و Refresh Token جديد
        public async Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidSessionException("رمز التحديث غير صالح أو منتهي الصلاحية");

            var tokenHash = RefreshTokenProtector.HashToken(refreshToken);
            var now = DateTime.UtcNow;

            await using var tx = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var consumed = await _refreshTokenRepository.TryConsumeValidByTokenHashAsync(tokenHash, now);
                if (consumed == null)
                    throw new InvalidSessionException("رمز التحديث غير صالح أو منتهي الصلاحية");

                var user = await _userRepository.FindByIdAsync(consumed.UserId);
                if (user == null || !user.IsActive)
                    throw new InvalidSessionException("المستخدم غير موجود أو غير نشط");

                var tokenResult = await GenerateTokensAsync(consumed.UserId);
                await tx.CommitAsync();

                return new TokenResponseDto
                {
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        #endregion

        #region  إلغاء Refresh Token
        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var tokenHash = RefreshTokenProtector.HashToken(refreshToken);
            var dbToken = await _refreshTokenRepository.GetByTokenAsync(tokenHash);
            if (dbToken == null) return false;

            _refreshTokenRepository.MarkRevoked(dbToken);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        #endregion
    }
}
