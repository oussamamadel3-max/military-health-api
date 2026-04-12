using AutoMapper;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Utilities;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ArchiveProject.Application.Common.Localization;

namespace ArchiveProject.Infrastructure.Services.AuthServices
{
    public class RoleService : IRoleService
    {
        private readonly RoleManager<Role> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly MainDbContext _context;
        private readonly IMapper _mapper;
        private readonly ICurrentUserService _currentUserService;

        public RoleService(RoleManager<Role> roleManager, MainDbContext context, UserManager<User> userManager, IMapper mapper, ICurrentUserService currentUserService)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _context = context;
            _mapper = mapper;
            _currentUserService = currentUserService;
        }

        #region         الانشاء
        public async Task<Result<RoleDto>> CreateRoleAsync(CreateRoleDto newRole)
        {
            var errorList = new List<string>();

            var role = _mapper.Map<Role>(newRole);

            // ملء الحقول يدويًا لأن Interceptor لا يعمل مع RoleManager
            role.CreatedAt = DateTime.Now;
            role.CreatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.CreateAsync(role);

            if (!identityResult.Succeeded)
            {
                errorList.AddRange(identityResult.Errors.Select(e => e.Description));
                return Result<RoleDto>.Failure(errorList, null);
            }

            var roleDto = _mapper.Map<RoleDto>(role);

            return Result<RoleDto>.Success(roleDto);
        }
        #endregion


        #region         التعديل
        public async Task<Result<string>> UpdateRoleAsync(string id, UpdateRoleDto updatedRole)
        {
            var errorList = new List<string>();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                errorList.Add("الدور غير موجود.");
                return Result<string>.Failure(errorList, null);
            }

            // المابينغ من DTO إلى الـ Role (مع الحفاظ على القيم الموجودة)
            _mapper.Map(updatedRole, role);

            // تحديث الحقول يدوياً لأن Interceptor لا يعمل مع RoleManager
            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.UpdateAsync(role);
            if (!identityResult.Succeeded)
            {
                errorList.AddRange(identityResult.Errors.Select(e => e.Description));
                return Result<string>.Failure(errorList, null);
            }

            return Result<string>.Success("تم تحديث الدور بنجاح.");
        }
        #endregion


        #region         الحذف
        public async Task<Result<string>> DeleteRoleAsync(string id)
        {
            var errorList = new List<string>();

            var role = await _roleManager.FindByIdAsync(id);

            if (role == null)
            {
                errorList.Add("الدور غير موجود");
                return Result<string>.Failure(errorList, null);
            }

            await _roleManager.DeleteAsync(role);

            return Result<string>.Success("تم الحذف بنجاح");
        }
        #endregion


        #region      جلب حسب المعرف 
        public async Task<Result<RoleDto>> GetById(string id)
        {
            var errorList = new List<string>();

            var role = _roleManager.Roles
                .FirstOrDefault(r => r.Id == id);

            if (role == null)
            {
                await Task.Delay(1);
                errorList.Add("الدور غير موجود");
                return Result<RoleDto>.Failure(errorList, null);
            }

            var roleDto = new RoleDto();

            roleDto.Name = role.Name ?? string.Empty;

            roleDto.Id = role.Id;

            return Result<RoleDto>.Success(roleDto);
        }
        #endregion


        #region          جلب الصلاحيات مع تفاصيلها 
        public async Task<Result<PaginatedList<RoleDetailsDto>>> GetRolesWithDetailsAsync(int pageNumber, int pageSize, string? name = null, bool? isActive = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var rolesQuery = _roleManager.Roles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(name.Trim());
                rolesQuery = rolesQuery.Where(r => EF.Functions.Like(r.Name!, $"%{s}%"));
            }

            if (isActive.HasValue)
                rolesQuery = rolesQuery.Where(r => r.IsActive == isActive.Value);

            // 🚫 استبعاد دور SuperAdmin
            rolesQuery = rolesQuery.Where(r => r.NormalizedName != "SUPERADMIN");

            rolesQuery = rolesQuery.OrderBy(r => r.Name);

            var totalRecords = await rolesQuery.CountAsync();

            var roles = await rolesQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (roles.Count == 0)
            {
                var empty = new PaginatedList<RoleDetailsDto>(new List<RoleDetailsDto>(), totalRecords, pageNumber, pageSize);
                return Result<PaginatedList<RoleDetailsDto>>.Success(empty);
            }

            var roleIds = roles.Select(r => r.Id).ToList();

            var permissionsDict = await (from rp in _context.RolePermissions
                                         join p in _context.Permissions on rp.PermissionId equals p.Id
                                         where roleIds.Contains(rp.RoleId) && p.IsActive
                                         select new
                                         {
                                             rp.RoleId,
                                             Permission = new PermissionInfoDto
                                             {
                                                 Id = p.Id,
                                                 Name = AuthDisplayNames.PermissionAr(p.Name)
                                             }
                                         })
                                         .GroupBy(x => x.RoleId)
                                         .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Permission).ToList());

            var assignedUsersDict = await _context.UserRoleAssociations
                .Where(ur => roleIds.Contains(ur.RoleId))
                .GroupBy(ur => ur.RoleId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            var userIds = roles
                .SelectMany(r => new[] { r.CreatedBy, r.UpdatedBy })
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            var usersDict = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName);

            var roleDtos = roles.Select(role =>
            {
                var dto = _mapper.Map<RoleDetailsDto>(role);

                dto.Name = AuthDisplayNames.RoleAr(role.Name);

                permissionsDict.TryGetValue(role.Id, out var perms);
                assignedUsersDict.TryGetValue(role.Id, out var assignedCount);

                dto.Permissions = perms ?? new List<PermissionInfoDto>();
                dto.PermissionsCount = dto.Permissions.Count;
                dto.AssignedUsersCount = assignedCount;

                dto.CreatedByName = !string.IsNullOrEmpty(role.CreatedBy) && usersDict.TryGetValue(role.CreatedBy, out var cb) ? cb : null;
                dto.UpdatedByName = !string.IsNullOrEmpty(role.UpdatedBy) && usersDict.TryGetValue(role.UpdatedBy, out var ub) ? ub : null;

                return dto;
            }).ToList();

            var result = new PaginatedList<RoleDetailsDto>(roleDtos, totalRecords, pageNumber, pageSize);
            return Result<PaginatedList<RoleDetailsDto>>.Success(result);
        }

        #endregion


        #region         اسناد مستخدم الى دور 
        public async Task<Result<string>> AssignUserToRole(string userId, List<string> roleIds)
        {
            var errorList = new List<string>();

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                errorList.Add("المستخدم غير موجود");
                return Result<string>.Failure(errorList, null);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            var activeRoles = await _roleManager.Roles
                .Where(r => roleIds.Contains(r.Id) && r.IsActive) // فقط الأدوار النشطة
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            // التحقق من الأدوار غير النشطة
            var activeRoleIds = activeRoles.Select(r => r.Id).ToList();
            var inactiveRoleIds = roleIds.Except(activeRoleIds).ToList();

            if (inactiveRoleIds.Any())
            {
                errorList.Add($"الأدوار التالية غير نشطة: {string.Join(", ", inactiveRoleIds)}");
                return Result<string>.Failure(errorList, null);
            }

            var updatedRoleNames = activeRoles
                .Select(r => r.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            var addedRoles = updatedRoleNames.Except(currentRoles).ToList();

            var removedRoles = currentRoles.Except(updatedRoleNames).ToList();

            if (addedRoles.Any())
            {
                await _userManager.AddToRolesAsync(user, addedRoles);
            }

            if (removedRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, removedRoles);
            }

            return Result<string>.Success("Save role request");
        }
        #endregion


        #region         اسناد صلاحية الى دور 
        //public async Task<Result<string>> TogglePermissionsForRoleAsync(AssignPermissionsDto dto)
        //{
        //    var errors = new List<string>();

        //    if (string.IsNullOrWhiteSpace(dto.RoleId))
        //        return Result<string>.Failure(new[] { "RoleId مطلوب." }, null);

        //    // 1) الدور
        //    var role = await _roleManager.FindByIdAsync(dto.RoleId);
        //    if (role == null)
        //        return Result<string>.Failure(new[] { "الدور غير موجود." }, null);

        //    // 2) تحديث يدوي لأن RoleManager لا يمر على interceptor
        //    role.UpdatedAt = DateTime.Now;
        //    role.UpdatedBy = _currentUserService.UserId;

        //    // 3) ids الواردة
        //    var requestedIds = dto.PermissionsIds?.Where(x => x != Guid.Empty).Distinct().ToList()
        //                       ?? new List<Guid>();

        //    if (!requestedIds.Any())
        //        return Result<string>.Success("لا توجد صلاحيات للتعديل.");

        //    // 4) تأكيد الصلاحيات موجودة ونشطة
        //    var activeValidIds = await _context.Permissions
        //        .Where(p => requestedIds.Contains(p.Id) && p.IsActive)
        //        .Select(p => p.Id)
        //        .ToListAsync();

        //    var missingOrInactive = requestedIds.Except(activeValidIds).ToList();
        //    if (missingOrInactive.Any())
        //        return Result<string>.Failure(
        //            new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

        //    // 5) الصلاحيات الحالية للدور (فقط التي ضمن المطلوب)
        //    var existingRows = await _context.RolePermissions
        //        .Where(rp => rp.RoleId == dto.RoleId && activeValidIds.Contains(rp.PermissionId))
        //        .ToListAsync();

        //    var existingIds = existingRows.Select(rp => rp.PermissionId).ToHashSet();

        //    // 6) Toggle
        //    var toRemove = existingRows; // الموجودة => نحذفها
        //    var toAddIds = activeValidIds.Where(pid => !existingIds.Contains(pid)).ToList(); // غير الموجودة => نضيفها

        //    using var tx = await _context.Database.BeginTransactionAsync();
        //    try
        //    {
        //        if (toRemove.Any())
        //            _context.RolePermissions.RemoveRange(toRemove);

        //        if (toAddIds.Any())
        //        {
        //            var addRows = toAddIds.Select(pid => new RolePermission
        //            {
        //                RoleId = dto.RoleId,
        //                PermissionId = pid
        //            });

        //            await _context.RolePermissions.AddRangeAsync(addRows);
        //        }

        //        await _context.SaveChangesAsync();

        //        // 7) إبطال توكنات المستخدمين الذين لديهم هذا الدور ليطبق التغيير
        //        await SecurityStampHelper.InvalidateUsersWithRoleAsync(_context, _userManager, dto.RoleId);

        //        await tx.CommitAsync();

        //        return Result<string>.Success(
        //            $"تم تحديث صلاحيات الدور بنجاح. (إضافة: {toAddIds.Count} | إزالة: {toRemove.Count})");
        //    }
        //    catch
        //    {
        //        await tx.RollbackAsync();
        //        throw;
        //    }
        //}
        public async Task<Result<string>> SyncPermissionsForRoleAsync(AssignPermissionsDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RoleId))
                return Result<string>.Failure(new[] { "RoleId مطلوب." }, null);

            var role = await _roleManager.FindByIdAsync(dto.RoleId);
            if (role == null)
                return Result<string>.Failure(new[] { "الدور غير موجود." }, null);

            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            // القائمة النهائية القادمة من الواجهة (الشيكات الحالية)
            var requestedIds = (dto.PermissionsIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            // ✅ فقط النشطة
            var activeValidIds = await _context.Permissions
                .Where(p => requestedIds.Contains(p.Id) && p.IsActive)
                .Select(p => p.Id)
                .ToListAsync();

            var missingOrInactive = requestedIds.Except(activeValidIds).ToList();
            if (missingOrInactive.Any())
                return Result<string>.Failure(
                    new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

            // صلاحيات الدور الحالية
            var existingRolePerms = await _context.RolePermissions
                .Where(rp => rp.RoleId == dto.RoleId)
                .ToListAsync();

            var existingIds = existingRolePerms
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            // ✅ إضافة فقط الجديد
            var toAdd = activeValidIds
                .Where(id => !existingIds.Contains(id))
                .ToList();

            // ✅ حذف فقط اللي انشال من الشيكات (موجود سابقاً وصار غير موجود)
            var toRemove = existingRolePerms
                .Where(rp => !activeValidIds.Contains(rp.PermissionId))
                .ToList();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (toRemove.Any())
                    _context.RolePermissions.RemoveRange(toRemove);

                if (toAdd.Any())
                {
                    var addRows = toAdd.Select(pid => new RolePermission
                    {
                        RoleId = dto.RoleId,
                        PermissionId = pid
                    });
                    await _context.RolePermissions.AddRangeAsync(addRows);
                }

                await _context.SaveChangesAsync();

                await SecurityStampHelper.InvalidateUsersWithRoleAsync(_context, _userManager, dto.RoleId);

                await tx.CommitAsync();

                return Result<string>.Success($"تم تحديث الصلاحيات بنجاح. (إضافة: {toAdd.Count} | حذف: {toRemove.Count})");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        #endregion


        #region          انشاء دور مع الصلاحيات
        public async Task<Result<RoleDetailsDto>> CreateRoleWithPermissionsAsync(CreateRoleWithPermissionsDto dto)
        {
            var errors = new List<string>();

            // 0) تنظيف الاسم
            var roleName = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roleName))
            {
                errors.Add("اسم الدور مطلوب.");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            // 1) التأكد أن الدور غير موجود (بدون حساسية لحالة الأحرف)
            var existingRole = await _roleManager.FindByNameAsync(roleName);
            if (existingRole != null)
            {
                errors.Add("الدور موجود مسبقاً، لا يمكن إضافته مرة أخرى.");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            // 2) تجهيز قائمة الصلاحيات المطلوبة (Distinct لمنع التكرار)
            var inputIds = dto.PermissionIds ?? Enumerable.Empty<Guid>();
            var requestedDistinct = inputIds.Distinct().ToList();
            var inputDuplicatesSkipped = inputIds.Count() - requestedDistinct.Count;

            // 3) جلب الصلاحيات الصحيحة فقط من قاعدة البيانات - فقط الصلاحيات النشطة
            var validIds = await _context.Permissions
                .Where(p => requestedDistinct.Contains(p.Id) && p.IsActive) // فقط الصلاحيات النشطة
                .Select(p => p.Id)
                .ToListAsync();

            // التحقق من الصلاحيات غير النشطة
            var inactiveIds = requestedDistinct.Except(validIds).ToList();
            if (inactiveIds.Any())
            {
                errors.Add($"الصلاحيات التالية غير نشطة: {string.Join(", ", inactiveIds)}");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            var invalidSkipped = requestedDistinct.Count - validIds.Count;

            // 4) إنشاء الدور
            var role = _mapper.Map<Role>(dto);
            role.Name = roleName; // تأكد إن الاسم نظيف
            role.CreatedAt = DateTime.Now;
            role.CreatedBy = _currentUserService.UserId;
            role.IsActive = true;

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var identityResult = await _roleManager.CreateAsync(role);
                if (!identityResult.Succeeded)
                {
                    errors.AddRange(identityResult.Errors.Select(e => e.Description));
                    await tx.RollbackAsync();
                    return Result<RoleDetailsDto>.Failure(errors, null);
                }

                // 5) إسناد الصلاحيات الصحيحة فقط (ما في ExistingIds لأن الدور جديد)
                var toAdd = validIds;
                if (toAdd.Count > 0)
                {
                    var rolePermissions = toAdd.Select(pid => new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = pid
                    });

                    await _context.RolePermissions.AddRangeAsync(rolePermissions);
                }

                await _context.SaveChangesAsync();

                // تحديث SecurityStamp لجميع المستخدمين الذين لديهم هذا الدور
                await SecurityStampHelper.InvalidateUsersWithRoleAsync(_context, _userManager, role.Id);

                await tx.CommitAsync();
            }
            catch (DbUpdateException dbEx)
            {
                await tx.RollbackAsync();
                errors.Add("فشل في حفظ البيانات. يرجى المحاولة لاحقاً.");
                errors.Add(dbEx.GetBaseException().Message);
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            // 6) تجهيز الإخراج + تقرير
            var roleDetails = _mapper.Map<RoleDetailsDto>(role);
            roleDetails.Permissions = await _context.RolePermissions
                .Where(rp => rp.RoleId == role.Id && rp.Permission != null && rp.Permission.IsActive) // فقط الصلاحيات النشطة
                .Include(rp => rp.Permission)
                .Select(rp => new PermissionInfoDto
                {
                    Id = rp.Permission!.Id,
                    Name = rp.Permission.Name ?? string.Empty
                })
                .ToListAsync();

            roleDetails.PermissionsCount = roleDetails.Permissions.Count;

            // ⚠️ في حال عندك حقل رسالة داخل الـ DTO حط التقرير فيه.
            // إن لم يوجد، بإمكانك طباعة/تسجيل التقرير أو تعدّل Result ليحمل رسالة.
            var addedCount = roleDetails.PermissionsCount;
            var skippedCount = invalidSkipped + inputDuplicatesSkipped;
            var report = $"تم إنشاء الدور بنجاح. تمت إضافة {addedCount} صلاحية. " +
                         $"تم تجاوز {skippedCount} (غير موجودة أو مكررة في الإدخال).";

            // إذا كان لديك دعم لرسالة ضمن Result، استخدمه. وإلا أرسل فقط الـ DTO:
            // return Result<RoleDetailsDto>.Success(roleDetails, report);
            return Result<RoleDetailsDto>.Success(roleDetails);
        }

        #endregion


        #region             جلب عدد الادوار الفعالة 
        public async Task<Result<string>> GetActiveRolesCount()
        {
            var count = await _roleManager.Roles
                .Where(r => r.IsActive)
                .CountAsync();

            return Result<string>.Success(count.ToString());
        }
        #endregion


        #region     جلب كل الصلاحيات الفعالة  
        public async Task<Result<List<DropdownRoleDto>>> GetDropdownRolesAsync()
        {
            var roles = await _context.Roles
                .AsNoTracking()
                .Where(r => r.IsActive) // شرط أنو يكون الدور مفعل
                .OrderBy(r => r.Name)
                .Select(r => new DropdownRoleDto
                {
                    roleId = r.Id,
                    name = AuthDisplayNames.RoleAr(r.Name ?? string.Empty)
                })
                .ToListAsync();

            return Result<List<DropdownRoleDto>>.Success(roles);
        }

        public async Task<Result<RoleStatusUpdateDto>> ToggleRoleStatusAsync(Guid roleId)
        {
            // 1. جلب الدور
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId.ToString());
            if (role == null)
                return Result<RoleStatusUpdateDto>.Failure(new List<string> { $"الدور غير موجود." }, null);

            // 2. عمل Toggle للحالة
            role.IsActive = !role.IsActive;
            // تحديث الحقول يدوياً لأن Interceptor لا يعمل مع RoleManager
            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            // 3. حفظ التغيير
            _context.Roles.Update(role);
            await _context.SaveChangesAsync();

            // 4. تحويل إلى DTO باستخدام AutoMapper
            var dto = _mapper.Map<RoleStatusUpdateDto>(role);

            return Result<RoleStatusUpdateDto>.Success(dto);
        }

        #endregion


    }
}

