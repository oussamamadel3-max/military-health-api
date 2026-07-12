using Application.Common.Results;
using Application.Common.Utilities;
using Application.Dtos;
using Application.Dtos.DocumentDtos;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using AutoMapper;
using Domain.Entities;
using Application.Common.Permissions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documents;
    private readonly IAttachmentRepository _attachments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActorScopeProvider _actorScope;
    private readonly FieldPermissionHelper _fieldPerms;
    private readonly IUserContextService _userCtx;
    private readonly IMapper _mapper;
    private readonly IAttachmentService _attachmentService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITextInputSanitizer _textSanitizer;

    public DocumentService(
        IDocumentRepository documents,
        IAttachmentRepository attachments,
        IUnitOfWork unitOfWork,
        IActorScopeProvider actorScope,
        FieldPermissionHelper fieldPerms,
        IUserContextService userCtx,
        IMapper mapper,
        IAttachmentService attachmentService,
        IHttpContextAccessor httpContextAccessor,
        ITextInputSanitizer textSanitizer)
    {
        _documents = documents;
        _attachments = attachments;
        _unitOfWork = unitOfWork;
        _actorScope = actorScope;
        _fieldPerms = fieldPerms;
        _userCtx = userCtx;
        _mapper = mapper;
        _attachmentService = attachmentService;
        _httpContextAccessor = httpContextAccessor;
        _textSanitizer = textSanitizer;
    }

    private string CurrentUserId => _userCtx.UserId ?? string.Empty;
    private List<string> CurrentRoleIds => _userCtx.RoleIds?.ToList() ?? new List<string>();
    private IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> JwtFieldPerms =>
        _userCtx.FieldPermissions ?? new Dictionary<string, Domain.Enums.FieldAccessType>();

    private static bool IsWithinScope(ActorScope scope, Document document)
    {
        if (scope.IsSuperAdmin || scope.IsBranchManager) return true;
        return document.UserId == scope.UserId;
    }

    public async Task<Result<List<DocumentDto>>> CreateAsync(CreateDocumentManyDto dto)
    {
        try
        {
            var userId = CurrentUserId;
            var roleIds = CurrentRoleIds;
            var jwt = JwtFieldPerms;

            var scope = await _actorScope.GetScopeAsync(userId);
            if (scope.Actor is null)
                return Result<List<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var fpErrors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                dto, jwt, "Document", _fieldPerms, userId, roleIds);
            if (fpErrors.Any())
                return Result<List<DocumentDto>>.Failure(fpErrors.ToArray());

            var currentMax = await _documents.GetMaxDocumentNumberAsync();

            var entity = new Document
            {
                Title = _textSanitizer.SanitizePlainText(dto.Title) ?? string.Empty,
                Description = _textSanitizer.SanitizePlainText(dto.Description) ?? string.Empty,
                UserId = userId,
                DocumentNumber = currentMax + 1
            };
            entity.Description ??= string.Empty;
            _documents.Add(entity);

            const int maxRetries = 5;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await _unitOfWork.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
                {
                    if (attempt == maxRetries)
                        return Result<List<DocumentDto>>.Failure(new[] { "تعذر توليد رقم وثيقة فريد بعد عدة محاولات. حاول مجددًا." });

                    var newMax = await _documents.GetMaxDocumentNumberAsync();
                    entity.DocumentNumber = newMax + 1;
                }
            }

            if (dto.AttachmentIds != null && dto.AttachmentIds.Count > 0)
            {
                foreach (var attId in dto.AttachmentIds.Distinct())
                    await _attachmentService.LinkToEntityAsync(attId, entity.DocumentId, "Document");
            }

            var outDto = _mapper.Map<DocumentDto>(entity);
            outDto = await _fieldPerms.ApplyFieldPermissionsAsync(outDto, userId, roleIds, "Document", jwt);
            return Result<List<DocumentDto>>.Success(new List<DocumentDto> { outDto });
        }
        catch (Exception ex)
        {
            return Result<List<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
        }
    }

    public async Task<Result<string>> UpdateAsync(long documentId, UpdateDocumentDto dto)
    {
        try
        {
            var userId = CurrentUserId ?? string.Empty;
            var roleIds = CurrentRoleIds;
            var jwt = JwtFieldPerms;

            var entity = await _documents.GetByIdAsync(documentId, trackChanges: true);
            if (entity == null) return Result<string>.Failure(new[] { "الوثيقة غير موجودة." });

            var scope = await _actorScope.GetScopeAsync(userId);
            if (scope.Actor is null) return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

            if (!IsWithinScope(scope, entity))
                return Result<string>.Failure(new[] { "لا يمكنك تعديل وثيقة لم تقم بإنشائها." });

            var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                dto, jwt, "Document", _fieldPerms, userId, roleIds);
            if (errors.Any()) return Result<string>.Failure(errors.ToArray());

            _mapper.Map(dto, entity);
            entity.Title = _textSanitizer.SanitizePlainText(entity.Title) ?? string.Empty;
            entity.Description = _textSanitizer.SanitizePlainText(entity.Description) ?? string.Empty;

            using var tx = await _unitOfWork.BeginTransactionAsync();
            try
            {
                _documents.Update(entity);

                const int maxRetries = 5;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try { await _unitOfWork.SaveChangesAsync(); break; }
                    catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
                    {
                        if (attempt == maxRetries)
                            return Result<string>.Failure(new[] { "تعذر حفظ التغييرات بسبب تعارض الترقيم بعد عدة محاولات." });

                        var retryMax = await _documents.GetMaxDocumentNumberAsync();
                        entity.DocumentNumber = retryMax + 1;
                    }
                }

                var currentIds = await _attachments.GetIdsByDocumentAsync(entity.DocumentId);
                var currentSet = currentIds.ToHashSet();
                HashSet<Guid> targetSet = new(currentSet);

                if (dto.ReplaceAttachments == true && dto.AttachmentIds is { Count: > 0 })
                    targetSet = dto.AttachmentIds.Distinct().ToHashSet();
                else
                {
                    if (dto.AddAttachmentIds is { Count: > 0 })
                        foreach (var id in dto.AddAttachmentIds.Distinct()) targetSet.Add(id);
                    if (dto.RemoveAttachmentIds is { Count: > 0 })
                        foreach (var id in dto.RemoveAttachmentIds.Distinct()) targetSet.Remove(id);
                }

                var toUnlink = currentSet.Except(targetSet).ToList();
                var toLink = targetSet.Except(currentSet).ToList();

                if (toLink.Count > 0)
                {
                    var candidates = await _attachments.GetByIdsAsync(toLink);
                    var found = candidates.Select(a => a.AttachmentId).ToHashSet();
                    var missing = toLink.Where(id => !found.Contains(id)).ToList();
                    if (missing.Count > 0)
                        return Result<string>.Failure(new[] { $"مرفقات غير موجودة: [{string.Join(", ", missing)}]" });

                    var locked = candidates
                        .Where(a => a.OwnerId != 0 && a.OwnerId != entity.DocumentId)
                        .Select(a => a.AttachmentId).ToList();
                    if (locked.Count > 0)
                        return Result<string>.Failure(new[] { $"مرفقات مرتبطة بكيان آخر: [{string.Join(", ", locked)}]" });

                    foreach (var a in candidates)
                    {
                        a.OwnerId = entity.DocumentId;
                        a.OwnerType = "Document";
                        a.UpdatedAt = DateTime.UtcNow;
                        a.UpdatedBy = userId;
                    }
                    _attachments.UpdateRange(candidates);
                }

                if (toUnlink.Count > 0)
                {
                    var willUnlink = (await _attachments.GetByIdsAsync(toUnlink))
                        .Where(a => a.OwnerType == "Document" && a.OwnerId == entity.DocumentId).ToList();

                    foreach (var a in willUnlink)
                    {
                        a.OwnerId = 0;
                        a.OwnerType = string.Empty;
                        a.IsProfilePhoto = false;
                        a.UpdatedAt = DateTime.UtcNow;
                        a.UpdatedBy = userId;
                    }
                    _attachments.UpdateRange(willUnlink);
                }

                if (toLink.Count > 0 || toUnlink.Count > 0)
                    await _unitOfWork.SaveChangesAsync();

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            return Result<string>.Success("تم تحديث الوثيقة والمرفقات بنجاح.");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(new[] { $"حدث خطأ أثناء التعديل: {ex.Message}" });
        }
    }

    public async Task<Result<string>> DeleteAsync(long documentId)
    {
        try
        {
            var userId = CurrentUserId;
            var scope = await _actorScope.GetScopeAsync(userId);

            var entity = await _documents.GetByIdAsync(documentId, trackChanges: true);
            if (entity == null)
                return Result<string>.Failure(new[] { "الوثيقة غير موجودة." });

            if (!IsWithinScope(scope, entity))
                return Result<string>.Failure(new[] { "لا يمكنك حذف وثيقة لم تقم بإنشائها." });

            _documents.Remove(entity);
            await _unitOfWork.SaveChangesAsync();
            return Result<string>.Success("تم حذف الوثيقة بنجاح.");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
        }
    }

    public async Task<Result<DocumentDto>> GetByIdAsync(long documentId)
    {
        try
        {
            var userId = CurrentUserId;
            var roleIds = CurrentRoleIds;
            var jwt = JwtFieldPerms;

            var scope = await _actorScope.GetScopeAsync(userId);
            if (scope.Actor is null)
                return Result<DocumentDto>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var entity = await _documents.GetByIdAsync(documentId);
            if (entity == null || !IsWithinScope(scope, entity))
                return Result<DocumentDto>.Failure(new[] { "الوثيقة غير موجودة أو خارج نطاق وصولك." });

            var dto = _mapper.Map<DocumentDto>(entity);
            dto = await _fieldPerms.ApplyFieldPermissionsAsync(dto, userId, roleIds, "Document", jwt);

            var attachmentsEntities = (await _attachments.GetByOwnerAsync(documentId, new[] { "Document" }))
                .OrderByDescending(a => a.UploadedAt).ToList();

            dto.Attachments = attachmentsEntities.Select(a => new AttachmentLinkDto
            {
                Id = a.AttachmentId,
                FileUrl = BuildProtectedDownloadUrl(a.AttachmentId),
                FileName = a.FileName ?? $"{a.AttachmentId}"
            }).ToList();

            return Result<DocumentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<DocumentDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
        }
    }

    public async Task<Result<PaginatedList<DocumentDto>>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, string? title = null)
    {
        try
        {
            var userId = CurrentUserId;
            var roleIds = CurrentRoleIds;
            var jwt = JwtFieldPerms;

            var scope = await _actorScope.GetScopeAsync(userId);
            if (scope.Actor is null)
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var paged = await _documents.GetPagedAsync(scope, pageNumber, pageSize, title);
            return await BuildDocumentPageResultAsync(paged, userId, roleIds, jwt, pageSize);
        }
        catch (Exception ex)
        {
            return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
        }
    }

    public async Task<Result<IEnumerable<DropDownDto>>> GetDocumentsDropdownAsync()
    {
        var data = await _documents.GetDropdownAsync();
        return Result<IEnumerable<DropDownDto>>.Success(data);
    }

    public async Task<Result<PaginatedList<DocumentDto>>> SearchAsync(DocumentSearchCriteria c, int pageNumber = 1, int pageSize = 10)
    {
        try
        {
            var userId = CurrentUserId;
            var roleIds = CurrentRoleIds;
            var jwt = JwtFieldPerms;

            var scope = await _actorScope.GetScopeAsync(userId);
            if (scope.Actor is null)
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

            var paged = await _documents.SearchAsync(c, scope, pageNumber, pageSize);
            return await BuildDocumentPageResultAsync(paged, userId, roleIds, jwt, pageSize, includeAttachments: false);
        }
        catch (Exception ex)
        {
            return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
        }
    }

    private async Task<Result<PaginatedList<DocumentDto>>> BuildDocumentPageResultAsync(
        PaginatedList<Document> paged,
        string userId,
        List<string> roleIds,
        IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> jwt,
        int pageSize,
        bool includeAttachments = true)
    {
        var dtos = _mapper.Map<List<DocumentDto>>(paged.Items.ToList());
        var permitted = await _fieldPerms.ApplyFieldPermissionsAsync(dtos, userId, roleIds, "Document", jwt);

        if (includeAttachments)
        {
            var docIds = permitted.Select(d => d.DocumentId).ToList();
            if (docIds.Count > 0)
            {
                var atts = await GetAttachmentsForDocumentsAsync(docIds);
                var map = atts.GroupBy(x => x.OwnerId).ToDictionary(
                    g => g.Key,
                    g => g.Select(x => new AttachmentLinkDto
                    {
                        Id = x.AttachmentId,
                        FileUrl = BuildProtectedDownloadUrl(x.AttachmentId),
                        FileName = x.FileName ?? $"{x.AttachmentId}"
                    }).ToList());

                foreach (var d in permitted)
                    d.Attachments = map.TryGetValue(d.DocumentId, out var list) ? list : new List<AttachmentLinkDto>();
            }
        }

        var result = new PaginatedList<DocumentDto>(permitted, paged.TotalCount, paged.PageNumber, pageSize);
        return Result<PaginatedList<DocumentDto>>.Success(result);
    }

    private async Task<List<Attachment>> GetAttachmentsForDocumentsAsync(IReadOnlyList<long> documentIds)
    {
        var result = new List<Attachment>();
        foreach (var docId in documentIds)
            result.AddRange(await _attachments.GetByOwnerAsync(docId, new[] { "Document" }));
        return result.OrderByDescending(a => a.UploadedAt).ToList();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx)
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        return false;
    }

    private string BuildProtectedDownloadUrl(Guid id)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
            return $"/api/attachments/download/{id}";
        return $"{request.Scheme}://{request.Host}/api/attachments/download/{id}";
    }
}
