using AutoMapper;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services.AuthServices
{
    public class PermissionService : IPermissionService
    {
        private readonly MainDbContext _context;
        private const int MaxPageSize = 100;
        private readonly IMapper _mapper;
        private readonly RoleManager<Role> _roleManager;

        public PermissionService(MainDbContext context, IMapper mapper, RoleManager<Role> roleManager)
        {
            _context = context;
            _mapper = mapper;
            _roleManager = roleManager;
        }


        #region         الانشاء 
        public async Task<Result<PermissionDto>> CreatePermissionAsync(CreatePermissionDto dto)
        {
            if (await _context.Permissions.AnyAsync(p => p.Name == dto.Name))
                return Result<PermissionDto>.Failure(new List<string> { "الصلاحية موجودة مسبقاَ" }, null);

            var permission = new Permission { Name = dto.Name, Description = dto.Description };

            _context.Permissions.Add(permission);

            await _context.SaveChangesAsync();

            return Result<PermissionDto>.Success(new PermissionDto(permission.Id, permission.Name, permission.Description, permission.IsActive));
        }
        #endregion


        #region         الحذف 
        public async Task<Result<string>> DeletePermissionAsync(string id)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission == null)
                return Result<string>.Failure(new List<string> { "الصلاحية غير موجودة" }, null);

            _context.Permissions.Remove(permission);
            await _context.SaveChangesAsync();

            return Result<string>.Success("تم الحذف بنجاح");
        }
        #endregion


        #region         جلب حسب المعرف
        public async Task<Result<PermissionDto>> GetByIdAsync(string id)
        {
            var errors = new List<string>();

            var permission = await _context.Permissions.FindAsync(Guid.Parse(id));

            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<PermissionDto>.Failure(errors, null);
            }

            var dto = new PermissionDto(permission.Id, permission.Name, permission.Description ?? string.Empty, permission.IsActive);


            return Result<PermissionDto>.Success(dto);
        }
        #endregion


        #region         التعديل
        public async Task<Result<string>> UpdatePermissionAsync(Guid id, UpdatePermissionDto dto)
        {
            var permission = await _context.Permissions.FindAsync(id);
            if (permission == null)
                return Result<string>.Failure(new List<string> { "الصلاحية غير موجودة" }, null);

            permission.Name = dto.Name;
            permission.Description = dto.Description;
            await _context.SaveChangesAsync();

            return Result<string>.Success("تم التحديث بنجاح");
        }
        #endregion


        #region      جلب الكل
        public async Task<Result<List<PermissionDto>>> GetPermissionsAsync()
        {
            var query = _context.Permissions
                .AsNoTracking()
                .OrderBy(p => p.Name);

            var items = await query
                .Select(p => new PermissionDto(p.Id, p.Name ?? string.Empty, p.Description ?? string.Empty, p.IsActive))
                .ToListAsync();

            return Result<List<PermissionDto>>.Success(items);
        }

        #endregion


        #region         جلب الصلاحيات مع الادوار التي ترتبط بها
        public async Task<Result<List<PermissionWithRolesResultDto>>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null)
        {
            try
            {
                // الكويري الأساسي
                var query = _context.Permissions
                    .AsNoTracking()
                    .Include(p => p.RolePermissions)
                        .ThenInclude(rp => rp.Role)
                    .AsQueryable();

                // فلترة بالاسم (جزئي، غير حساس لحالة الأحرف)
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var s = name.Trim();
                    query = query.Where(p => EF.Functions.Like(p.Name, $"%{s}%"));
                }

                // فلترة بالحالة
                if (isActive.HasValue)
                {
                    query = query.Where(p => p.IsActive == isActive.Value);
                }

                // ترتيب
                query = query.OrderBy(p => p.Name).ThenBy(p => p.Id);

                // جلب جميع النتائج
                var permissions = await query.ToListAsync();

                // إسقاط للـ DTO
                var permissionDtos = permissions.Select(p => new PermissionWithRolesResultDto
                {
                    Roles = p.RolePermissions.Select(rp => new RoleDto
                    {
                        Id = rp.Role.Id,
                        Name = rp.Role.Name,
                        Description = rp.Role.Description,
                        IsActive = rp.Role.IsActive
                    }).ToList(),

                    Permissions = new List<PermissionWithRolesDto>
            {
                new PermissionWithRolesDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    IsActive = p.IsActive,
                    AssignedRoleIds = p.RolePermissions.Select(rp => rp.RoleId).ToList(),
                    AssignedRolesCount = p.RolePermissions.Count
                }
            }
                }).ToList();

                return Result<List<PermissionWithRolesResultDto>>.Success(permissionDtos);
            }
            catch (Exception ex)
            {
                return Result<List<PermissionWithRolesResultDto>>
                    .Failure(new List<string> { ex.Message }, null);
            }
        }


        #endregion


        #region         تعين او الغاء تعين صلاحية لدور معين
        public async Task<Result<string>> TogglePermissionForRoleAsync(string roleId, Guid permissionId)
        {
            var errors = new List<string>();

            // جلب الرول
            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null)
            {
                errors.Add("الدور غير موجود");
                return Result<string>.Failure(errors, null);
            }

            // جلب الصلاحية
            var permission = await _context.Permissions.FindAsync(permissionId);
            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<string>.Failure(errors, null);
            }

            // التحقق إن كانت موجودة مسبقاً
            var existing = await _context.RolePermissions
                .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId);

            if (existing != null)
            {
                // إذا موجودة، نلغي الإسناد
                _context.RolePermissions.Remove(existing);
                await _context.SaveChangesAsync();
                return Result<string>.Success("تم إلغاء اسناد الصلاحية للدور بنجاح");
            }

            // إذا غير موجودة، نضيف الإسناد
            var rolePermission = new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            };

            _context.RolePermissions.Add(rolePermission);
            await _context.SaveChangesAsync();
            return Result<string>.Success("تم اسناد الصلاحية للدور بنجاح");
        }

        public async Task<Result<string>> TogglePermissionStatusAsync(Guid permissionId)
        {
            var errors = new List<string>();

            // جلب الصلاحية
            var permission = await _context.Permissions.FindAsync(permissionId);
            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<string>.Failure(errors, null);
            }

            // نفترض أن عندك خاصية IsActive في جدول Permission
            permission.IsActive = !permission.IsActive;

            _context.Permissions.Update(permission);
            await _context.SaveChangesAsync();

            var status = permission.IsActive ? "مفعلة" : "معطلة";
            return Result<string>.Success($"تم تغيير حالة الصلاحية إلى: {status}");
        }

        #endregion


    }
}

