using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DepartmentDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services
{
    public class DepartmentService : IDepartmentService
    {
        private readonly MainDbContext _context;
        private readonly FieldPermissionHelper _fieldPermissionHelper;
        private readonly IUserContextService _userContext;
        private readonly ICurrentUserService _currentUserService;
        private readonly IMapper _mapper;

        public DepartmentService(MainDbContext context,FieldPermissionHelper fieldPermissionHelper,IUserContextService userContext, ICurrentUserService currentUserService, IMapper mapper)
        {
            _context = context;
            _fieldPermissionHelper = fieldPermissionHelper;
            _userContext = userContext;
            _currentUserService = currentUserService;
            _mapper = mapper;
        }

        private string CurrentUserId => _userContext.UserId ?? string.Empty;
        private List<string> CurrentRoleIds => _userContext.RoleIds?.ToList() ?? new List<string>();
        private IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> JwtFieldPerms =>
            _userContext.FieldPermissions ?? new Dictionary<string, Domain.Enums.FieldAccessType>();

        #region Create department
        public async Task<Result<DepartmentDto>> CreateAsync(CreateDepartmentDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                // 0) المستخدم المنفّذ
                var actor = await _context.Users
                    .AsNoTracking()
                    .OfType<User>()
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (actor is null)
                    return Result<DepartmentDto>.Failure(new[] { "المستخدم الحالي غير موجود." });

                // 1) تحديد الدور — SuperAdmin فقط
                var actorRoles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                var isSuperAdmin = actorRoles.Any(n => n == "SUPERADMIN");

                if (!isSuperAdmin)
                    return Result<DepartmentDto>.Failure(new[] { "غير مسموح بإنشاء الأقسام." });

                // 2) صلاحيات الحقول
                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Department", _fieldPermissionHelper, userId, roleIds);

                if (errors.Any())
                    return Result<DepartmentDto>.Failure(errors.ToArray());

                // 3) فريدية رمز القسم
                var code = (dto.Code ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    var duplicate = await _context.Departments
                        .AsNoTracking()
                        .AnyAsync(d => d.Code == code);

                    if (duplicate)
                        return Result<DepartmentDto>.Failure(new[] { "رمز القسم مستخدم مسبقاً." });
                }

                // 4) إنشاء
                var entity = _mapper.Map<Department>(dto);
                entity.Description ??= string.Empty;

                _context.Departments.Add(entity);
                await _context.SaveChangesAsync();

                // 5) إخراج + صلاحيات الحقول
                var outDto = _mapper.Map<DepartmentDto>(entity);
                outDto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    outDto, userId, roleIds, "Department", jwtFieldPermissions);

                return Result<DepartmentDto>.Success(outDto);
            }
            catch (Exception ex)
            {
                return Result<DepartmentDto>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
            }
        }

        #endregion

        #region Update department
        public async Task<Result<string>> UpdateAsync(long departmentId, UpdateDepartmentDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var entity = await _context.Departments.FindAsync(departmentId);
                if (entity == null)
                    return Result<string>.Failure(new[] { "القسم غير موجود." });

                var actorRoles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                var isSuperAdmin = actorRoles.Any(n => n == "SUPERADMIN");
                if (!isSuperAdmin)
                    return Result<string>.Failure(new[] { "غير مسموح بتعديل الأقسام." });

                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Department", _fieldPermissionHelper, userId, roleIds);

                if (errors.Any())
                    return Result<string>.Failure(errors.ToArray());

                var targetCode = string.IsNullOrWhiteSpace(dto.Code) ? entity.Code : dto.Code.Trim();

                var exists = await _context.Departments
                    .AsNoTracking()
                    .AnyAsync(d =>
                        d.DepartmentId != departmentId &&
                        d.Code == targetCode);

                if (exists)
                    return Result<string>.Failure(new[] { "رمز القسم مستخدم مسبقاً." });

                _mapper.Map(dto, entity);
                entity.Description ??= string.Empty;

                _context.Departments.Update(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم تحديث بيانات القسم بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء التحديث: {ex.Message}" });
            }
        }
        #endregion

        #region Delete department
        public async Task<Result<string>> DeleteAsync(long departmentId)
        {
            try
            {
                var entity = await _context.Departments.FindAsync(departmentId);
                if (entity == null) return Result<string>.Failure(new[] { "القسم غير موجود." });

                // منع حذف قسم عليه مستخدمون أو وثائق
                var hasUsers = await _context.Users.AnyAsync(u => u.DepartmentId == departmentId);
                if (hasUsers) return Result<string>.Failure(new[] { "لا يمكن حذف القسم لوجود مستخدمين مرتبطين به." });

                var hasDocs = await _context.Documents.AnyAsync(d => d.DepartmentId == departmentId);
                if (hasDocs) return Result<string>.Failure(new[] { "لا يمكن حذف القسم لوجود وثائق مرتبطة به." });

                _context.Departments.Remove(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم حذف القسم بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
            }
        }
        #endregion

        #region GetById department
        public async Task<Result<DepartmentDto>> GetByIdAsync(long departmentId)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var dto = await _context.Departments
                    .AsNoTracking()
                    .Where(d => d.DepartmentId == departmentId)
                    .ProjectTo<DepartmentDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (dto == null) return Result<DepartmentDto>.Failure(new[] { "القسم غير موجود." });

                dto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(dto, userId, roleIds, "Department", jwtFieldPermissions);
                return Result<DepartmentDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<DepartmentDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Dropdown list
        public async Task<Result<IEnumerable<DropDownDto>>> GetDepartmentsDropdownAsync()
        {
            var userId = _currentUserService.UserId ?? string.Empty;

            // أدوار المستخدم (NormalizedName)
            var roleNames = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuperAdmin = roleNames.Any(n => n == "SUPERADMIN");
            bool isBranchManager = roleNames.Any(n => n == "BRANCHMANAGER");

            var actor = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DepartmentId })
                .FirstOrDefaultAsync();

            if (actor is null)
                return Result<IEnumerable<DropDownDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var query = _context.Departments.AsNoTracking().AsQueryable();

            if (isSuperAdmin || isBranchManager)
            {
                // يرى كل الأقسام
            }
            else
            {
                if (!actor.DepartmentId.HasValue)
                    return Result<IEnumerable<DropDownDto>>.Success(Array.Empty<DropDownDto>());

                query = query.Where(d => d.DepartmentId == actor.DepartmentId.Value);
            }

            var data = await query
                .OrderBy(d => d.DepartmentName)
                .Select(d => new DropDownDto
                {
                    Id = d.DepartmentId,
                    Name = d.DepartmentName
                })
                .ToListAsync();

            return Result<IEnumerable<DropDownDto>>.Success(data);
        }
        #endregion

        #region GetAllDepartments Paged list (simple filter: code, name, isActive) 
        public async Task<Result<PaginatedList<DepartmentDto>>> GetDepartmentsAsync(int pageNumber = 1, int pageSize = 10, string? name = null, bool? isActive = null)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                // 1) تحديد دور المنفّذ (NormalizedName للترجمة)
                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool actorIsSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool actorIsBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                // 2) جلب بيانات المنفّذ
                var actor = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId) as User;

                if (actor is null)
                    return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                long? effectiveDepartmentId = null;

                if (actorIsSuperAdmin || actorIsBranchManager)
                {
                    // يرى كل الأقسام
                }
                else
                {
                    if (!actor.DepartmentId.HasValue)
                        return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "لا يمكن تحديد قسم المنفّذ." });

                    effectiveDepartmentId = actor.DepartmentId.Value;
                    name = null;
                }

                var query = _context.Departments.AsNoTracking().AsQueryable();

                if (effectiveDepartmentId.HasValue)
                    query = query.Where(d => d.DepartmentId == effectiveDepartmentId.Value);

                                if (!string.IsNullOrWhiteSpace(name))
                    query = query.Where(d => d.DepartmentName.Contains(name));

                if (isActive.HasValue)
                    query = query.Where(d => d.IsActive == isActive.Value);

                // 5) الإسقاط والصفحة
                var projected = query
                    .OrderByDescending(d => d.CreatedAt)
                    .ThenByDescending(d => d.DepartmentId)
                    .ProjectTo<DepartmentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DepartmentDto>.CreateAsync(projected, pageNumber, pageSize);

                // 6) تطبيق صلاحيات الحقول
                var permittedItems = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Department", jwt);

                var result = new PaginatedList<DepartmentDto>(
                    permittedItems, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DepartmentDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DepartmentDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }

        #endregion

        #region Search (advanced + paged)
        public async Task<Result<PaginatedList<DepartmentDto>>> SearchAsync(DepartmentSearchCriteria criteria, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool actorIsSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool actorIsBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                var actor = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId) as User;

                if (actor is null)
                    return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var query = _context.Departments.AsNoTracking().AsQueryable();

                if (!actorIsSuperAdmin && !actorIsBranchManager)
                {
                    if (!actor.DepartmentId.HasValue)
                        return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "لا يمكن تحديد قسم المنفّذ." });

                    query = query.Where(d => d.DepartmentId == actor.DepartmentId.Value);
                }

                if (!string.IsNullOrWhiteSpace(criteria.Code))
                    query = query.Where(d => d.Code.Contains(criteria.Code));

                if (!string.IsNullOrWhiteSpace(criteria.DepartmentName))
                    query = query.Where(d => d.DepartmentName.Contains(criteria.DepartmentName));

                if (criteria.IsActive.HasValue)
                    query = query.Where(d => d.IsActive == criteria.IsActive.Value);

                query = criteria.SortBy?.ToLower() switch
                {
                    "code" => criteria.SortDescending ? query.OrderByDescending(d => d.Code) : query.OrderBy(d => d.Code),
                    "departmentname" => criteria.SortDescending ? query.OrderByDescending(d => d.DepartmentName) : query.OrderBy(d => d.DepartmentName),
                    "createdat" => criteria.SortDescending ? query.OrderByDescending(d => d.CreatedAt) : query.OrderBy(d => d.CreatedAt),
                    _ => query.OrderBy(d => d.DepartmentId)
                };

                var projected = query.ProjectTo<DepartmentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DepartmentDto>.CreateAsync(projected, pageNumber, pageSize);

                var permittedItems = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Department", jwt);

                var result = new PaginatedList<DepartmentDto>(
                    permittedItems, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DepartmentDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DepartmentDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
            }
        }
        #endregion

    }
}
