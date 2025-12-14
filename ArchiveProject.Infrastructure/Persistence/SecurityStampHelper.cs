using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Persistence
{
    /// <summary>
    /// Helper class لتحديث SecurityStamp وإبطال Tokens عند تغيير الصلاحيات
    /// </summary>
    public static class SecurityStampHelper
    {
        /// <summary>
        /// تحديث SecurityStamp للمستخدم وإبطال جميع Refresh Tokens الخاصة به
        /// يجب استدعاء هذه الدالة عند أي تعديل في الصلاحيات/الأدوار
        /// </summary>
        public static async Task InvalidateUserTokensAsync(
            MainDbContext context,
            UserManager<User> userManager,
            string userId)
        {
            var user = await context.Users.FindAsync(userId);
            if (user == null) return;

            // تحديث SecurityStamp - هذا سيجعل جميع JWT tokens الحالية غير صالحة
            await userManager.UpdateSecurityStampAsync(user);

            // إبطال جميع Refresh Tokens الخاصة بالمستخدم
            var refreshTokens = await context.UserRefreshTokens
                .Where(rt => rt.UserId == userId && !rt.IsRevoked)
                .ToListAsync();

            foreach (var token in refreshTokens)
            {
                token.IsRevoked = true;
            }

            await context.SaveChangesAsync();
        }

        /// <summary>
        /// تحديث SecurityStamp لجميع المستخدمين الذين لديهم دور معين
        /// يجب استدعاء هذه الدالة عند تعديل صلاحيات الدور
        /// </summary>
        public static async Task InvalidateUsersWithRoleAsync(
            MainDbContext context,
            UserManager<User> userManager,
            string roleId)
        {
            // جلب جميع المستخدمين الذين لديهم هذا الدور
            var userIds = await context.UserRoleAssociations
                .Where(ura => ura.RoleId == roleId)
                .Select(ura => ura.UserId)
                .Distinct()
                .ToListAsync();

            foreach (var userId in userIds)
            {
                await InvalidateUserTokensAsync(context, userManager, userId);
            }
        }

        /// <summary>
        /// تحديث SecurityStamp لجميع المستخدمين الذين لديهم صلاحية حقل معينة
        /// </summary>
        public static async Task InvalidateUsersWithFieldPermissionAsync(
            MainDbContext context,
            UserManager<User> userManager,
            Guid fieldPermissionId)
        {
            // جلب جميع المستخدمين الذين لديهم هذه الصلاحية مباشرة
            var userIds = await context.UserFieldPermissions
                .Where(ufp => ufp.FieldPermissionId == fieldPermissionId)
                .Select(ufp => ufp.UserId)
                .Distinct()
                .ToListAsync();

            // جلب جميع المستخدمين الذين لديهم هذه الصلاحية عبر الأدوار
            var roleIds = await context.RoleFieldPermissions
                .Where(rfp => rfp.FieldPermissionId == fieldPermissionId)
                .Select(rfp => rfp.RoleId)
                .Distinct()
                .ToListAsync();

            if (roleIds.Any())
            {
                var userIdsFromRoles = await context.UserRoleAssociations
                    .Where(ura => roleIds.Contains(ura.RoleId))
                    .Select(ura => ura.UserId)
                    .Distinct()
                    .ToListAsync();

                userIds = userIds.Union(userIdsFromRoles).Distinct().ToList();
            }

            foreach (var userId in userIds)
            {
                await InvalidateUserTokensAsync(context, userManager, userId);
            }
        }
    }
}
