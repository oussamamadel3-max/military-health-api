using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ArchiveProject.Infrastructure.Services.AuthServices
{
    public class TokenService : ITokenService
    {
        private readonly MainDbContext _context;
        private readonly JwtSettings _jwtSettings;

        public TokenService(MainDbContext context, IOptions<JwtSettings> jwtOptions)
        {
            _context = context;
            _jwtSettings = jwtOptions.Value;
        }

        #region    توليد Access/Refresh Token وإرجاع بيانات الصلاحيات لاستخدامها خارجياً
        public async Task<TokenGenerationResultDto> GenerateTokensAsync(string userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) throw new Exception("User not found");

            // 1️⃣ Roles - فقط الأدوار النشطة
            var roleNames = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == user.Id && ura.Role != null && ura.Role.IsActive) // فقط الأدوار النشطة
                .Select(ura => ura.Role!.Name!)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToListAsync();
            roleNames = roleNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 2️⃣ User permissions - فقط الصلاحيات النشطة
            var userPermissions = await _context.UserPermissions
                .Where(up => up.UserId == user.Id && up.Permission != null && up.Permission.IsActive) // فقط الصلاحيات النشطة
                .Select(up => up.Permission!.Name)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToListAsync();

            // 3️⃣ Role permissions - فقط الصلاحيات النشطة
            var rolePermissions = await _context.RolePermissions
                .Where(rp => rp.Role != null &&
                             rp.Role.Name != null &&
                             rp.Role.IsActive && // فقط الأدوار النشطة
                             roleNames.Contains(rp.Role.Name) &&
                             rp.Permission != null &&
                             rp.Permission.IsActive) // فقط الصلاحيات النشطة
                .Select(rp => rp.Permission!.Name)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToListAsync();

            // 4️⃣ دمج الصلاحيات
            var allPermissions = userPermissions.Concat(rolePermissions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();



            // 5️⃣ Field Permissions (من روابط المستخدم + الأدوار، مع أولوية لصلاحيات المستخدم)
            var userFieldPermissions = await _context.UserFieldPermissions
                .Where(ufp => ufp.UserId == user.Id)
                .Select(ufp => new
                {
                    ufp.FieldPermission.EntityName,
                    ufp.FieldPermission.FieldName,
                    ufp.FieldPermission.AccessType
                })
                .ToListAsync();

            var roleFieldPermissions = await _context.RoleFieldPermissions
                .Where(rfp => rfp.Role != null &&
                              rfp.Role.Name != null &&
                              roleNames.Contains(rfp.Role.Name))
                .Select(rfp => new
                {
                    rfp.FieldPermission.EntityName,
                    rfp.FieldPermission.FieldName,
                    rfp.FieldPermission.AccessType
                })
                .ToListAsync();

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
            _context.UserRefreshTokens.Add(dbRefreshToken);
            await _context.SaveChangesAsync();

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
            var now = DateTime.Now;
            var dbToken = await _context.UserRefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.IsRevoked);

            if (dbToken == null || dbToken.ExpiryDate < now)
                throw new Exception("رمز التحديث غير صالح أو منتهي الصلاحية");

            var user = await _context.Users.FindAsync(dbToken.UserId);
            if (user == null || !user.IsActive)
                throw new Exception("المستخدم غير موجود أو غير نشط");
            _context.UserRefreshTokens.Remove(dbToken); // حذف القديم مباشرة

            // توليد Access + Refresh جديدين
            var tokenResult = await GenerateTokensAsync(dbToken.UserId);

            // ملاحظة: GenerateTokensAsync يضيف بالفعل Refresh Token جديد للـ DB
            // لذلك لا داعي لإضافة سجل جديد هنا

            await _context.SaveChangesAsync();

            // إرجاع كلا التوكنين للعميل
            return new TokenResponseDto
            {
                AccessToken = tokenResult.AccessToken,
                RefreshToken = tokenResult.RefreshToken
            };
        }
        #endregion

        #region  إلغاء Refresh Token
        public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
        {
            var dbToken = await _context.UserRefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
            if (dbToken == null) return false;

            dbToken.IsRevoked = true;
            await _context.SaveChangesAsync();
            return true;
        }
        #endregion
    }
}

