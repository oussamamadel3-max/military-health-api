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
        private readonly IAccessContextRepository _access;
        private readonly IUnitOfWork _unitOfWork;
        private readonly JwtSettings _jwtSettings;

        public TokenService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IAccessContextRepository access,
            IUnitOfWork unitOfWork,
            IOptions<JwtSettings> jwtOptions)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _access = access;
            _unitOfWork = unitOfWork;
            _jwtSettings = jwtOptions.Value;
        }

        #region    توليد Access/Refresh Token وإرجاع بيانات الصلاحيات لاستخدامها خارجياً
        public Task<TokenGenerationResultDto> GenerateTokensAsync(string userId, Guid? activeCenterId = null)
            => GenerateTokensCoreAsync(userId, activeCenterId, rotatedFrom: null);

        private async Task<TokenGenerationResultDto> GenerateTokensCoreAsync(
            string userId,
            Guid? activeCenterId,
            UserRefreshToken? rotatedFrom)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) throw new InvalidSessionException("المستخدم غير موجود.");

            var snapshot = await _access.LoadAsync(user.Id);
            var roleNames = CenterPermissionEvaluator.ApplicableRoleNames(snapshot.Roles, activeCenterId).ToList();
            var allPermissions = CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, activeCenterId).ToList();

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
            var sessionFamilyId = rotatedFrom?.TokenFamilyId ?? Guid.NewGuid();
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("security_stamp", user.SecurityStamp ?? Guid.NewGuid().ToString()),
                new Claim(SessionFamilyGate.ClaimType, sessionFamilyId.ToString()),
                new Claim(CenterClaimTypes.Scope, activeCenterId.HasValue ? CenterClaimTypes.Center : CenterClaimTypes.Central)
            };

            if (activeCenterId.HasValue)
                claims.Add(new Claim(CenterClaimTypes.CenterId, activeCenterId.Value.ToString()));

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
                TokenFamilyId = sessionFamilyId,
                ParentRefreshTokenId = rotatedFrom?.Id,
                SecurityStamp = user.SecurityStamp,
                IssuedAt = new DateTimeOffset(now),
                ExpiryDate = now.AddDays(_jwtSettings.RefreshTokenExpirationInDays),
                IsRevoked = false
            };
            _refreshTokenRepository.Add(dbRefreshToken);
            await _unitOfWork.SaveChangesAsync();
            if (rotatedFrom != null)
                await _refreshTokenRepository.MarkRotatedAsync(rotatedFrom.Id, dbRefreshToken.Id);

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
        public async Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken, Guid? activeCenterId = null)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw Reject(SessionRefresh.Missing, null);

            var tokenHash = RefreshTokenProtector.HashToken(refreshToken);
            var now = DateTime.UtcNow;
            var rejectionCommitted = false;

            await using var tx = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var consumed = await _refreshTokenRepository.TryConsumeValidByTokenHashAsync(tokenHash, now);
                if (consumed == null)
                {
                    var known = await _refreshTokenRepository.GetByTokenAsync(tokenHash);
                    if (known == null)
                        throw Reject(SessionRefresh.Invalid, null);

                    if (known.IsRevoked)
                    {
                        await _refreshTokenRepository.RevokeActiveFamilyAsync(known.TokenFamilyId, SessionRefresh.ReuseReason);
                        await tx.CommitAsync();
                        rejectionCommitted = true;
                        throw Reject(SessionRefresh.Reused, known.UserId);
                    }

                    if (known.ExpiryDate < now)
                        throw Reject(SessionRefresh.Expired, known.UserId);

                    throw Reject(SessionRefresh.Invalid, known.UserId);
                }

                var user = await _userRepository.FindByIdAsync(consumed.UserId);
                if (user == null || !user.IsActive)
                {
                    await _refreshTokenRepository.SetRevokeReasonAsync(consumed.Id, SessionRefresh.Inactive);
                    await tx.CommitAsync();
                    rejectionCommitted = true;
                    throw Reject(SessionRefresh.Inactive, consumed.UserId);
                }

                if (!AccessTokenSessionGate.IsAccessTokenAccepted(
                        userExists: true,
                        isActive: true,
                        tokenSecurityStamp: consumed.SecurityStamp,
                        userSecurityStamp: user.SecurityStamp))
                {
                    await _refreshTokenRepository.SetRevokeReasonAsync(consumed.Id, SessionRefresh.SecurityStamp);
                    await tx.CommitAsync();
                    rejectionCommitted = true;
                    throw Reject(SessionRefresh.SecurityStamp, consumed.UserId);
                }

                var tokenResult = await GenerateTokensCoreAsync(consumed.UserId, activeCenterId, consumed);
                await tx.CommitAsync();

                return new TokenResponseDto
                {
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken
                };
            }
            catch
            {
                if (!rejectionCommitted)
                    await tx.RollbackAsync();
                throw;
            }
        }

        private static InvalidSessionException Reject(string reasonCode, string? userId)
            => new(SessionRefresh.RejectedMessage, reasonCode, userId);
        #endregion

        #region  إلغاء Refresh Token
        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var tokenHash = RefreshTokenProtector.HashToken(refreshToken);
            var dbToken = await _refreshTokenRepository.GetByTokenAsync(tokenHash);
            if (dbToken == null || dbToken.IsRevoked) return false;

            dbToken.RevokeReason = SessionRefresh.LogoutReason;
            _refreshTokenRepository.MarkRevoked(dbToken);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        #endregion
    }
}
