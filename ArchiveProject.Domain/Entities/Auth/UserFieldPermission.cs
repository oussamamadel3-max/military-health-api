namespace ArchiveProject.Domain.Entities.Auth
{
    public class UserFieldPermission
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FieldPermissionId { get; set; }
        public FieldPermission FieldPermission { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;
    }

}

