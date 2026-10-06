namespace Domain.Entities.Clinical;

public class InternalExamDetail
{
    public Guid ExamId { get; set; }
    public string? Cardiovascular { get; set; }
    public string? Respiratory { get; set; }
    public string? Digestive { get; set; }
    public string? Endocrine { get; set; }
    public string? Neurological { get; set; }
    public string? Hematology { get; set; }
    public string? Joints { get; set; }
    public string? Renal { get; set; }
    public string? Skin { get; set; }

    public MedicalExam Exam { get; set; } = null!;
}
