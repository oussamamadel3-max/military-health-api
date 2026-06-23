using Application.Interfaces;
using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Persistence
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
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            UserManager<User> userManager,
            string userId)
        {
            var user = await userRepository.FindByIdAsync(userId);
            if (user == null) return;

            await userManager.UpdateSecurityStampAsync(user);

            var refreshTokens = await refreshTokenRepository.GetActiveByUserIdAsync(userId);
            foreach (var token in refreshTokens)
            {
                refreshTokenRepository.MarkRevoked(token);
            }

            await unitOfWork.SaveChangesAsync();
        }

        /// <summary>
        /// تحديث SecurityStamp لجميع المستخدمين الذين لديهم دور معين
        /// يجب استدعاء هذه الدالة عند تعديل صلاحيات الدور
        /// </summary>
        public static async Task InvalidateUsersWithRoleAsync(
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            UserManager<User> userManager,
            string roleId)
        {
            var userIds = await userRepository.GetUserIdsByRoleIdAsync(roleId);

            foreach (var userId in userIds)
            {
                await InvalidateUserTokensAsync(userRepository, refreshTokenRepository, unitOfWork, userManager, userId);
            }
        }

        /// <summary>
        /// تحديث SecurityStamp لجميع المستخدمين الذين لديهم صلاحية حقل معينة
        /// </summary>
        public static async Task InvalidateUsersWithFieldPermissionAsync(
            IFieldPermissionRepository fieldPermissionRepository,
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            UserManager<User> userManager,
            Guid fieldPermissionId)
        {
            var userLinks = await fieldPermissionRepository.GetUserLinksAsync(fieldPermissionId);
            var userIds = userLinks.Select(ufp => ufp.UserId).Distinct().ToList();

            var roleLinks = await fieldPermissionRepository.GetRoleLinksAsync(fieldPermissionId);
            var roleIds = roleLinks.Select(rfp => rfp.RoleId).Distinct().ToList();

            if (roleIds.Count > 0)
            {
                foreach (var roleId in roleIds)
                {
                    var userIdsFromRoles = await userRepository.GetUserIdsByRoleIdAsync(roleId);
                    userIds = userIds.Union(userIdsFromRoles).Distinct().ToList();
                }
            }

            foreach (var userId in userIds)
            {
                await InvalidateUserTokensAsync(userRepository, refreshTokenRepository, unitOfWork, userManager, userId);
            }
        }
    }
}
