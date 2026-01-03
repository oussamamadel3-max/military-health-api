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

                // 1) تحديد الدور (مسموح فقط: SuperAdmin أو BranchManager)
                var actorRoles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                var isSuperAdmin = actorRoles.Any(n => n == "SUPERADMIN");
                var isBranchManager = actorRoles.Any(n => n == "BRANCHMANAGER");

                if (!isSuperAdmin && !isBranchManager)
                    return Result<DepartmentDto>.Failure(new[] { "غير مسموح بإنشاء الأقسام." });

                // 2) تحديد الفرع الفعّال
                long effectiveBranchId;

                if (isSuperAdmin)
                {
                    if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                        return Result<DepartmentDto>.Failure(new[] { "يجب تحديد الفرع عند إنشاء قسم (Admin)." });

                    var branchExists = await _context.Branchs
                        .AsNoTracking()
                        .AnyAsync(b => b.BranchId == dto.BranchId.Value);

                    if (!branchExists)
                        return Result<DepartmentDto>.Failure(new[] { "الفرع غير موجود." });

                    effectiveBranchId = dto.BranchId.Value;
                }
                else
                {
                    // BranchManager: الفرع من حسابه فقط
                    if (!actor.BranchId.HasValue)
                        return Result<DepartmentDto>.Failure(new[] { "لا يمكن تحديد فرع مدير الفرع." });

                    // إذا بدك تمنع مدير الفرع يكون تابع لقسم:
                    if (actor.DepartmentId.HasValue)
                        return Result<DepartmentDto>.Failure(new[] { "حساب مدير الفرع يجب ألا يكون مرتبطًا بقسم." });

                    effectiveBranchId = actor.BranchId.Value;

                    // تجاهل أي قيمة مرسلة من العميل
                    dto.BranchId = effectiveBranchId;
                }

                // 3) صلاحيات الحقول
                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Department", _fieldPermissionHelper, userId, roleIds);

                if (errors.Any())
                    return Result<DepartmentDto>.Failure(errors.ToArray());

                // 4) فريدية Code ضمن الفرع
                var code = (dto.Code ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    var duplicate = await _context.Departments
                        .AsNoTracking()
                        .AnyAsync(d => d.BranchId == effectiveBranchId && d.Code == code);

                    if (duplicate)
                        return Result<DepartmentDto>.Failure(new[] { "رمز القسم مستخدم ضمن هذا الفرع." });
                }

                // 5) إنشاء
                var entity = _mapper.Map<Department>(dto);
                entity.BranchId = effectiveBranchId;
                entity.Description ??= string.Empty;

                _context.Departments.Add(entity);
                await _context.SaveChangesAsync();

                // 6) إخراج + صلاحيات الحقول
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
        #region Update department
        public async Task<Result<string>> UpdateAsync(long departmentId, UpdateDepartmentDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                // 0) القسم الحالي
                var entity = await _context.Departments.FindAsync(departmentId);
                if (entity == null)
                    return Result<string>.Failure(new[] { "القسم غير موجود." });

                // 1) المستخدم المنفّذ
                var actor = await _context.Users
                    .AsNoTracking()
                    .OfType<User>()
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (actor is null)
                    return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

                // 2) أدوار المنفّذ (فقط SuperAdmin أو BranchManager)
                var actorRoles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                var isSuperAdmin = actorRoles.Any(n => n == "SUPERADMIN");
                var isBranchManager = actorRoles.Any(n => n == "BRANCHMANAGER");

                if (!isSuperAdmin && !isBranchManager)
                    return Result<string>.Failure(new[] { "غير مسموح بتعديل الأقسام." });

                // 3) BranchManager: فقط ضمن فرعو + ممنوع يغيّر BranchId
                if (!isSuperAdmin)
                {
                    // تأكيد أنه فعلاً مدير فرع (حسب منطقك: BranchId موجود و DepartmentId = null)
                    if (!actor.BranchId.HasValue || actor.DepartmentId.HasValue)
                        return Result<string>.Failure(new[] { "غير مسموح بتعديل الأقسام إلا لمدير الفرع أو SuperAdmin." });

                    // القسم الهدف لازم يكون ضمن فرع المدير
                    if (entity.BranchId != actor.BranchId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك تعديل قسم خارج فرعك." });

                    // ممنوع تغيير الفرع من العميل
                    if (dto.BranchId.HasValue && dto.BranchId.Value != entity.BranchId)
                        return Result<string>.Failure(new[] { "غير مسموح لمدير الفرع بتغيير فرع القسم." });

                    // أو إذا بتحب تتجاهل بدل الرفض:
                    dto.BranchId = null;
                }

                // 4) صلاحيات الحقول (بعد تثبيت/تقييد BranchId)
                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Department", _fieldPermissionHelper, userId, roleIds);

                if (errors.Any())
                    return Result<string>.Failure(errors.ToArray());

                // 5) حساب القيم الهدف (BranchId للـ SuperAdmin فقط يمكن تغييره)
                var targetBranchId = isSuperAdmin
                    ? (dto.BranchId ?? entity.BranchId)
                    : entity.BranchId;

                var targetCode = string.IsNullOrWhiteSpace(dto.Code) ? entity.Code : dto.Code.Trim();

                // 6) تحقق الفرع موجود إذا SuperAdmin غيّره
                if (isSuperAdmin && dto.BranchId.HasValue && dto.BranchId.Value != entity.BranchId)
                {
                    var branchExists = await _context.Branchs
                        .AsNoTracking()
                        .AnyAsync(b => b.BranchId == targetBranchId);

                    if (!branchExists)
                        return Result<string>.Failure(new[] { "الفرع غير موجود." });
                }

                // 7) فريدية الكود ضمن الفرع
                var exists = await _context.Departments
                    .AsNoTracking()
                    .AnyAsync(d =>
                        d.DepartmentId != departmentId &&
                        d.BranchId == targetBranchId &&
                        d.Code == targetCode);

                if (exists)
                    return Result<string>.Failure(new[] { "رمز القسم مستخدم ضمن هذا الفرع." });

                // 8) تطبيق التعديلات
                _mapper.Map(dto, entity);

                // ضمان الربط النهائي
                entity.BranchId = targetBranchId;
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

        #endregion

        #region Delete department
        public async Task<Result<string>> DeleteAsync(long departmentId)
        {
            try
            {
                var entity = await _context.Departments.FindAsync(departmentId);
                if (entity == null) return Result<string>.Failure(new[] { "القسم غير موجود." });

                // منع حذف قسم عليه مستخدمون أو مجموعات أرشفة أو وثائق
                var hasUsers = await _context.Users.AnyAsync(u => u.DepartmentId == departmentId);
                if (hasUsers) return Result<string>.Failure(new[] { "لا يمكن حذف القسم لوجود مستخدمين مرتبطين به." });

                var hasGroups = await _context.ArchiveGroups.AnyAsync(g => g.DepartmentId == departmentId);
                if (hasGroups) return Result<string>.Failure(new[] { "لا يمكن حذف القسم لوجود مجموعات أرشفة مرتبطة به." });

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

            // معلومات الممثل (نجيب فقط ما يلزم)
            var actorInfo = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.BranchId, u.DepartmentId })
                .FirstOrDefaultAsync();

            if (actorInfo is null)
                return Result<IEnumerable<DropDownDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var query = _context.Departments.AsNoTracking().AsQueryable();

            if (isSuperAdmin)
            {
                // يرى الكل
            }
            else if (isBranchManager)
            {
                if (!actorInfo.BranchId.HasValue)
                    return Result<IEnumerable<DropDownDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                query = query.Where(d => d.BranchId == actorInfo.BranchId.Value);
            }
            else
            {
                // موظّف قسم
                if (!actorInfo.DepartmentId.HasValue)
                    return Result<IEnumerable<DropDownDto>>.Success(Array.Empty<DropDownDto>());

                query = query.Where(d => d.DepartmentId == actorInfo.DepartmentId.Value);
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
        public async Task<Result<PaginatedList<DepartmentDto>>> GetDepartmentsAsync(int pageNumber = 1, int pageSize = 10, long? branchId = null, string? name = null, bool? isActive = null)
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

                long? actorBranchId = actor.BranchId;
                long? actorDepartmentId = actor.DepartmentId;

                // 3) تحديد النطاق النهائي
                long? effectiveBranchId = null;
                long? effectiveDepartmentId = null;

                if (actorIsSuperAdmin)
                {
                    // يشوف كل شيء، ولو مرّر branchId نفلتر عليه
                    effectiveBranchId = branchId;
                }
                else if (actorIsBranchManager)
                {
                    if (!actorBranchId.HasValue)
                        return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "لا يمكن تحديد فرع المنفّذ." });

                    // تجاهل أي branchId مختلف
                    effectiveBranchId = actorBranchId.Value;
                }
                else
                {
                    // موظف قسم: يرى قسمه فقط
                    if (!actorDepartmentId.HasValue)
                        return Result<PaginatedList<DepartmentDto>>.Failure(new[] { "لا يمكن تحديد قسم المنفّذ." });

                    effectiveDepartmentId = actorDepartmentId.Value;

                    // ❗ تجاهل فلتر البحث بالاسم لموظف القسم
                    name = null;
                }

                // 4) بناء الاستعلام
                var query = _context.Departments.AsNoTracking().AsQueryable();

                if (effectiveDepartmentId.HasValue)
                {
                    query = query.Where(d => d.DepartmentId == effectiveDepartmentId.Value);
                }
                else if (effectiveBranchId.HasValue)
                {
                    query = query.Where(d => d.BranchId == effectiveBranchId.Value);
                }
                // سوبر أدمن دون branchId = يرى الكل

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

                var query = _context.Departments.AsNoTracking().AsQueryable();

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
