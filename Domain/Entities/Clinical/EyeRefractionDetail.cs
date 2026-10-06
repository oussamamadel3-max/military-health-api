namespace Domain.Entities.Clinical;

public class EyeRefractionDetail
{
    public Guid RefractionDetailId { get; set; }
    public Guid ExamId { get; set; }
    public string EyeSide { get; set; } = null!;
    public string RefractionType { get; set; } = null!;
    public string InputFormat { get; set; } = null!;
    public decimal? Numerator { get; set; }
    public decimal? Denominator { get; set; }
    public decimal? DecimalInput { get; set; }
    public decimal? CalculatedValue { get; set; }
    public bool IsWorst { get; set; }

    public EyeExamDetail EyeExamDetail { get; set; } = null!;
}
