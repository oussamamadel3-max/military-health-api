namespace Domain.Entities.Core;

public class BmiCategory
{
    public short CategoryId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public decimal MinBmi { get; set; }
    public decimal? MaxBmi { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
