using Application.Dtos.AuthDtos;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Application.Services.AuthServices
{
    public class TokenService : ITokenService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks = new();
        private static readonly ConcurrentDictionary<string, (TokenResponseDto Result, DateTime ExpiresAt)> RefreshResultsCache = new();
        private static readonly TimeSpan RefreshCacheTtl = TimeSpan.FromSeconds(30);

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
            if (user == null) throw new Exception("User not found");

            // 1️⃣ Roles - فقط الأدوار النشطة
            var roleNames = await _userRepository.GetActiveRoleNamesAsync(user.Id);
            roleNames = roleNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 2️⃣ User permissions - فقط الصلاحيات النشطة
            var userPermissions = await _userRepository.GetDirectPermissionNamesAsync(user.Id, activeOnly: true);

            // 3️⃣ Role permissions - فقط الصلاحيات النشطة
            var roleIds = await _userRepository.GetActiveRoleIdsAsync(user.Id);
            var rolePermissions = (await _roleRepository.GetRolePermissionsForRolesAsync(roleIds))
                .Select(rp => rp.Permission!.Name)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            // 4️⃣ دمج الصلاحيات
            var allPermissions = userPermissions.Concat(rolePermissions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 5️⃣ Field Permissions (من روابط المستخدم + الأدوار، مع أولوية لصلاحيات المستخدم)
            var userFieldPermissions = await _userRepository.GetUserFieldPermissionDetailsAsync(user.Id);
            var roleFieldPermissions = await _roleRepository.GetRoleFieldPermissionDetailsAsync(roleNames);

            var mergedFieldPermissions = new Dictionary<string, (string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>();

            // أضف صلاحيات الأدوار أولاً
            foreach (var fp in roleFieldPermissions)
            {
                var fieldKey = $"{fp.EntityName}:{fp.FieldName}";
                if (!mergedFieldPermissions.ContainsKey(fieldKey))
                    mergedFieldPermissions[fieldKey] = (fp.EntityName, fp.FieldName, fp.AccessType);
            }

            // ثم قم بالكتابة فوقها بصلاحيات المستخدم إن وجدت
            foreach (var fp in userFieldPermissions)
            {
                var fieldKey = $"{fp.EntityName}:{fp.FieldName}";
                mergedFieldPermissions[fieldKey] = (fp.EntityName, fp.FieldName, fp.AccessType);
            }

            var fieldPermissions = mergedFieldPermissions.Values.ToList();

            // 6️⃣ Claims
            var now = DateTime.Now;
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                // إضافة SecurityStamp للتحقق من صلاحية Token عند تغيير الصلاحيات
                new Claim("security_stamp", user.SecurityStamp ?? Guid.NewGuid().ToString())
            };

            // Permissions
            foreach (var permission in allPermissions)
            {
                if (!string.IsNullOrEmpty(permission))
                    claims.Add(new Claim("permissions", permission));
            }

            // Roles
            foreach (var roleName in roleNames)
            {
                if (!string.IsNullOrEmpty(roleName))
                {
                    claims.Add(new Claim("role", roleName));
                    claims.Add(new Claim(ClaimTypes.Role, roleName));
                }
            }

            // Field permissions
            foreach (var fp in fieldPermissions)
                claims.Add(new Claim("field_permission", $"{fp.EntityName}:{fp.FieldName}:{fp.AccessType}"));

            // 7️⃣ إنشاء Access Token
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

            // 8️⃣ إنشاء Refresh Token
            var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

            // حفظ Refresh Token في DB
            var dbRefreshToken = new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = refreshToken,
                ExpiryDate = now.AddDays(_jwtSettings.RefreshTokenExpirationInDays),
                IsRevoked = false
            };
            _refreshTokenRepository.Add(dbRefreshToken);
            await _unitOfWork.SaveChangesAsync();

            return new TokenGenerationResultDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                Roles = roleNames,
                Permissions = allPermissions
            };
        }
        #endregion

        #region    استخدام Refresh Token للحصول على Access Token جديد و Refresh Token جديد
        public async Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken)
        {
            if (TryGetCachedRefreshResult(refreshToken, out var cachedResult))
                return cachedResult;

            var refreshLock = RefreshLocks.GetOrAdd(refreshToken, _ => new SemaphoreSlim(1, 1));
            await refreshLock.WaitAsync();
            try
            {
                if (TryGetCachedRefreshResult(refreshToken, out cachedResult))
                    return cachedResult;

                var now = DateTime.Now;
                var dbToken = await _refreshTokenRepository.GetValidByTokenAsync(refreshToken, now);

                if (dbToken == null)
                {
                    if (TryGetCachedRefreshResult(refreshToken, out cachedResult))
                        return cachedResult;

                    throw new Exception("رمز التحديث غير صالح أو منتهي الصلاحية");
                }

                var user = await _userRepository.FindByIdAsync(dbToken.UserId);
                if (user == null || !user.IsActive)
                    throw new Exception("المستخدم غير موجود أو غير نشط");

                _refreshTokenRepository.MarkRevoked(dbToken);

                var tokenResult = await GenerateTokensAsync(dbToken.UserId);
                await _unitOfWork.SaveChangesAsync();

                var response = new TokenResponseDto
                {
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken
                };

                CacheRefreshResult(refreshToken, response);
                return response;
            }
            finally
            {
                refreshLock.Release();
                RefreshLocks.TryRemove(refreshToken, out _);
            }
        }

        private static bool TryGetCachedRefreshResult(string refreshToken, out TokenResponseDto result)
        {
            result = null!;
            if (!RefreshResultsCache.TryGetValue(refreshToken, out var cached))
                return false;

            if (cached.ExpiresAt <= DateTime.UtcNow)
            {
                RefreshResultsCache.TryRemove(refreshToken, out _);
                return false;
            }

            result = cached.Result;
            return true;
        }

        private static void CacheRefreshResult(string refreshToken, TokenResponseDto result)
        {
            RefreshResultsCache[refreshToken] = (result, DateTime.UtcNow.Add(RefreshCacheTtl));
        }
        #endregion

        #region  إلغاء Refresh Token
        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            var dbToken = await _refreshTokenRepository.GetByTokenAsync(refreshToken);
            if (dbToken == null) return false;

            _refreshTokenRepository.MarkRevoked(dbToken);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        #endregion
    }
}
