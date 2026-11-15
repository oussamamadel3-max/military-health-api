using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

/// <summary>
/// كتالوج صريح لكيانات/حقول صلاحيات الحقول المعتمدة عبر attributes.
/// لا يكتشف كيانات Domain غير المعلَّمة.
/// </summary>
public interface IFieldPermissionCatalog
{
    /// <summary>الكيانات المعلَّمة بـ FieldPermissionEntity فقط.</summary>
    IReadOnlyList<EntityInfoDto> GetEntities();

    /// <summary>
    /// حقول الكيان المعتمدة (خصائص DTO ذات FieldPermission المرتبطة بالكيان).
    /// يرمي إن كان الكيان خارج الكتالوج.
    /// </summary>
    IReadOnlyList<FieldInfoDto> GetFields(string entityName);

    /// <summary>هل الكيان ضمن الكتالوج المعتمد؟</summary>
    bool IsEntityRegistered(string entityName);

    /// <summary>هل الحقل معتمد ضمن كيان الكتالوج؟</summary>
    bool IsFieldRegistered(string entityName, string fieldName);

    /// <summary>اسم العرض للكيان أو الاسم الخام إن وُجد في الكتالوج.</summary>
    string GetEntityDisplayName(string entityName);

    /// <summary>خريطة اسم الحقل → اسم العرض للكيان، أو فارغة إن لم يُسجَّل.</summary>
    IReadOnlyDictionary<string, string> GetFieldDisplayMap(string entityName);
}
