using Domain.Enums;

namespace Application.Common.Permissions;

/// <summary>
/// دلالات موحّدة لـ <see cref="FieldAccessType"/> ودمج تعارضات الأدوار/المستخدم.
/// الحقول غير المدرجة في كتالوج الحماية لا تمر عبر هذه الدلالات.
/// </summary>
public static class FieldAccessSemantics
{
    /// <summary>هل يسمح النوع بقراءة قيمة الحقل؟</summary>
    public static bool CanRead(FieldAccessType accessType)
        => accessType is FieldAccessType.Read or FieldAccessType.ReadWrite;

    /// <summary>هل يسمح النوع بكتابة قيمة الحقل؟</summary>
    public static bool CanWrite(FieldAccessType accessType)
        => accessType is FieldAccessType.Write or FieldAccessType.ReadWrite;

    /// <summary>
    /// دمج صلاحيات أدوار متعددة باتحاد قدرات القراءة/الكتابة
    /// (إن منح أي دور قراءة وأخر كتابة تصبح ReadWrite).
    /// </summary>
    public static FieldAccessType MergeRoleAccess(IEnumerable<FieldAccessType> roleAccessTypes)
    {
        var canRead = false;
        var canWrite = false;

        foreach (var access in roleAccessTypes)
        {
            canRead |= CanRead(access);
            canWrite |= CanWrite(access);
        }

        return Compose(canRead, canWrite);
    }

    /// <summary>
    /// صلاحية المستخدم المباشرة تستبدل نتيجة الأدوار بالكامل عند وجودها.
    /// </summary>
    public static FieldAccessType ResolveEffective(
        FieldAccessType? userAccess,
        IEnumerable<FieldAccessType> roleAccessTypes)
    {
        if (userAccess.HasValue)
            return userAccess.Value;

        return MergeRoleAccess(roleAccessTypes);
    }

    /// <summary>
    /// للحقول المحمية فقط: غياب أي تعيين يعني <see cref="FieldAccessType.None"/> (Default Deny).
    /// </summary>
    public static FieldAccessType DefaultWhenUnset => FieldAccessType.None;

    private static FieldAccessType Compose(bool canRead, bool canWrite)
    {
        if (canRead && canWrite)
            return FieldAccessType.ReadWrite;
        if (canRead)
            return FieldAccessType.Read;
        if (canWrite)
            return FieldAccessType.Write;
        return FieldAccessType.None;
    }
}
