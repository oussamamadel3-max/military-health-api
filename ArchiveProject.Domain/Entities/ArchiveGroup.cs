using ArchiveProject.Domain.Attributes;
using ArchiveProject.Domain.Common;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "مجموعة الارشفة")]
    public class ArchiveGroup : BaseEntity, IAuditableEntity
    {
        [Key]
        public long ArchiveGroupId { get; set; }

        [Required]
        public long DepartmentId { get; set; }
        public Department Department { get; set; } = null!;

        [Required]              
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        [Required, MaxLength(250)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public long ArchiveGroupNumber { get; set; }

        public IList<Document> Documents { get; set; } = new List<Document>();

        public long GetPrimaryKey() => ArchiveGroupId;
    }

}
