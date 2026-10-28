namespace Application.Dtos.ClinicWorkspace;

/// <summary>معاملات قائمة فحوصات عيادة التخصص ضمن شريحة قائمة FR-039.</summary>
public class ClinicExamListQuery
{
    public Guid ClinicId { get; set; }
    public long? FileNumber { get; set; }
    public string? Barcode { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>عنصر قائمة فحوصات الباطنة الحالية.</summary>
public class InternalExamListItemDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? Cardiovascular { get; set; }
    public string? Respiratory { get; set; }
    public string? Digestive { get; set; }
    public string? Endocrine { get; set; }
    public string? Neurological { get; set; }
    public string? Hematology { get; set; }
    public string? Joints { get; set; }
    public string? Renal { get; set; }
    public string? Skin { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerUserName { get; set; }
    public DateTimeOffset ExaminedAt { get; set; }
}

/// <summary>عنصر قائمة فحوصات الجراحة الحالية.</summary>
public class SurgeryExamListItemDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? GeneralSurgery { get; set; }
    public string? Urology { get; set; }
    public string? VascularThoracic { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerUserName { get; set; }
    public DateTimeOffset ExaminedAt { get; set; }
}

/// <summary>عنصر قائمة فحوصات العظمية الحالية.</summary>
public class OrthopedicExamListItemDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? Musculoskeletal { get; set; }
    public string? Neurosurgery { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerUserName { get; set; }
    public DateTimeOffset ExaminedAt { get; set; }
}

/// <summary>عنصر قائمة فحوصات الأذنية الحالية.</summary>
public class EntExamListItemDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? RightEar { get; set; }
    public string? LeftEar { get; set; }
    public string? TympanicMembrane { get; set; }
    public string? HearingTest { get; set; }
    public string? WhisperTest { get; set; }
    public string? NoseExam { get; set; }
    public string? MouthExam { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerUserName { get; set; }
    public DateTimeOffset ExaminedAt { get; set; }
}
