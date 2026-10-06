namespace Domain.Entities.Clinical;

public class OrthopedicExamDetail
{
    public Guid ExamId { get; set; }
    public string? Musculoskeletal { get; set; }
    public string? Neurosurgery { get; set; }

    public MedicalExam Exam { get; set; } = null!;
}
