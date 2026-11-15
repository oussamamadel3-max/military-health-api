using Application.Dtos.Members;

namespace Application.Interfaces.Services;

/// <summary>
/// إنفاذ صلاحيات الحقل التجريبي <c>Member.MotherName</c> على مسارات القراءة/الكتابة المعتمدة.
/// محكوم بـ <c>FieldPermissions:EnforcementEnabled</c> (افتراضيًا معطّل).
/// </summary>
public interface IMemberFieldPermissionGuard
{
    /// <summary>هل إنفاذ الحقول مفعّل حالياً؟</summary>
    bool IsEnforcementEnabled { get; }

    /// <summary>
    /// يُخفي <see cref="MemberDetailsDto.MotherName"/> في الاستجابة إن لم تتوفر صلاحية قراءة فعالة.
    /// لا يمس قاعدة البيانات.
    /// </summary>
    Task ApplyReadToDetailsAsync(MemberDetailsDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// يفرض صلاحية الكتابة عند إنشاء قيمة غير فارغة أو عند تغيير/مسح القيمة في التحديث.
    /// إعادة إرسال القيمة نفسها في تحديث كامل لا تُعد تعديلاً.
    /// </summary>
    /// <param name="proposedMotherName">القيمة الواردة من الطلب.</param>
    /// <param name="existingMotherName">القيمة المخزّنة؛ <c>null</c> عند الإنشاء.</param>
    /// <param name="isCreate"><c>true</c> لمسار الإنشاء/الاستيراد.</param>
    Task EnsureMotherNameWriteAllowedAsync(
        string? proposedMotherName,
        string? existingMotherName,
        bool isCreate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// يُخفي <c>MotherName</c> في صفوف معاينة الاستيراد المُعادة للعميل عند غياب صلاحية القراءة.
    /// لا يغيّر <c>ValidRowsJson</c> المخزّن للاعتماد.
    /// </summary>
    Task<IReadOnlyList<MemberImportValidRow>> MaskImportPreviewRowsAsync(
        IReadOnlyList<MemberImportValidRow> rows,
        CancellationToken cancellationToken = default);
}
