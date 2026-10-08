namespace Domain.Entities.Core;

public class Clinic
{
    public Guid ClinicId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public short SortOrder { get; set; }
    public bool CountsForFinal { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
