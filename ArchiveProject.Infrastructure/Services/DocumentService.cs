using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.AttachmentInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.Infrastructure.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly MainDbContext _context;
        private readonly FieldPermissionHelper _fieldPerms;
        private readonly IUserContextService _userCtx;
        private readonly IMapper _mapper;
        private readonly IAttachmentService _attachments;
        private readonly IHttpContextAccessor _httpContextAccessor;


        public DocumentService(MainDbContext context,FieldPermissionHelper fieldPerms,IUserContextService userCtx,IMapper mapper,IAttachmentService attachments, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _fieldPerms = fieldPerms;
            _userCtx = userCtx;
            _mapper = mapper;
            _attachments = attachments;
            _httpContextAccessor = httpContextAccessor;
        }

        private string CurrentUserId => _userCtx.UserId ?? string.Empty;
        private List<string> CurrentRoleIds => _userCtx.RoleIds?.ToList() ?? new List<string>();
        private IReadOnlyDictionary<string, Domain.Enums.FieldAccessType> JwtFieldPerms =>
            _userCtx.FieldPermissions ?? new Dictionary<string, Domain.Enums.FieldAccessType>();



        #region Create Document (single document + link AttachmentIds)
        public async Task<Result<List<DocumentDto>>> CreateAsync(CreateDocumentManyDto dto)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                // الممثل الحالي
                var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) as User;
                if (actor is null)
                    return Result<List<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                // أدوار
                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                // تحديد DepartmentId الفعلي بحسب الدور
                long effectiveDepartmentId;
                if (actor.DepartmentId.HasValue && !isBranchManager && !isSuperAdmin)
                {
                    effectiveDepartmentId = actor.DepartmentId.Value; // موظف قسم
                    dto.DepartmentId = effectiveDepartmentId;
                }
                else if (isBranchManager)
                {
                    if (!actor.BranchId.HasValue)
                        return Result<List<DocumentDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                    var dept = await _context.Departments.AsNoTracking()
                                 .FirstOrDefaultAsync(d => d.DepartmentId == dto.DepartmentId);
                    if (dept is null)
                        return Result<List<DocumentDto>>.Failure(new[] { "القسم غير موجود." });

                    if (dept.BranchId != actor.BranchId.Value)
                        return Result<List<DocumentDto>>.Failure(new[] { "لا يمكنك إنشاء وثائق لقسم خارج فرعك." });

                    effectiveDepartmentId = dept.DepartmentId;
                }
                else
                {
                    if (dto.DepartmentId <= 0)
                        return Result<List<DocumentDto>>.Failure(new[] { "القسم مطلوب." });

                    var depExists0 = await _context.Departments.AnyAsync(d => d.DepartmentId == dto.DepartmentId);
                    if (!depExists0)
                        return Result<List<DocumentDto>>.Failure(new[] { "القسم غير موجود." });

                    effectiveDepartmentId = dto.DepartmentId;
                }

                // صلاحيات الحقول
                var fpErrors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwt, "Document", _fieldPerms, userId, roleIds);
                if (fpErrors.Any())
                    return Result<List<DocumentDto>>.Failure(fpErrors.ToArray());

                // تحقق DocumentType
                if (!dto.DocumentTypeId.HasValue)
                    return Result<List<DocumentDto>>.Failure(new[] { "نوع الوثيقة مطلوب." });

                var docType = await _context.DocumentTypes.FindAsync(dto.DocumentTypeId.Value);
                if (docType == null)
                    return Result<List<DocumentDto>>.Failure(new[] { "نوع الوثيقة غير موجود." });

                // تحقق ArchiveGroup (اختياري) ويجب أن يتبع نفس القسم
                if (dto.ArchiveGroupId.HasValue)
                {
                    var groupDeptId = await _context.ArchiveGroups
                        .Where(g => g.ArchiveGroupId == dto.ArchiveGroupId.Value)
                        .Select(g => (long?)g.DepartmentId)
                        .FirstOrDefaultAsync();
                    if (!groupDeptId.HasValue)
                        return Result<List<DocumentDto>>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });
                    if (groupDeptId.Value != effectiveDepartmentId)
                        return Result<List<DocumentDto>>.Failure(new[] { "مجموعة الأرشفة لا تتبع نفس القسم المحدد." });
                }

                // احسب رقم الوثيقة التالي داخل القسم
                var currentMax = await _context.Documents
                    .Where(d => d.DepartmentId == effectiveDepartmentId)
                    .Select(d => (long?)d.DocumentNumber)
                    .MaxAsync() ?? 0;

                var entity = new Document
                {
                    DepartmentId = effectiveDepartmentId,
                    DocumentTypeId = dto.DocumentTypeId,
                    ArchiveGroupId = dto.ArchiveGroupId,
                    Title = dto.Title,
                    Description = dto.Description ?? string.Empty,
                    UserId = userId,
                    DocumentNumber = currentMax + 1
                };
                entity.Description ??= string.Empty;
                _context.Documents.Add(entity);

                // حفظ مع معالجة تعارض ترقيم داخل القسم
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
                            return Result<List<DocumentDto>>.Failure(new[] { "تعذر توليد رقم وثيقة فريد بعد عدة محاولات. حاول مجددًا." });

                        var newMax = await _context.Documents
                            .Where(d => d.DepartmentId == entity.DepartmentId)
                            .Select(d => (long?)d.DocumentNumber)
                            .MaxAsync() ?? 0;

                        entity.DocumentNumber = newMax + 1;
                    }
                }

                // ربط المرفقات المرسلة (إن وُجدت)
                if (dto.AttachmentIds != null && dto.AttachmentIds.Count > 0)
                {
                    foreach (var attId in dto.AttachmentIds.Distinct())
                    {
                        // نستخدم خدمة المرفقات القياسية
                        await _attachments.LinkToEntityAsync(attId, entity.DocumentId, "Document");
                    }
                }

                // إخراج
                var outDto = _mapper.Map<DocumentDto>(entity);
                outDto = await _fieldPerms.ApplyFieldPermissionsAsync(outDto, userId, roleIds, "Document", jwt);

                // نحافظ على توقيع الواجهة: قائمة (عنصر واحد)
                return Result<List<DocumentDto>>.Success(new List<DocumentDto> { outDto });
            }
            catch (Exception ex)
            {
                return Result<List<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الإنشاء: {ex.Message}" });
            }
        }
        #endregion

        #region Update Document (link new AttachmentIds if provided)
        //public async Task<Result<string>> UpdateAsync(long documentId, UpdateDocumentDto dto)
        //{
        //    try
        //    {
        //        var userId = CurrentUserId;
        //        var roleIds = CurrentRoleIds;
        //        var jwt = JwtFieldPerms;

        //        var entity = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        //        if (entity == null)
        //            return Result<string>.Failure(new[] { "الوثيقة غير موجودة." });

        //        // الممثل + الأدوار
        //        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) as User;
        //        if (actor is null)
        //            return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

        //        var actorRoleNorms = await _context.UserRoleAssociations
        //            .Where(ura => ura.UserId == userId)
        //            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
        //            .ToListAsync();

        //        bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
        //        bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

        //        // تحديد القسم النهائي
        //        var originalDepartmentId = entity.DepartmentId;
        //        long requestedDepartmentId = dto.DepartmentId ?? entity.DepartmentId;
        //        long finalDepartmentId;

        //        if (!isSuperAdmin && !isBranchManager && actor.DepartmentId.HasValue)
        //        {
        //            if (entity.DepartmentId != actor.DepartmentId.Value)
        //                return Result<string>.Failure(new[] { "لا يمكنك تعديل وثيقة خارج قسمك." });

        //            finalDepartmentId = actor.DepartmentId.Value;
        //            dto.DepartmentId = finalDepartmentId;
        //        }
        //        else if (isBranchManager)
        //        {
        //            if (!actor.BranchId.HasValue)
        //                return Result<string>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

        //            var dept = await _context.Departments.AsNoTracking()
        //                .FirstOrDefaultAsync(d => d.DepartmentId == requestedDepartmentId);
        //            if (dept is null)
        //                return Result<string>.Failure(new[] { "القسم غير موجود." });

        //            if (dept.BranchId != actor.BranchId.Value)
        //                return Result<string>.Failure(new[] { "لا يمكنك نقل/تعديل وثيقة لقسم خارج فرعك." });

        //            finalDepartmentId = dept.DepartmentId;
        //            dto.DepartmentId = finalDepartmentId;
        //        }
        //        else
        //        {
        //            var depExists = await _context.Departments
        //                .AnyAsync(d => d.DepartmentId == requestedDepartmentId);
        //            if (!depExists)
        //                return Result<string>.Failure(new[] { "القسم غير موجود." });

        //            finalDepartmentId = requestedDepartmentId;
        //            dto.DepartmentId = finalDepartmentId;
        //        }

        //        // صلاحيات الحقول
        //        var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
        //            dto, jwt, "Document", _fieldPerms, userId, roleIds);
        //        if (errors.Any())
        //            return Result<string>.Failure(errors.ToArray());

        //        // تحقق المرجعيات
        //        long? targetTypeId = dto.DocumentTypeId ?? entity.DocumentTypeId;
        //        if (!targetTypeId.HasValue)
        //            return Result<string>.Failure(new[] { "نوع الوثيقة مطلوب." });

        //        var docType = await _context.DocumentTypes.FindAsync(targetTypeId.Value);
        //        if (docType == null)
        //            return Result<string>.Failure(new[] { "نوع الوثيقة غير موجود." });

        //        long? targetGroupId = dto.ArchiveGroupId ?? entity.ArchiveGroupId;
        //        if (targetGroupId.HasValue)
        //        {
        //            var groupDeptId = await _context.ArchiveGroups
        //                .Where(g => g.ArchiveGroupId == targetGroupId.Value)
        //                .Select(g => (long?)g.DepartmentId)
        //                .FirstOrDefaultAsync();

        //            if (!groupDeptId.HasValue)
        //                return Result<string>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });

        //            if (groupDeptId.Value != finalDepartmentId)
        //                return Result<string>.Failure(new[] { "مجموعة الأرشفة لا تتبع نفس القسم النهائي." });
        //        }

        //        // Map التغييرات الأساسية
        //        _mapper.Map(dto, entity);
        //        entity.DepartmentId = finalDepartmentId;
        //        entity.DocumentTypeId = targetTypeId;
        //        entity.ArchiveGroupId = targetGroupId;

        //        // إعادة الترقيم عند تغيير القسم
        //        bool departmentChanged = (finalDepartmentId != originalDepartmentId);
        //        if (departmentChanged)
        //        {
        //            var newMax = await _context.Documents
        //                .Where(d => d.DepartmentId == finalDepartmentId)
        //                .Select(d => (long?)d.DocumentNumber)
        //                .MaxAsync() ?? 0;

        //            entity.DocumentNumber = newMax + 1;
        //        }

        //        _context.Documents.Update(entity);

        //        // حفظ مع Retry لتعارض الترقيم
        //        const int maxRetries = 5;
        //        for (int attempt = 1; attempt <= maxRetries; attempt++)
        //        {
        //            try
        //            {
        //                await _context.SaveChangesAsync();
        //                break;
        //            }
        //            catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
        //            {
        //                if (attempt == maxRetries)
        //                    return Result<string>.Failure(new[] { "تعذر حفظ التغييرات بسبب تعارض الترقيم بعد عدة محاولات." });

        //                var retryMax = await _context.Documents
        //                    .Where(d => d.DepartmentId == entity.DepartmentId)
        //                    .Select(d => (long?)d.DocumentNumber)
        //                    .MaxAsync() ?? 0;

        //                entity.DocumentNumber = retryMax + 1;
        //            }
        //        }

        //        // ربط المرفقات الجديدة إن أُرسلت (لا نقوم بإزالة الموجود حالياً حفاظاً على البيانات)
        //        if (dto.AttachmentIds != null && dto.AttachmentIds.Count > 0)
        //        {
        //            // يمكن التحسين مستقبلاً بمقارنة الموجود وإضافة الجديد فقط
        //            foreach (var attId in dto.AttachmentIds.Distinct())
        //            {
        //                await _attachments.LinkToEntityAsync(attId, entity.DocumentId, "Document");
        //            }
        //        }

        //        return Result<string>.Success("تم تحديث الوثيقة بنجاح.");
        //    }
        //    catch (Exception ex)
        //    {
        //        return Result<string>.Failure(new[] { $"حدث خطأ أثناء التحديث: {ex.Message}" });
        //    }
        //}
        public async Task<Result<string>> UpdateAsync(long documentId, UpdateDocumentDto dto)
        {
            try
            {
                var userId = CurrentUserId ?? string.Empty;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var entity = await _context.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
                if (entity == null) return Result<string>.Failure(new[] { "الوثيقة غير موجودة." });

                // === الممثل + الأدوار ===
                var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId) as User;
                if (actor is null) return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                // === تحديد القسم النهائي ===
                var originalDepartmentId = entity.DepartmentId;
                long requestedDepartmentId = dto.DepartmentId ?? entity.DepartmentId;
                long finalDepartmentId;

                if (!isSuperAdmin && !isBranchManager && actor.DepartmentId.HasValue)
                {
                    if (entity.DepartmentId != actor.DepartmentId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك تعديل وثيقة خارج قسمك." });

                    finalDepartmentId = actor.DepartmentId.Value;
                    dto.DepartmentId = finalDepartmentId;
                }
                else if (isBranchManager)
                {
                    if (!actor.BranchId.HasValue)
                        return Result<string>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                    var dept = await _context.Departments.AsNoTracking()
                        .FirstOrDefaultAsync(d => d.DepartmentId == requestedDepartmentId);
                    if (dept is null) return Result<string>.Failure(new[] { "القسم غير موجود." });
                    if (dept.BranchId != actor.BranchId.Value)
                        return Result<string>.Failure(new[] { "لا يمكنك نقل/تعديل وثيقة لقسم خارج فرعك." });

                    finalDepartmentId = dept.DepartmentId;
                    dto.DepartmentId = finalDepartmentId;
                }
                else
                {
                    var depExists = await _context.Departments.AnyAsync(d => d.DepartmentId == requestedDepartmentId);
                    if (!depExists) return Result<string>.Failure(new[] { "القسم غير موجود." });

                    finalDepartmentId = requestedDepartmentId;
                    dto.DepartmentId = finalDepartmentId;
                }

                // === صلاحيات الحقول ===
                var errors = await PermissionHelperUtilities.ValidateFieldPermissionsFromJwtAsync(
                    dto, jwt, "Document", _fieldPerms, userId, roleIds);
                if (errors.Any()) return Result<string>.Failure(errors.ToArray());

                // === تحقق المرجعيات ===
                long? targetTypeId = dto.DocumentTypeId ?? entity.DocumentTypeId;
                if (!targetTypeId.HasValue) return Result<string>.Failure(new[] { "نوع الوثيقة مطلوب." });

                var docType = await _context.DocumentTypes.FindAsync(targetTypeId.Value);
                if (docType == null) return Result<string>.Failure(new[] { "نوع الوثيقة غير موجود." });

                long? targetGroupId = dto.ArchiveGroupId ?? entity.ArchiveGroupId;
                if (targetGroupId.HasValue)
                {
                    var groupDeptId = await _context.ArchiveGroups
                        .Where(g => g.ArchiveGroupId == targetGroupId.Value)
                        .Select(g => (long?)g.DepartmentId)
                        .FirstOrDefaultAsync();

                    if (!groupDeptId.HasValue)
                        return Result<string>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });
                    if (groupDeptId.Value != finalDepartmentId)
                        return Result<string>.Failure(new[] { "مجموعة الأرشفة لا تتبع نفس القسم النهائي." });
                }

                // === تطبيق التغييرات الأساسية ===
                _mapper.Map(dto, entity);
                entity.DepartmentId = finalDepartmentId;
                entity.DocumentTypeId = targetTypeId;
                entity.ArchiveGroupId = targetGroupId;

                // إعادة ترقيم إذا تغيّر القسم
                bool departmentChanged = (finalDepartmentId != originalDepartmentId);
                if (departmentChanged)
                {
                    var newMax = await _context.Documents
                        .Where(d => d.DepartmentId == finalDepartmentId)
                        .Select(d => (long?)d.DocumentNumber)
                        .MaxAsync() ?? 0;
                    entity.DocumentNumber = newMax + 1;
                }
                entity.Description ??= string.Empty;

                using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    _context.Documents.Update(entity);

                    // حفظ الوثيقة أولاً (مع Retry لتعارض الترقيم)
                    const int maxRetries = 5;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try { await _context.SaveChangesAsync(); break; }
                        catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
                        {
                            if (attempt == maxRetries)
                                return Result<string>.Failure(new[] { "تعذر حفظ التغييرات بسبب تعارض الترقيم بعد عدة محاولات." });

                            var retryMax = await _context.Documents
                                .Where(d => d.DepartmentId == entity.DepartmentId)
                                .Select(d => (long?)d.DocumentNumber)
                                .MaxAsync() ?? 0;
                            entity.DocumentNumber = retryMax + 1;
                        }
                    }

                    // ================== مصالحة المرفقات ==================
                    // الحالة الحالية
                    var currentIds = await _context.Attachments.AsNoTracking()
                        .Where(a => a.OwnerType == "Document" && a.OwnerId == entity.DocumentId)
                        .Select(a => a.AttachmentId)
                        .ToListAsync();
                    var currentSet = currentIds.ToHashSet();

                    // تحديد الهدف النهائي
                    HashSet<Guid> targetSet = new(currentSet);

                    if (dto.ReplaceAttachments == true && dto.AttachmentIds is { Count: > 0 })
                    {
                        targetSet = dto.AttachmentIds.Distinct().ToHashSet();
                    }
                    else
                    {
                        if (dto.AddAttachmentIds is { Count: > 0 })
                            foreach (var id in dto.AddAttachmentIds.Distinct()) targetSet.Add(id);

                        if (dto.RemoveAttachmentIds is { Count: > 0 })
                            foreach (var id in dto.RemoveAttachmentIds.Distinct()) targetSet.Remove(id);
                    }

                    // احسب الفروقات
                    var toUnlink = currentSet.Except(targetSet).ToList();   // لازم تُفكّ ارتباطها
                    var toLink = targetSet.Except(currentSet).ToList();   // لازم تُربط

                    // تحقق وجود/صلاحية المرفقات التي سنربطها
                    if (toLink.Count > 0)
                    {
                        var candidates = await _context.Attachments
                            .Where(a => toLink.Contains(a.AttachmentId))
                            .ToListAsync();

                        var found = candidates.Select(a => a.AttachmentId).ToHashSet();
                        var missing = toLink.Where(id => !found.Contains(id)).ToList();
                        if (missing.Count > 0)
                            return Result<string>.Failure(new[] { $"مرفقات غير موجودة: [{string.Join(", ", missing)}]" });

                        // لا نسمح بربط مرفق مرتبط بكيان آخر مختلف
                        var locked = candidates
                            .Where(a => a.OwnerId != 0 && a.OwnerId != entity.DocumentId)
                            .Select(a => a.AttachmentId)
                            .ToList();
                        if (locked.Count > 0)
                            return Result<string>.Failure(new[] { $"مرفقات مرتبطة بكيان آخر: [{string.Join(", ", locked)}]" });

                        foreach (var a in candidates)
                        {
                            a.OwnerId = entity.DocumentId;
                            a.OwnerType = "Document";
                            a.UpdatedAt = DateTime.UtcNow;
                            a.UpdatedBy = userId;
                        }
                        _context.Attachments.UpdateRange(candidates);
                    }

                    // فكّ الارتباط للمرفقات التي سنزيلها (لا نحذف الملف)
                    if (toUnlink.Count > 0)
                    {
                        var willUnlink = await _context.Attachments
                            .Where(a => toUnlink.Contains(a.AttachmentId)
                                        && a.OwnerType == "Document"
                                        && a.OwnerId == entity.DocumentId)
                            .ToListAsync();

                        foreach (var a in willUnlink)
                        {
                            a.OwnerId = 0;         // مفصول
                            a.OwnerType = string.Empty;
                            a.IsProfilePhoto = false;
                            a.UpdatedAt = DateTime.UtcNow;
                            a.UpdatedBy = userId;
                        }
                        _context.Attachments.UpdateRange(willUnlink);
                    }

                    if (toLink.Count > 0 || toUnlink.Count > 0)
                        await _context.SaveChangesAsync();

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
        #endregion

        #region Delete document
        public async Task<Result<string>> DeleteAsync(long documentId)
        {
            try
            {
                var entity = await _context.Documents.FindAsync(documentId);
                if (entity == null)
                    return Result<string>.Failure(new[] { "الوثيقة غير موجودة." });

                _context.Documents.Remove(entity);
                await _context.SaveChangesAsync();

                // ملاحظة: لم نحذف أو نفك ربط المرفقات تلقائياً (حسب سياسة النظام الحالية).
                return Result<string>.Success("تم حذف الوثيقة بنجاح.");
            }
            catch (Exception ex)
            {
                return Result<string>.Failure(new[] { $"حدث خطأ أثناء الحذف: {ex.Message}" });
            }
        }
        #endregion

        #region GetById Document (مع فلترة الوصول بحسب الدور)
        public async Task<Result<DocumentDto>> GetByIdAsync(long documentId)
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

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor == null)
                    return Result<DocumentDto>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var baseQuery = _context.Documents.AsNoTracking()
                    .Where(d => d.DocumentId == documentId)
                    .Join(_context.Departments,
                          d => d.DepartmentId,
                          dep => dep.DepartmentId,
                          (d, dep) => new { d, dep.BranchId });

                if (isSuperAdmin)
                {
                    // يرى الكل
                }
                else if (isBranchManager)
                {
                    if (!actor.BranchId.HasValue)
                        return Result<DocumentDto>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                    baseQuery = baseQuery.Where(x => x.BranchId == actor.BranchId.Value);
                }
                else
                {
                    if (!actor.DepartmentId.HasValue)
                        return Result<DocumentDto>.Failure(new[] { "لا يمكن تحديد قسم المستخدم الحالي." });

                    baseQuery = baseQuery.Where(x => x.d.DepartmentId == actor.DepartmentId.Value);
                }

                var dto = await baseQuery
                    .Select(x => x.d)
                    .ProjectTo<DocumentDto>(_mapper.ConfigurationProvider)
                    .FirstOrDefaultAsync();

                if (dto == null)
                    return Result<DocumentDto>.Failure(new[] { "الوثيقة غير موجودة أو خارج نطاق وصولك." });

                // تطبيق صلاحيات الحقول
                dto = await _fieldPerms.ApplyFieldPermissionsAsync(dto, userId, roleIds, "Document", jwt);

                // ====== جلب المرفقات خارج الاستعلام ثم بناء روابط الملفات ======
                var attachmentsEntities = await _context.Attachments
                    .AsNoTracking()
                    .Where(a => a.OwnerType == "Document" && a.OwnerId == documentId)
                    .OrderByDescending(a => a.UploadedAt)
                    .ToListAsync(); // <- هُنا نأتي بالبيانات للذاكرة أولاً

                // حوّل إلى DTO محلي باستخدام  (لا مشكلة لأنها الآن عملية على الذاكرة)
                var attachments = attachmentsEntities.Select(a => new AttachmentLinkDto
                {
                    Id = a.AttachmentId,
                    FileUrl = BuildProtectedDownloadUrl(a.AttachmentId),
                    FileName = a.FileName ?? $"{a.AttachmentId}"
                }).ToList();

                dto.Attachments = attachments;

                return Result<DocumentDto>.Success(dto);
            }
            catch (Exception ex)
            {
                return Result<DocumentDto>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }

        #endregion

        #region GetAllDocuments Paged list (simple filter: title)
        public async Task<Result<PaginatedList<DocumentDto>>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, string? title = null)
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

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor == null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var query = _context.Documents.AsNoTracking()
                    .Join(_context.Departments,
                          d => d.DepartmentId,
                          dep => dep.DepartmentId,
                          (d, dep) => new { d, dep.BranchId });

                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                        query = query.Where(x => x.BranchId == actor.BranchId.Value);
                    }
                    else
                    {
                        if (!actor.DepartmentId.HasValue)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "لا يمكن تحديد قسم المستخدم الحالي." });

                        query = query.Where(x => x.d.DepartmentId == actor.DepartmentId.Value);
                    }
                }

                if (!string.IsNullOrWhiteSpace(title))
                    query = query.Where(x => x.d.Title.Contains(title));

                var projected = query
                    .OrderByDescending(x => x.d.CreatedAt)
                    .ThenByDescending(x => x.d.DocumentId)
                    .Select(x => x.d)
                    .ProjectTo<DocumentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentDto>.CreateAsync(projected, pageNumber, pageSize);

                // صلاحيات الحقول
                var permitted = await _fieldPerms.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Document", jwt);

                // ===================== تعبئة Attachments =====================
                var docIds = permitted.Select(d => d.DocumentId).ToList();

                if (docIds.Count > 0)
                {
                    var atts = await _context.Attachments.AsNoTracking()
                        .Where(a => a.OwnerType == "Document" && docIds.Contains(a.OwnerId))
                        .OrderByDescending(a => a.UploadedAt)
                        .Select(a => new
                        {
                            a.OwnerId,
                            a.AttachmentId,
                            a.FileName
                        })
                        .ToListAsync();

                    var map = atts
                        .GroupBy(x => x.OwnerId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => new AttachmentLinkDto
                            {
                                Id = x.AttachmentId,
                                FileUrl = BuildProtectedDownloadUrl(x.AttachmentId), // ✅
                                FileName = x.FileName ?? $"{x.AttachmentId}"         // ✅ fallback
                            }).ToList()
                        );

                    foreach (var d in permitted)
                        d.Attachments = map.TryGetValue(d.DocumentId, out var list) ? list : new List<AttachmentLinkDto>();
                }
                // ============================================================

                var result = new PaginatedList<DocumentDto>(
                    permitted, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DocumentDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Dropdown List
        public async Task<Result<IEnumerable<DropDownDto>>> GetDocumentsDropdownAsync()
        {
            var q = _context.Documents.AsNoTracking().AsQueryable();
            var data = await q
                .OrderBy(d => d.DocumentId)
                .Select(d => new DropDownDto
                {
                    Id = d.DocumentId,
                    Name = d.Title
                })
                .ToListAsync();

            return Result<IEnumerable<DropDownDto>>.Success(data);
        }
        #endregion

        #region Get Documents By ArchiveGroupId (paged)
        public async Task<Result<PaginatedList<DocumentDto>>> GetByArchiveGroupPagedAsync(long archiveGroupId, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var groupInfo = await _context.ArchiveGroups
                    .AsNoTracking()
                    .Where(g => g.ArchiveGroupId == archiveGroupId)
                    .Join(_context.Departments,
                          g => g.DepartmentId,
                          d => d.DepartmentId,
                          (g, d) => new { d.DepartmentId, d.BranchId })
                    .FirstOrDefaultAsync();

                if (groupInfo is null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "مجموعة الأرشفة غير موجودة." });

                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor is null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var roles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = roles.Any(r => r == "SUPERADMIN");
                bool isBranchManager = roles.Any(r => r == "BRANCHMANAGER");

                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue || actor.BranchId.Value != groupInfo.BranchId)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "غير مسموح بالوصول إلى وثائق هذه المجموعة." });
                    }
                    else
                    {
                        if (!actor.DepartmentId.HasValue || actor.DepartmentId.Value != groupInfo.DepartmentId)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "غير مسموح بالوصول إلى وثائق هذه المجموعة." });
                    }
                }

                var query = _context.Documents.AsNoTracking()
                    .Where(d => d.ArchiveGroupId == archiveGroupId)
                    .OrderBy(d => d.DocumentNumber)
                    .ProjectTo<DocumentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentDto>.CreateAsync(query, pageNumber, pageSize);

                var permitted = await _fieldPerms.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Document", jwt);

                return Result<PaginatedList<DocumentDto>>.Success(
                    new PaginatedList<DocumentDto>(permitted, paged.TotalCount, paged.PageNumber, pageSize));
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Get Ungrouped Documents By DepartmentId (paged)
        public async Task<Result<PaginatedList<DocumentDto>>> GetUngroupedByDepartmentPagedAsync(long departmentId, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                var deptInfo = await _context.Departments
                    .AsNoTracking()
                    .Where(d => d.DepartmentId == departmentId)
                    .Select(d => new { d.DepartmentId, d.BranchId })
                    .FirstOrDefaultAsync();

                if (deptInfo is null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "القسم غير موجود." });

                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor is null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                var roles = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = roles.Any(r => r == "SUPERADMIN");
                bool isBranchManager = roles.Any(r => r == "BRANCHMANAGER");

                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue || actor.BranchId.Value != deptInfo.BranchId)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "غير مسموح بالوصول إلى وثائق هذا القسم." });
                    }
                    else
                    {
                        if (!actor.DepartmentId.HasValue || actor.DepartmentId.Value != departmentId)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "غير مسموح بالوصول إلى وثائق هذا القسم." });
                    }
                }

                var query = _context.Documents.AsNoTracking()
                    .Where(d => d.DepartmentId == departmentId)
                    .OrderBy(d => d.DocumentNumber)
                    .ProjectTo<DocumentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentDto>.CreateAsync(query, pageNumber, pageSize);

                var permitted = await _fieldPerms.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Document", jwt);

                return Result<PaginatedList<DocumentDto>>.Success(
                    new PaginatedList<DocumentDto>(permitted, paged.TotalCount, paged.PageNumber, pageSize));
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء الجلب: {ex.Message}" });
            }
        }
        #endregion

        #region Search (advanced + paged) with Scope + proper ordering
        public async Task<Result<PaginatedList<DocumentDto>>> SearchAsync(DocumentSearchCriteria c, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var userId = CurrentUserId;
                var roleIds = CurrentRoleIds;
                var jwt = JwtFieldPerms;

                // ===== Roles =====
                var actorRoleNorms = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
                    .ToListAsync();

                bool isSuperAdmin = actorRoleNorms.Any(n => n == "SUPERADMIN");
                bool isBranchManager = actorRoleNorms.Any(n => n == "BRANCHMANAGER");

                var actor = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.BranchId, u.DepartmentId })
                    .FirstOrDefaultAsync();

                if (actor == null)
                    return Result<PaginatedList<DocumentDto>>.Failure(new[] { "المستخدم الحالي غير موجود." });

                // ===== Base query WITH scope =====
                // نضمّ Departments حتى نفلتر بالفرع لمدير الفرع
                var q = _context.Documents.AsNoTracking()
                    .Join(_context.Departments.AsNoTracking(),
                          d => d.DepartmentId,
                          dep => dep.DepartmentId,
                          (d, dep) => new { d, dep.BranchId })
                    .AsQueryable();

                if (!isSuperAdmin)
                {
                    if (isBranchManager)
                    {
                        if (!actor.BranchId.HasValue)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "لا يمكن تحديد فرع المستخدم الحالي." });

                        q = q.Where(x => x.BranchId == actor.BranchId.Value);
                    }
                    else
                    {
                        if (!actor.DepartmentId.HasValue)
                            return Result<PaginatedList<DocumentDto>>.Failure(new[] { "لا يمكن تحديد قسم المستخدم الحالي." });

                        // إذا بدك “يشوف وثائقه حتى لو خارج قسمه”:
                        // q = q.Where(x => x.d.DepartmentId == actor.DepartmentId.Value || x.d.UserId == userId);
                        q = q.Where(x => x.d.DepartmentId == actor.DepartmentId.Value);
                    }
                }

                // ===== Filters =====
                if (c.DepartmentId.HasValue)
                    q = q.Where(x => x.d.DepartmentId == c.DepartmentId.Value);

                if (c.DocumentTypeId.HasValue)
                    q = q.Where(x => x.d.DocumentTypeId == c.DocumentTypeId.Value);

                if (c.ArchiveGroupId.HasValue)
                    q = q.Where(x => x.d.ArchiveGroupId == c.ArchiveGroupId.Value);

                // ===== Search (Title/Description) =====
                if (!string.IsNullOrWhiteSpace(c.Title))
                {
                    var t = c.Title.Trim();
                    q = q.Where(x => x.d.Title.Contains(t));
                    // إذا أنت PostgreSQL وتحب بحث غير حساس لحالة الأحرف:
                    // q = q.Where(x => EF.Functions.ILike(x.d.Title, $"%{t}%"));
                }

                if (!string.IsNullOrWhiteSpace(c.Description))
                {
                    var dsc = c.Description.Trim();
                    q = q.Where(x => x.d.Description.Contains(dsc));
                    // PostgreSQL:
                    // q = q.Where(x => EF.Functions.ILike(x.d.Description, $"%{dsc}%"));
                }

                // ===== Between dates =====
                if (c.CreatedFrom.HasValue)
                {
                    var from = c.CreatedFrom.Value;
                    q = q.Where(x => x.d.CreatedAt >= from);
                }

                if (c.CreatedTo.HasValue)
                {
                    // inclusive لآخر اليوم
                    var toExclusive = c.CreatedTo.Value.Date.AddDays(1);
                    q = q.Where(x => x.d.CreatedAt < toExclusive);
                }

                // ===== Sorting =====
                // الافتراضي: الأحدث أولاً (حتى يظهر الجديد بأول صفحة)
                var sortBy = (c.SortBy ?? "createdat").Trim().ToLowerInvariant();
                bool desc = c.SortDescending;

                q = sortBy switch
                {
                    "title" => desc ? q.OrderByDescending(x => x.d.Title).ThenByDescending(x => x.d.CreatedAt)
                                    : q.OrderBy(x => x.d.Title).ThenByDescending(x => x.d.CreatedAt),

                    "documentnumber" => desc ? q.OrderByDescending(x => x.d.DocumentNumber).ThenByDescending(x => x.d.CreatedAt)
                                             : q.OrderBy(x => x.d.DocumentNumber).ThenByDescending(x => x.d.CreatedAt),

                    "createdat" => desc ? q.OrderByDescending(x => x.d.CreatedAt).ThenByDescending(x => x.d.DocumentId)
                                        : q.OrderBy(x => x.d.CreatedAt).ThenByDescending(x => x.d.DocumentId),

                    _ => q.OrderByDescending(x => x.d.CreatedAt).ThenByDescending(x => x.d.DocumentId)
                };

                // ===== Projection + Pagination =====
                var projected = q.Select(x => x.d)
                    .ProjectTo<DocumentDto>(_mapper.ConfigurationProvider);

                var paged = await PaginatedList<DocumentDto>.CreateAsync(projected, pageNumber, pageSize);

                // ===== Field Permissions =====
                var permitted = await _fieldPerms.ApplyFieldPermissionsAsync(
                    paged.Items.ToList(), userId, roleIds, "Document", jwt);

                var result = new PaginatedList<DocumentDto>(
                    permitted, paged.TotalCount, paged.PageNumber, pageSize);

                return Result<PaginatedList<DocumentDto>>.Success(result);
            }
            catch (Exception ex)
            {
                return Result<PaginatedList<DocumentDto>>.Failure(new[] { $"حدث خطأ أثناء البحث: {ex.Message}" });
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


        private string BuildProtectedDownloadUrl(Guid id)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request == null)
                return $"/api/attachments/download/{id}";

            return $"{request.Scheme}://{request.Host}/api/attachments/download/{id}";
        }

    }
}
