using System.ComponentModel;

namespace ArchiveProject.Domain.Enums
{
    /// <summary>
    /// أنواع الصلاحيات على مستوى الحقول
    /// </summary>
    public enum FieldAccessType
    {
        /// <summary>
        /// لا يوجد وصول - الحقل مخفي تماماً
        /// </summary>
        [Description("لا يوجد وصول")]
        None = 0,

        /// <summary>
        /// قراءة فقط - يمكن عرض الحقل لكن لا يمكن تعديله
        /// </summary>
        [Description("قراءة فقط")]
        Read = 1,

        /// <summary>
        /// كتابة فقط - يمكن تعديل الحقل لكن لا يمكن قراءته
        /// </summary>
        [Description("كتابة فقط")]
        Write = 2,

        /// <summary>
        /// قراءة وكتابة - وصول كامل للحقل
        /// </summary>
        [Description("قراءة وكتابة")]
        ReadWrite = 3,


    }
}