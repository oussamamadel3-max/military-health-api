namespace Domain.Entities.Clinical;

public class EntExamDetail
{
    public Guid ExamId { get; set; }
    public string? RightEar { get; set; }
    public string? LeftEar { get; set; }
    public string? TympanicMembrane { get; set; }
    public string? HearingTest { get; set; }
    public string? WhisperTest { get; set; }
    public string? NoseExam { get; set; }
    public string? MouthExam { get; set; }
    public string? OtherEntDiseases { get; set; }

    public MedicalExam Exam { get; set; } = null!;
}
