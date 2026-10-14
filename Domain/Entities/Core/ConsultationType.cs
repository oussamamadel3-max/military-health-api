using Domain.Interfaces;

namespace Domain.Entities.Core;

public class ConsultationType : IAuditableEntity
{
    public short ConsultationTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? Description { get; set; }
    public short SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
