using Domain.Attributes;
using Domain.Common;
using Domain.Entities.Auth;
using Domain.Interfaces;

namespace Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "الوثيقة")]
    public class Document : BaseEntity, IAuditableEntity
    {
        public long DocumentId { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>
        /// رقم الوثيقة فريد على مستوى النظام (توليد تلقائي)
        /// </summary>
        public long DocumentNumber { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long GetPrimaryKey() => DocumentId;
    }

}
