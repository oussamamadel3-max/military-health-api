//using Microsoft.AspNetCore.Identity;
//using Microsoft.EntityFrameworkCore;
//using ArchiveProject.Domain.Entities;
//using ArchiveProject.Domain.Entities.Auth;
//using ArchiveProject.Domain.Enums;

//namespace ArchiveProject.Infrastructure.Persistence
//{
//    /// <summary>
//    /// ????? ?? ????? ???????? ??????? ?????? (????????? ?????? ???????? ?????? ???????)
//    /// ??? ???????? ??? OnModelCreating
//    /// </summary>
//    public static class SeedData
//    {
//        public static void Seed(ModelBuilder modelBuilder)
//        {
//            DateTime seedDate = new(2025, 10, 30);
//            const string systemUser = "System";

//            #region ===== ???? GUIDs ????????? =====

//            // ===== Project Permissions =====
//            var permProjectReadId = Guid.Parse("11111111-1111-1111-1111-222222222222");
//            var permProjectCreateId = Guid.Parse("11111111-1111-1111-1111-333333333333");
//            var permProjectEditId = Guid.Parse("11111111-1111-1111-1111-111111111111");
//            var permProjectDeleteId = Guid.Parse("11111111-1111-1111-1111-444444444444");

//            // ===== StoppedProject Permissions =====
//            var permStoppedProjectReadId = Guid.Parse("11111111-1111-1111-1111-555555555555");
//            var permStoppedProjectCreateId = Guid.Parse("11111111-1111-1111-1111-777777777777");
//            var permStoppedProjectEditId = Guid.Parse("11111111-1111-1111-1111-666666666666");
//            var permStoppedProjectDeleteId = Guid.Parse("11111111-1111-1111-1111-888888888888");

//            // ===== ProjectPhase Permissions =====
//            var permProjectPhaseReadId = Guid.Parse("11111111-1111-1111-1111-999999999999");
//            var permProjectPhaseCreateId = Guid.Parse("11111111-1111-1111-1111-aaaaaaaaaaaa");
//            var permProjectPhaseEditId = Guid.Parse("11111111-1111-1111-1111-bbbbbbbbbbbb");
//            var permProjectPhaseDeleteId = Guid.Parse("11111111-1111-1111-1111-cccccccccccc");

//            // ===== Procedure Permissions =====
//            var permProcedureReadId = Guid.Parse("11111111-1111-1111-1111-000000000000");
//            var permProcedureCreateId = Guid.Parse("11111111-1111-1111-1111-ffffffffffff");
//            var permProcedureEditId = Guid.Parse("11111111-1111-1111-1111-dddddddddddd");
//            var permProcedureDeleteId = Guid.Parse("11111111-1111-1111-1111-eeeeeeeeeeee");

//            // ===== Organization Permissions =====
//            var permOrganizationReadId = Guid.Parse("11111111-3333-1111-1111-444444444444");
//            var permOrganizationCreateId = Guid.Parse("11111111-2222-1111-1111-111111111111");
//            var permOrganizationEditId = Guid.Parse("11111111-2222-1111-1111-333333333333");
//            var permOrganizationDeleteId = Guid.Parse("11111111-3333-1111-1111-222222222222");

//            // ===== Department Permissions =====
//            var permDepartmentReadId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
//            var permDepartmentCreateId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
//            var permDepartmentEditId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
//            var permDepartmentDeleteId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");


//            // ===== User Management Permissions =====
//            var permUserReadId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000A");
//            var permUserCreateId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000C");
//            var permUserEditId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000B");
//            var permUserDeleteId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009");

//            // ===== Dashboard & Systems Monitoring =====
//            var permDashboardReadId = Guid.Parse("11111111-2222-1111-1111-555555555555");
//            var permSystemsMonitoringReadId = Guid.Parse("11111111-3333-1111-1111-666666666666");

//            #endregion

//            var permSuperAdminId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");



//            #region ===== Roles & Users =====

//            modelBuilder.Entity<Role>().HasData(
//                new Role { Id = "11111111-1111-1111-1111-777777777777", Name = "Admin", NormalizedName = "ADMIN" }
//            );

//            var hasher = new PasswordHasher<User>();

//            var adminUser = new User
//            {
//                Id = "b961abff-24e7-4e4e-b3dd-6d72405b8ca0",
//                UserName = "superAdmin",
//                NormalizedUserName = "SUPERADMIN",
//                Email = "superAdmin@gmail.com",
//                NormalizedEmail = "SUPERADMIN@GMAIL.COM",
//                EmailConfirmed = true,
//                IsActive = true,
//                CreationDate = seedDate,
//                SecurityStamp = Guid.NewGuid().ToString(),
//                PasswordHash = hasher.HashPassword(null!, "Admin@123"),
//            };



//            modelBuilder.Entity<User>().HasData(adminUser);

//            modelBuilder.Entity<UserRoleAssociation>().HasData(
//                new UserRoleAssociation
//                {
//                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
//                    UserId = adminUser.Id,
//                    RoleId = "11111111-1111-1111-1111-777777777777"
//                }

//            );

//            #endregion

//            #region ===== Permissions =====

//            modelBuilder.Entity<Permission>().HasData(

//                new Permission { Id = permSuperAdminId, Name = "SuperAdmin.All", Description = "Super Admin" }
//            );

//            #endregion

//            #region ===== User Permissions (Admin) =====

//            var allPermissionIds = new[]
//            {
//                permSuperAdminId
//            };

//            var userPermissions = allPermissionIds.Select(pid =>
//                new UserPermission
//                {
//                    Id = Guid.NewGuid(),
//                    UserId = adminUser.Id,
//                    PermissionId = pid
//                }
//            );

//            modelBuilder.Entity<UserPermission>().HasData(userPermissions);

//            #endregion

//        }
//    }
//}

