using ArchiveProject.Domain.Attributes;
using ArchiveProject.Domain.Common;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;
namespace ArchiveProject.Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "القسم")]
    public class Department : BaseEntity, IAuditableEntity
    {

        [Key]
        public long DepartmentId { get; set; }

        [Required, MaxLength(250)]
        public string DepartmentName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public IList<User> Users { get; set; } = new List<User>();


        public long GetPrimaryKey() => DepartmentId;

    }

}

