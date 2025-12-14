using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentTypeDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services
{
    public class DocumentTypeService : IDocumentTypeService
    {
        private readonly MainDbContext _context;
        private readonly FieldPermissionHelper _fieldPermissionHelper;
        private readonly IUserContextService _userContext;
        private readonly IMapper _mapper;

        public DocumentTypeService(MainDbContext context,FieldPermissionHelper fieldPermissionHelper,IUserContextService userContext,IMapper mapper)
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

        #region Create DocumentType
        public async Task<Result<DocumentTypeDto>> CreateAsync(CreateDocumentTypeDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "DocumentType", _fieldPermissionHelper, userId, roleIds);
                if (errors.Any()) return Result<DocumentTypeDto>.Failure(errors.ToArray());

                // فريدان الاسم
                var name = (dto.DocumentTypeName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(name) &&
                    await _context.DocumentTypes.AnyAsync(t => t.DocumentTypeName == name))
                    return Result<DocumentTypeDto>.Failure(new[] { "اسم نوع الوثيقة مستخدم مسبقًا." });

                var entity = _mapper.Map<DocumentType>(dto);
                _context.DocumentTypes.Add(entity);
                await _context.SaveChangesAsync();

                var outDto = _mapper.Map<DocumentTypeDto>(entity);
                outDto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    outDto, userId, roleIds, "DocumentType", jwtFieldPermissions);

                return Result<DocumentTypeDto>.Success(outDto);
            }
            catch (Exception ex)
            {
                return Result<DocumentTypeDto>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
            }
        }
        #endregion

        #region Update document type
        public async Task<Result<string>> UpdateAsync(long documentTypeId, UpdateDocumentTypeDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var entity = await _context.DocumentTypes.FindAsync(documentTypeId);
                if (entity == null) return Result<string>.Failure(new[] { "نوع الوثيقة غير موجود." });

                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwtFieldPermissions, "DocumentType", _fieldPermissionHelper, userId, roleIds);
                if (errors.Any()) return Result<string>.Failure(errors.ToArray());

                // فريدان الاسم عند التحديث
                var targetName = string.IsNullOrWhiteSpace(dto.DocumentTypeName) ? entity.DocumentTypeName : dto.DocumentTypeName.Trim();
                var exists = await _context.DocumentTypes
                    .AnyAsync(t => t.DocumentTypeId != documentTypeId && t.DocumentTypeName == targetName);
                if (exists) return Result<string>.Failure(new[] { "اسم نوع الوثيقة مستخدم مسبقًا." });

                _mapper.Map(dto, entity);

                _context.DocumentTypes.Update(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم تحديث نوع الوثيقة بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء التحديث: {ex.Message}" });
            }
        }
        #endregion

        #region Delete document type
        public async Task<Result<string>> DeleteAsync(long documentTypeId)
        {
            try
            {
                var entity = await _context.DocumentTypes.FindAsync(documentTypeId);
                if (entity == null) return Result<string>.Failure(new[] { "نوع الوثيقة غير موجود." });

                // التحقق من وجود وثائق مرتبطة
                var inUse = await _context.Documents.AnyAsync(d => d.DocumentTypeId == documentTypeId);
                if (inUse) return Result<string>.Failure(new[] { "لا يمكن حذف النوع لوجود وثائق مرتبطة به." });

                _context.DocumentTypes.Remove(entity);
                await _context.SaveChangesAsync();

                return Result<string>.Success("تم حذف نوع الوثيقة بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
            }
        }
        #endregion

        #region GetById document type
        public async Task<Result<DocumentTypeDto>> GetByIdAsync(long documentTypeId)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwtFieldPermissions = JwtFieldPerms;

                var dto = await _context.DocumentTypes
                    .AsNoTracking()
                    .Where(t => t.DocumentTypeId == documentTypeId)
                    .ProjectTo<DocumentTypeDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (dto == null) return Result<DocumentTypeDto>.Failure(new[] { "نوع الوثيقة غير موجود." });

                dto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    dto, userId, roleIds, "DocumentType", jwtFieldPermissions);

                return Result<DocumentTypeDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<DocumentTypeDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Dropdown list
        public async Task<Result<IEnumerable<DropDownDto>>> GetDocumentTypesDropdownAsync()
        {
            var data = await _context.DocumentTypes
                .AsNoTracking()
                .OrderBy(t => t.DocumentTypeId)
                .Select(t => new DropDownDto
                {
                    Id = t.DocumentTypeId,
                    Name = t.DocumentTypeName
                })
                .ToListAsync();

            return Result<IEnumerable<DropDownDto>>.Success(data);
        }
        #endregion

        #region GetAllDocumentTypes Paged list (simple filter)
        public async Task<Result<PaginatedList<DocumentTypeDto>>> GetDocumentTypesAsync(int pageNumber = 1, int pageSize = 10,string? name = null)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var query = _context.DocumentTypes.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(name))
                    query = query.Where(t => t.DocumentTypeName.Contains(name));

                var projected = query
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.DocumentTypeId)
                    .ProjectTo<DocumentTypeDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentTypeDto>.CreateAsync(projected, pageNumber, pageSize);

                var permitted = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "DocumentType", jwt);

                var result = new PaginatedList<DocumentTypeDto>(
                    permitted, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DocumentTypeDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentTypeDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Search (advanced + paged)
        public async Task<Result<PaginatedList<DocumentTypeDto>>> SearchAsync(
            DocumentTypeSearchCriteria criteria, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var query = _context.DocumentTypes.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(criteria.Name))
                    query = query.Where(t => t.DocumentTypeName.Contains(criteria.Name));

                if (!string.IsNullOrWhiteSpace(criteria.Extensions))
                    query = query.Where(t => t.AllowedExtensions.Contains(criteria.Extensions));

                query = criteria.SortBy?.ToLower() switch
                {
                    "name" => criteria.SortDescending ? query.OrderByDescending(t => t.DocumentTypeName) : query.OrderBy(t => t.DocumentTypeName),
                    "createdat" => criteria.SortDescending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
                    _ => query.OrderBy(t => t.DocumentTypeId)
                };

                var projected = query.ProjectTo<DocumentTypeDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentTypeDto>.CreateAsync(projected, pageNumber, pageSize);

                var permitted = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "DocumentType", jwt);

                var result = new PaginatedList<DocumentTypeDto>(
                    permitted, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DocumentTypeDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentTypeDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
            }
        }
        #endregion

    }
}
