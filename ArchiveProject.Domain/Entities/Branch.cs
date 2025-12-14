using ArchiveProject.Domain.Attributes;
using ArchiveProject.Domain.Common;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Domain.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Domain.Entities
{
    [FieldPermissionEntity(DisplayName = "الفرع")]
    public class Branch : BaseEntity, IAuditableEntity
    {

        [Key]
        public long BranchId { get; set; }

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string BranchName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public IList<Department> Departments { get; set; } = new List<Department>();
        public IList<User> Users { get; set; } = new List<User>();


        public long GetPrimaryKey() => BranchId;

    }

}
