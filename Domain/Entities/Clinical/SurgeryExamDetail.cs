namespace Domain.Entities.Clinical;

public class SurgeryExamDetail
{
    public Guid ExamId { get; set; }
    public string? GeneralSurgery { get; set; }
    public string? Urology { get; set; }
    public string? VascularThoracic { get; set; }

    public MedicalExam Exam { get; set; } = null!;
}
