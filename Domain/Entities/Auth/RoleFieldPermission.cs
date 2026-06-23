namespace Domain.Entities.Auth
{
    public class RoleFieldPermission
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FieldPermissionId { get; set; }
        public FieldPermission FieldPermission { get; set; } = null!;

        public string RoleId { get; set; } = string.Empty;
        public Role Role { get; set; } = null!;
    }

}

