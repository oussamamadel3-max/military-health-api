using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.BranchDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services
{
    public class BranchService : IBranchService
    {
        private readonly MainDbContext _context;
        private readonly FieldPermissionHelper _fieldPermissionHelper;
        private readonly IUserContextService _userContext;
        private readonly IMapper _mapper;

        public BranchService(MainDbContext context,FieldPermissionHelper fieldPermissionHelper,IUserContextService userContext,IMapper mapper)
        {
            _context = context;
            _fieldPermissionHelper = fieldPermissionHelper;
            _userContext = userContext;
            _mapper = mapper;
        }

        private string CurrentUserId => _userContext.UserId ?? string.Empty;
        private List<string> CurrentRoleIds => _userContext.RoleIds?.ToList() ?? new List<string>();
        private IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> JwtFieldPerms =>
            _userContext.FieldPermissions ?? new Dictionary<string, Domain.Enums.FieldAccessType>();

        #region Create branch
        public async Task<Result<BranchDto>> CreateAsync(CreateBranchDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                // صلاحيات الحقول
                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Branch", _fieldPermissionHelper, userId, roleIds);
                if (errors.Any())
                    return Result<BranchDto>.Failure(errors.ToArray());

                // فريدان الكود
                var code = (dto.Code ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(code) &&
                    await _context.Branchs.AnyAsync(b => b.Code == code))
                    return Result<BranchDto>.Failure(new[] { "رمز الفرع مستخدم مسبقًا." });

                // إنشاء بالـ Mapper
                var entity = _mapper.Map<Branch>(dto);

                entity.Description ??= string.Empty;

                _context.Branchs.Add(entity);
                await _context.SaveChangesAsync();

                // خروج بالـ Mapper + تطبيق صلاحيات القراءة
                var outDto = _mapper.Map<BranchDto>(entity);
                outDto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    outDto, userId, roleIds, "Branch", jwtFieldPermissions);

                return Result<BranchDto>.Success(outDto);
            }
            catch (Exception ex)
            {
                return Result<BranchDto>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
            }
        }
        #endregion

        #region Update branch
        public async Task<Result<string>> UpdateAsync(long branchId, UpdateBranchDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var entity = await _context.Branchs.FindAsync(branchId);
                if (entity == null)
                    return Result<string>.Failure(new[] { "الفرع غير موجود." });

                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "Branch", _fieldPermissionHelper, userId, roleIds);
                if (errors.Any())
                    return Result<string>.Failure(errors.ToArray());

                if (!string.IsNullOrWhiteSpace(dto.Code) && dto.Code != entity.Code)
                {
                    var exists = await _context.Branchs
                        .AnyAsync(b => b.Code == dto.Code && b.BranchId != branchId);
                    if (exists)
                        return Result<string>.Failure(new[] { "رمز الفرع مستخدم مسبقًا." });
                }

                // التحديث بالـ Mapper (onto existing entity)
                _mapper.Map(dto, entity);

                entity.Description ??= string.Empty;

                _context.Branchs.Update(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم تحديث بيانات الفرع بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء التحديث: {ex.Message}" });
            }
        }
        #endregion

        #region Delete branch
        public async Task<Result<string>> DeleteAsync(long branchId)
        {
            try
            {
                var entity = await _context.Branchs.FindAsync(branchId);
                if (entity == null)
                    return Result<string>.Failure(new[] { "الفرع غير موجود." });

                var hasDeps = await _context.Departments.AnyAsync(d => d.BranchId == branchId);
                if (hasDeps)
                    return Result<string>.Failure(new[] { "لا يمكن حذف الفرع لوجود أقسام مرتبطة به." });

                _context.Branchs.Remove(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم حذف الفرع بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
            }
        }
        #endregion

        #region GetById branch
        public async Task<Result<BranchDto>> GetByIdAsync(long branchId)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                // ProjectTo لتحويل مباشر إلى DTO عبر AutoMapper
                var dto = await _context.Branchs
                    .AsNoTracking()
                    .Where(b => b.BranchId == branchId)
                    .ProjectTo<BranchDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (dto == null)
                    return Result<BranchDto>.Failure(new[] { "الفرع غير موجود." });

                dto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    dto, userId, roleIds, "Branch", jwtFieldPermissions);

                return Result<BranchDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<BranchDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region GetAllBranches Paged list
        public async Task<Result<PaginatedList<BranchDto>>> GetBranchesAsync(
            int pageNumber = 1, int pageSize = 20, string? code = null, string? name = null)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var query = _context.Branchs.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(code))
                    query = query.Where(b => b.Code.Contains(code));

                if (!string.IsNullOrWhiteSpace(name))
                    query = query.Where(b => b.BranchName.Contains(name));

                // ProjectTo قبل التصفح
                var projected = query
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.BranchId)
                    .ProjectTo<BranchDto>(_mapper.ConfigurationProvider);

                // تطبيق التصفح
                var paged = await PaginatedList<BranchDto>.CreateAsync(projected, pageNumber, pageSize);

                // تطبيق صلاحيات القراءة على العناصر داخل الصفحة فقط
                var permittedItems = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Branch", jwt);

                // رجّع نفس الشكل PaginatedList لكن بالعناصر بعد التصفية
                var result = new PaginatedList<BranchDto>(
                    permittedItems,
                    count: paged.TotalCount,
                    pageNumber: paged.PageNumber,
                    pageSize: pageSize
                );

                return Result<PaginatedList<BranchDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<BranchDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Dropdown list
        public async Task<Result<IEnumerable<DropDownDto>>> GetBranchesAsync()
        {
            var data = await _context.Branchs
                .AsNoTracking()
                .OrderBy(b => b.BranchId)
                .Select(b => new DropDownDto
                {
                    Id = b.BranchId,
                    Name = b.BranchName
                })
                .ToListAsync();

            return Result<IEnumerable<DropDownDto>>.Success(data);
        }
        #endregion

        #region Search (advanced)
        public async Task<Result<PaginatedList<BranchDto>>> SearchAsync(BranchSearchCriteria criteria, int pageNumber = 1, int pageSize = 20)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var query = _context.Branchs.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(criteria.Code))
                    query = query.Where(b => b.Code.Contains(criteria.Code));

                if (!string.IsNullOrWhiteSpace(criteria.BranchName))
                    query = query.Where(b => b.BranchName.Contains(criteria.BranchName));

                if (!string.IsNullOrWhiteSpace(criteria.Description))
                    query = query.Where(b => b.Description.Contains(criteria.Description));

                query = criteria.SortBy?.ToLower() switch
                {
                    "code" => criteria.SortDescending ? query.OrderByDescending(b => b.Code) : query.OrderBy(b => b.Code),
                    "branchname" => criteria.SortDescending ? query.OrderByDescending(b => b.BranchName) : query.OrderBy(b => b.BranchName),
                    "createdat" => criteria.SortDescending ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
                    _ => query.OrderBy(b => b.BranchId)
                };

                // ProjectTo قبل التصفح لخفض الحمل على الذاكرة
                var projected = query.ProjectTo<BranchDto>(_mapper.ConfigurationProvider);

                // الصفحات
                var paged = await PaginatedList<BranchDto>.CreateAsync(projected, pageNumber, pageSize);

                // تطبيق صلاحيات الحقول على عناصر الصفحة فقط
                var permittedItems = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Branch", jwt);

                // إعادة تغليف النتيجة بنفس شكل PaginatedList
                var result = new PaginatedList<BranchDto>(
                    permittedItems,
                    count: paged.TotalCount,
                    pageNumber: paged.PageNumber,
                    pageSize: pageSize
                );

                return Result<PaginatedList<BranchDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<BranchDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
            }
        }

        #endregion
    }
}
