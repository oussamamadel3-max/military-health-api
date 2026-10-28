namespace Application.Dtos.ClinicWorkspace;

/// <summary>معاملات قائمة فحوصات عيادة العيون (شريحة قائمة FR-039).</summary>
public class EyeExamListQuery
{
    public Guid ClinicId { get; set; }
    public long? FileNumber { get; set; }
    public string? Barcode { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>عنصر قائمة فحوصات العيون الحالية ضمن المركز والعيادة المسندة.</summary>
public class EyeExamListItemDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? VisualAcuityRight { get; set; }
    public string? VisualAcuityLeft { get; set; }
    public string? ColorTestResult { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerUserName { get; set; }
    public DateTimeOffset ExaminedAt { get; set; }
}
