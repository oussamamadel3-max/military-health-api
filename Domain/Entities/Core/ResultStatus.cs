namespace Domain.Entities.Core;

public class ResultStatus
{
    public short StatusId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; } = true;
}
