using Domain.Attributes;
using Domain.Common;
using Domain.Entities.Auth;
using Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "الوثيقة")]
    public class Document : BaseEntity, IAuditableEntity
    {
        [Key]
        public long DocumentId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>
        /// رقم الوثيقة فريد على مستوى النظام (توليد تلقائي)
        /// </summary>
        [Required]
        public long DocumentNumber { get; set; }

        [Required, MaxLength(250)]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long GetPrimaryKey() => DocumentId;
    }

}
