namespace Domain.Entities.Clinical;

public class EyeExamDetail
{
    public Guid ExamId { get; set; }
    public string? VisualAcuityRight { get; set; }
    public string? VisualAcuityLeft { get; set; }
    public string? ColorTestResult { get; set; }
    public string? OtherEyeDiseases { get; set; }

    public MedicalExam Exam { get; set; } = null!;
}
