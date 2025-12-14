using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.ArchiveGroupDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services
{
    public class ArchiveGroupService : IArchiveGroupService
    {
        private readonly MainDbContext _context;
        private readonly FieldPermissionHelper _fieldPermissions;
        private readonly IUserContextService _userContext;
        private readonly IMapper _mapper;

        public ArchiveGroupService(
            MainDbContext context,
            FieldPermissionHelper fieldPermissions,
            IUserContextService userContext,
            IMapper mapper)
        {
            _context = context;
            _fieldPermissions = fieldPermissions;
            _userContext = userContext;
            _mapper = mapper;
        }

        private string UserId => _userContext.UserId ?? string.Empty;
        private List<string> RoleIds => _userContext.RoleIds?.ToList() ?? new List<string>();
        private IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> JwtPerms =>
            _userContext.FieldPermissions ?? new Dictionary<string, Domain.Enums.FieldAccessType>();


        #region Create ArchiveGroup
        //public async Task<Result<ArchiveGroupDto>> CreateAsync(CreateArchiveGroupDto dto)
        //{
        //    try
        //    {
        //        // 1) التحقق من صلاحيات الحقول
        //        var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
        //            dto, JwtPerms, "ArchiveGroup", _fieldPermissions, UserId, RoleIds);

        //        if (errors.Any())
        //            return Result<ArchiveGroupDto>.Failure(errors.ToArray());

        //        // 2) تحقق المرجعيات
        //        var depExists = await _context.Departments.AnyAsync(d => d.DepartmentId == dto.DepartmentId);
        //        if (!depExists) return Result<ArchiveGroupDto>.Failure(new[] { "القسم غير موجود." });

        //        var userExists = await _context.Users.AnyAsync(u => u.Id == dto.UserId);
        //        if (!userExists) return Result<ArchiveGroupDto>.Failure(new[] { "المستخدم غير موجود." });

        //        // 3) إنشاء الكيان من الـ DTO
        //        var entity = _mapper.Map<ArchiveGroup>(dto);

        //        // 4) توليد رقم المجموعة تلقائيًا داخل القسم
        //        var maxNumber = await _context.ArchiveGroups
        //            .Where(g => g.DepartmentId == dto.DepartmentId)
        //            .Select(g => (long?)g.ArchiveGroupNumber)
        //            .MaxAsync() ?? 0;

        //        entity.ArchiveGroupNumber = maxNumber + 1;

        //        _context.ArchiveGroups.Add(entity);

        //        // 5) حفظ مع آلية Retry على التعارض (Unique Index: DepartmentId + ArchiveGroupNumber)
        //        const int maxRetries = 5;
        //        for (int attempt = 1; attempt <= maxRetries; attempt++)
        //        {
        //            try
        //            {
        //                await _context.SaveChangesAsync();
        //                break; // نجحت
        //            }
        //            catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
        //            {
        //                if (attempt == maxRetries)
        //                    return Result<ArchiveGroupDto>.Failure(new[] { "تعذر توليد رقم مجموعة فريد بعد عدة محاولات." });

        //                // إعادة حساب الرقم بسبب حجزه بطلب متزامن
        //                var newMax = await _context.ArchiveGroups
        //                    .Where(g => g.DepartmentId == entity.DepartmentId)
        //                    .Select(g => (long?)g.ArchiveGroupNumber)
        //                    .MaxAsync() ?? 0;

        //                entity.ArchiveGroupNumber = newMax + 1;
        //                // loop will retry
        //            }
        //        }

        //        // 6) إخراج
        //        var outDto = _mapper.Map<ArchiveGroupDto>(entity);
        //        outDto = await _fieldPermissions.ApplyFieldPermissionsAsync(outDto, UserId, RoleIds, "ArchiveGroup", JwtPerms);

        //        return Result<ArchiveGroupDto>.Success(outDto);
        //    }
        //    catch (Exception ex)
        //    {
        //        return Result<ArchiveGroupDto>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
        //    }
        //}
        // DTO كما هو (نحتاج DepartmentId لأن مدير الفرع يحدد القسم)

        public async Task<Result<ArchiveGroupDto>> CreateAsync(CreateArchiveGroupDto dto)
        {
            try
            {
                // المستخدم الحالي
                var actorId = UserId ?? string.Empty;

                // هل هو SuperAdmin؟ (باستخدام NormalizedName حتى يترجمها EF)
                var isSuperAdmin = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == actorId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .AnyAsync(n => n == "SUPERADMIN");

                if (isSuperAdmin)
                    return Result<ArchiveGroupDto>.Failure(new[] { "غير مسموح للسوبر أدمن بإنشاء مجموعة أرشفة عبر هذا المسار." });

                // جلب بيانات المنفّذ لمعرفة فرعه/قسمه
                var actor = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == actorId) as User;

                if (actor is null)
                    return Result<ArchiveGroupDto>.Failure(new[] { "المستخدم الحالي غير موجود." });

                long departmentIdToUse;

                if (actor.DepartmentId.HasValue)
                {
                    // موظف قسم: نأخذ قسمه تلقائيًا ونُهمل القادم من الواجهة
                    departmentIdToUse = actor.DepartmentId.Value;
                    dto.DepartmentId = departmentIdToUse; // نثبّت في الـ DTO قبل فحص صلاحيات الحقول
                }
                else if (actor.BranchId.HasValue)
                {
                    // مدير فرع: يجب أن يختار قسم ضمن فرعه
                    if (dto.DepartmentId <= 0)
                        return Result<ArchiveGroupDto>.Failure(new[] { "يجب تحديد القسم." });

                    var dept = await _context.Departments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.DepartmentId == dto.DepartmentId);

                    if (dept is null)
                        return Result<ArchiveGroupDto>.Failure(new[] { "القسم غير موجود." });

                    if (dept.BranchId != actor.BranchId.Value)
                        return Result<ArchiveGroupDto>.Failure(new[] { "لا يمكنك إنشاء مجموعة أرشفة لقسم خارج فرعك." });

                    departmentIdToUse = dept.DepartmentId;
                }
                else
                {
                    // ليس سوبر، ولا مدير فرع، ولا موظف قسم => غير مسموح
                    return Result<ArchiveGroupDto>.Failure(new[] { "غير مسموح بإنشاء مجموعة الأرشفة." });
                }

                // 1) التحقق من صلاحيات الحقول (بعد ضبط DepartmentId الصحيح)
                var fpErrors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, JwtPerms, "ArchiveGroup", _fieldPermissions, UserId, RoleIds);

                if (fpErrors.Any())
                    return Result<ArchiveGroupDto>.Failure(fpErrors.ToArray());

                // 2) تحقق أن القسم موجود (صار عندنا departmentIdToUse مضمون)
                var depExists = await _context.Departments
                    .AnyAsync(d => d.DepartmentId == departmentIdToUse);
                if (!depExists)
                    return Result<ArchiveGroupDto>.Failure(new[] { "القسم غير موجود." });

                // 3) جهّز الكيان
                var entity = new ArchiveGroup
                {
                    Title = dto.Title?.Trim() ?? string.Empty,
                    DepartmentId = departmentIdToUse,
                    UserId = actorId, // منشئ المجموعة
                    CreatedAt = DateTime.Now
                };

                // 4) توليد رقم المجموعة داخل القسم
                var maxNumber = await _context.ArchiveGroups
                    .Where(g => g.DepartmentId == departmentIdToUse)
                    .Select(g => (long?)g.ArchiveGroupNumber)
                    .MaxAsync() ?? 0;

                entity.ArchiveGroupNumber = maxNumber + 1;

                _context.ArchiveGroups.Add(entity);

                // 5) حفظ مع Retry على التعارض (Unique: DepartmentId + ArchiveGroupNumber)
                const int maxRetries = 5;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await _context.SaveChangesAsync();
                        break; // نجحت
                    }
                    catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
                    {
                        if (attempt == maxRetries)
                            return Result<ArchiveGroupDto>.Failure(new[] { "تعذر توليد رقم مجموعة فريد بعد عدة محاولات." });

                        // أعِد حساب الرقم بسبب عملية متزامنة
                        var newMax = await _context.ArchiveGroups
                            .Where(g => g.DepartmentId == entity.DepartmentId)
                            .Select(g => (long?)g.ArchiveGroupNumber)
                            .MaxAsync() ?? 0;

                        entity.ArchiveGroupNumber = newMax + 1;
                        // ثم يعيد المحاولة
                    }
                }

                // 6) إخراج مع تطبيق صلاحيات الحقول على الـ DTO الناتج
                var outDto = _mapper.Map<ArchiveGroupDto>(entity);
                outDto = await _fieldPermissions.ApplyFieldPermissionsAsync(
                    outDto, UserId, RoleIds, "ArchiveGroup", JwtPerms);

                return Result<ArchiveGroupDto>.Success(outDto);
            }
            catch (Exception ex)
            {
                return Result<ArchiveGroupDto>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
            }
        }
        #endregion

        #region Update ArchiveGroup
        public async Task<Result<string>> UpdateAsync(long groupId, UpdateArchiveGroupDto dto)
        {
            try
            {
                var actorId = UserId ?? string.Empty;

                // منع السوبر أدمن
                var isSuperAdmin = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == actorId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .AnyAsync(n => n == "SUPERADMIN");
                if (isSuperAdmin)
                    return Result<string>.Failure(new[] { "غير مسموح للسوبر أدمن بتعديل مجموعة الأرشفة عبر هذا المسار." });

                var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
                if (actor is null)
                    return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var entity = await _context.ArchiveGroups.FirstOrDefaultAsync(g => g.ArchiveGroupId == groupId);
                if (entity == null) return Result<string>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });

                // حصر النطاق
                if (actor.DepartmentId.HasValue)
                {
                    // موظف قسم: لا يعدّل إلا مجموعات قسمه، وممنوع تغيير القسم
                    if (entity.DepartmentId != actor.DepartmentId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك تعديل مجموعة خارج قسمك." });

                    if (dto.DepartmentId.HasValue && dto.DepartmentId.Value != entity.DepartmentId)
                        return Result<string>.Failure(new[] { "غير مسموح لموظف القسم بنقل المجموعة لقسم آخر." });
                }
                else if (actor.BranchId.HasValue)
                {
                    // مدير فرع: يجب أن تكون المجموعة ضمن فرعه
                    var entityBranchId = await _context.Departments
                        .Where(d => d.DepartmentId == entity.DepartmentId)
                        .Select(d => d.BranchId)
                        .FirstOrDefaultAsync();

                    if (entityBranchId != actor.BranchId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك تعديل مجموعة خارج فرعك." });

                    // إن أراد تغيير القسم: يجب أن يكون القسم الهدف ضمن نفس الفرع
                    if (dto.DepartmentId.HasValue && dto.DepartmentId.Value != entity.DepartmentId)
                    {
                        var targetDept = await _context.Departments.AsNoTracking()
                            .FirstOrDefaultAsync(d => d.DepartmentId == dto.DepartmentId.Value);
                        if (targetDept is null)
                            return Result<string>.Failure(new[] { "القسم الهدف غير موجود." });

                        if (targetDept.BranchId != actor.BranchId.Value)
                            return Result<string>.Failure(new[] { "لا يمكنك نقل المجموعة لقسم خارج فرعك." });
                    }
                }
                else
                {
                    return Result<string>.Failure(new[] { "غير مسموح بتعديل مجموعة الأرشفة." });
                }

                // صلاحيات الحقول
                var fpErrors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, JwtPerms, "ArchiveGroup", _fieldPermissions, UserId, RoleIds);
                if (fpErrors.Any()) return Result<string>.Failure(fpErrors.ToArray());

                // تطبيق التعديلات: العنوان دائماً مسموح حسب صلاحيات الحقول
                if (!string.IsNullOrWhiteSpace(dto.Title))
                    entity.Title = dto.Title.Trim();

                // تغيير القسم (إن سُمح أعلاه)
                if (dto.DepartmentId.HasValue && dto.DepartmentId.Value != entity.DepartmentId)
                {
                    var newDeptId = dto.DepartmentId.Value;

                    // عند نقل المجموعة لقسم آخر: توليد رقم جديد داخل القسم الهدف
                    var newMax = await _context.ArchiveGroups
                        .Where(g => g.DepartmentId == newDeptId)
                        .Select(g => (long?)g.ArchiveGroupNumber)
                        .MaxAsync() ?? 0;

                    entity.DepartmentId = newDeptId;
                    entity.ArchiveGroupNumber = newMax + 1;
                }

                await _context.SaveChangesAsync();
                return Result<string>.Success("تم تحديث مجموعة الأرشفة بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء التحديث: {ex.Message}" });
            }
        }

        #endregion

        #region Delete ArchiveGroup
        public async Task<Result<string>> DeleteAsync(long groupId)
        {
            try
            {
                var actorId = UserId ?? string.Empty;

                var isSuperAdmin = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == actorId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .AnyAsync(n => n == "SUPERADMIN");
                if (isSuperAdmin)
                    return Result<string>.Failure(new[] { "غير مسموح للسوبر أدمن بحذف مجموعة الأرشفة عبر هذا المسار." });

                var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
                if (actor is null)
                    return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var entity = await _context.ArchiveGroups.FirstOrDefaultAsync(g => g.ArchiveGroupId == groupId);
                if (entity == null) return Result<string>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });

                // حصر النطاق
                if (actor.DepartmentId.HasValue)
                {
                    if (entity.DepartmentId != actor.DepartmentId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك حذف مجموعة خارج قسمك." });
                }
                else if (actor.BranchId.HasValue)
                {
                    var entityBranchId = await _context.Departments
                        .Where(d => d.DepartmentId == entity.DepartmentId)
                        .Select(d => d.BranchId)
                        .FirstOrDefaultAsync();

                    if (entityBranchId != actor.BranchId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك حذف مجموعة خارج فرعك." });
                }
                else
                {
                    return Result<string>.Failure(new[] { "غير مسموح بحذف مجموعة الأرشفة." });
                }

                // منع الحذف عند وجود وثائق
                var hasDocs = await _context.Documents.AnyAsync(d => d.ArchiveGroupId == groupId);
                if (hasDocs) return Result<string>.Failure(new[] { "لا يمكن الحذف لوجود وثائق مرتبطة بالمجموعة." });

                _context.ArchiveGroups.Remove(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم حذف مجموعة الأرشفة بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
            }
        }
        #endregion

        #region GetById ArchiveGroup
        public async Task<Result<ArchiveGroupDto>> GetByIdAsync(long groupId)
        {
            try
            {
                var dto = await _context.ArchiveGroups
                    .AsNoTracking()
                    .Where(g => g.ArchiveGroupId == groupId)
                    .ProjectTo<ArchiveGroupDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (dto == null)
                    return Result<ArchiveGroupDto>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });

                dto = await _fieldPermissions.ApplyFieldPermissionsAsync(
                    dto, UserId, RoleIds, "ArchiveGroup", JwtPerms);

                return Result<ArchiveGroupDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<ArchiveGroupDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region GetAllArchiveGroups Paged list (simple filter: title only)
        public async Task<Result<PaginatedList<ArchiveGroupDto>>> GetArchiveGroupsAsync(int pageNumber = 1, int pageSize = 10, string? title = null)
        {
            try
            {
                var actorId = UserId ?? string.Empty;

                // أدوار المنفذ (NormalizedName ليتماشى مع EF)
                var roleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == actorId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = roleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = roleNorms.Any(n => n == "BRANCHMANAGER");

                var actor = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == actorId) as User;

                if (actor is null)
                    return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                IQueryable<ArchiveGroup> query = _context.ArchiveGroups.AsNoTracking();

                // فلتر العنوان (مسموح للجميع بما أنه النطاق أدناه مقيّد)
                if (!string.IsNullOrWhiteSpace(title))
                    query = query.Where(g => g.Title.Contains(title));

                // نطاق الوصول
                if (isSuperAdmin)
                {
                    // يرى الكل - لا فلترة إضافية
                }
                else if (isBranchManager)
                {
                    if (!actor.BranchId.HasValue)
                        return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                    // مجموعات أقسام فرعو فقط
                    query = query.Join(_context.Departments,
                                       g => g.DepartmentId,
                                       d => d.DepartmentId,
                                       (g, d) => new { g, d.BranchId })
                                 .Where(x => x.BranchId == actor.BranchId.Value)
                                 .Select(x => x.g);
                }
                else
                {
                    // موظف قسم: قسمه فقط
                    if (!actor.DepartmentId.HasValue)
                        return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "لا يمكن تحديد قسم المستخدم الحالي." });

                    query = query.Where(g => g.DepartmentId == actor.DepartmentId.Value);
                }

                var projected = query
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.ArchiveGroupId)
                    .ProjectTo<ArchiveGroupDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<ArchiveGroupDto>.CreateAsync(projected, pageNumber, pageSize);

                var permitted = await _fieldPermissions.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), UserId, RoleIds, "ArchiveGroup", JwtPerms);

                var result = new PaginatedList<ArchiveGroupDto>(
                    permitted, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<ArchiveGroupDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Search (advanced + paged)
        public async Task<Result<PaginatedList<ArchiveGroupDto>>> SearchAsync(ArchiveGroupSearchCriteria c, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = UserId;

                // أدوار المستخدم
                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                // بيانات المستخدم
                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor == null)
                    return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                // ====== الاستعلام الأساسي (Join مع الأقسام حتى نقدر نفلتر بالفرع) ======
                var q = _context.ArchiveGroups.AsNoTracking()
                    .Join(_context.Departments.AsNoTracking(),
                          g => g.DepartmentId,
                          d => d.DepartmentId,
                          (g, d) => new { g, d.BranchId })
                    .AsQueryable();

                // ====== منطق الوصول ======
                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue)
                            return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                        q = q.Where(x => x.BranchId == actor.BranchId.Value);
                    }
                    else
                    {
                        if (!actor.DepartmentId.HasValue)
                            return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { "لا يمكن تحديد قسم المستخدم الحالي." });

                        q = q.Where(x => x.g.DepartmentId == actor.DepartmentId.Value);
                    }
                }

                // ====== الفلاتر ======
                if (c.DepartmentId.HasValue)
                    q = q.Where(x => x.g.DepartmentId == c.DepartmentId.Value);

                // ====== البحث ======
                if (!string.IsNullOrWhiteSpace(c.Title))
                    q = q.Where(x => x.g.Title.Contains(c.Title));

                // ====== بين تاريخين ======
                if (c.CreatedFrom.HasValue)
                    q = q.Where(x => x.g.CreatedAt >= c.CreatedFrom.Value);

                if (c.CreatedTo.HasValue)
                    q = q.Where(x => x.g.CreatedAt <= c.CreatedTo.Value);

                // ====== الترتيب ======
                q = (c.SortBy ?? "").ToLower() switch
                {
                    "title" => c.SortDescending ? q.OrderByDescending(x => x.g.Title) : q.OrderBy(x => x.g.Title),
                    "createdat" => c.SortDescending ? q.OrderByDescending(x => x.g.CreatedAt) : q.OrderBy(x => x.g.CreatedAt),
                    "departmentid" => c.SortDescending ? q.OrderByDescending(x => x.g.DepartmentId) : q.OrderBy(x => x.g.DepartmentId),
                    _ => c.SortDescending ? q.OrderByDescending(x => x.g.ArchiveGroupId) : q.OrderBy(x => x.g.ArchiveGroupId),
                };

                // ====== إسقاط + باجنيشن ======
                var projected = q.Select(x => x.g).ProjectTo<ArchiveGroupDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<ArchiveGroupDto>.CreateAsync(projected, pageNumber, pageSize);

                return Result<PaginatedList<ArchiveGroupDto>>.Success(paged);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<ArchiveGroupDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
            }
        }

        #endregion

        #region Get ArchiveGroups Dropdown
        public async Task<Result<IEnumerable<DropDownDto>>> GetArchiveGroupsDropdownAsync()
        {
            var userId = UserId;

            // 1) جلب المستخدم الحالي
            var actor = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (actor == null)
                return Result<IEnumerable<DropDownDto>>.Failure(new[] { "المستخدم غير موجود." });

            // 2) جلب الأدوار
            var actorRoles = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                .ToListAsync();

            bool isSuperAdmin = actorRoles.Any(r => r == "SUPERADMIN");
            bool isBranchManager = actorRoles.Any(r => r == "BRANCHMANAGER");

            IQueryable<ArchiveGroup> query = _context.ArchiveGroups.AsNoTracking();

            // 3) تصفية حسب الدور
            if (isSuperAdmin)
            {
                // لا شيء — يشوف الكل
            }
            else if (isBranchManager)
            {
                if (!actor.BranchId.HasValue)
                    return Result<IEnumerable<DropDownDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم." });

                long branchId = actor.BranchId.Value;

                // كل مجموعات أقسام فرعو
                query = query.Where(g =>
                    _context.Departments.Any(d =>
                        d.DepartmentId == g.DepartmentId &&
                        d.BranchId == branchId
                    )
                );
            }
            else
            {
                // موظف قسم → يشوف مجموعات قسمه فقط
                if (!actor.DepartmentId.HasValue)
                    return Result<IEnumerable<DropDownDto>>.Failure(new[] { "لا يمكن تحديد قسم المستخدم." });

                long deptId = actor.DepartmentId.Value;
                query = query.Where(g => g.DepartmentId == deptId);
            }

            // 4) تجهيز القائمة
            var data = await query
                .OrderBy(g => g.ArchiveGroupId)
                .Select(g => new DropDownDto
                {
                    Id = g.ArchiveGroupId,
                    Name = g.Title
                })
                .ToListAsync();

            return Result<IEnumerable<DropDownDto>>.Success(data);
        }
        #endregion

        #region Get ArchiveGroups By Department 
        public async Task<Result<IEnumerable<ArchiveGroupDto>>> GetArchiveGroupsByDepartmentAsync(long departmentId)
        {
            try
            {
                var userId = UserId ?? string.Empty;
                var roleIds = RoleIds;
                var jwt = JwtPerms;

                // تحقق من وجود القسم
                var dept = await _context.Departments
                    .AsNoTracking()
                    .Where(d => d.DepartmentId == departmentId)
                    .Select(d => new { d.DepartmentId, d.BranchId })
                    .FirstOrDefaultAsync();

                if (dept is null)
                    return Result<IEnumerable<ArchiveGroupDto>>.Failure(new[] { "القسم غير موجود." });

                // جلب المستخدم الحالي + أدواره
                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor is null)
                    return Result<IEnumerable<ArchiveGroupDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                // قيود الوصول
                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue || dept.BranchId != actor.BranchId.Value)
                            return Result<IEnumerable<ArchiveGroupDto>>.Failure(new[] { "غير مسموح بالوصول إلى مجموعات قسم خارج فرعك." });
                    }
                    else
                    {
                        // موظف قسم
                        if (!actor.DepartmentId.HasValue || actor.DepartmentId.Value != departmentId)
                            return Result<IEnumerable<ArchiveGroupDto>>.Failure(new[] { "غير مسموح بالوصول إلى مجموعات قسم لا يتبع قسمك." });
                    }
                }

                // الاستعلام
                var groups = await _context.ArchiveGroups.AsNoTracking()
                    .Where(g => g.DepartmentId == departmentId)
                    .OrderBy(g => g.ArchiveGroupNumber) // أو ArchiveGroupId
                    .ProjectTo<ArchiveGroupDto>(_mapper.ConfigurationProvider)
                    .ToListAsync();

                // تطبيق صلاحيات الحقول
                var permitted = await _fieldPermissions.ApplyFieldPermissionsAsync(
                    groups, userId, roleIds, "ArchiveGroup", jwt);

                return Result<IEnumerable<ArchiveGroupDto>>.Success(permitted);
            }
            catch (Exception ex)
            {
                return Result<IEnumerable<ArchiveGroupDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }

        #endregion

        #region Helper للكشف عن تعارض الفهرس الفريد
        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            if (ex.InnerException is SqlException sqlEx)
                return sqlEx.Number == 2601 || sqlEx.Number == 2627; // duplicate key / unique constraint
            return false;
        }
        #endregion
    }
}
