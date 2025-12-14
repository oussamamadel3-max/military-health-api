using ArchiveProject.Domain.Attributes;
using ArchiveProject.Domain.Common;
using ArchiveProject.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "نوع الوثيقة")]
    public class DocumentType : BaseEntity, IAuditableEntity
    {

        [Key]
        public long DocumentTypeId { get; set; }

        [Required, MaxLength(250)]
        public string DocumentTypeName { get; set; }
        public string AllowedExtensions { get; set; } = string.Empty;

        public IList<Document> Documents { get; set; } = new List<Document>();

        public long GetPrimaryKey() => DocumentTypeId;

    }
}
