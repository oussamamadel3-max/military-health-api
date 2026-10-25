namespace Application.Dtos.Members;

/// <summary>صف منتسب صالح جاهز للاعتماد من جلسة الاستيراد.</summary>
public class MemberImportValidRow
{
    public int RowNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string MotherName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public short MaritalStatusId { get; set; }
    public string Occupation { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public short? BloodTypeId { get; set; }
    public decimal? BloodSugarMgDl { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? BloodPressure { get; set; }
    public short? PulseBpm { get; set; }
    public bool HasTattoo { get; set; }
    public string? DistinguishingMarks { get; set; }
}

/// <summary>صف مرفوض أو مكرر في معاينة الاستيراد.</summary>
public class MemberImportIssueRow
{
    public int RowNumber { get; set; }
    /// <summary>Duplicate | Invalid</summary>
    public string Status { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string? RecruitmentNumber { get; set; }
}

/// <summary>نتيجة معاينة استيراد Excel دون حفظ منتسبين.</summary>
public class MemberImportPreviewResult
{
    public Guid ImportId { get; set; }
    public int TotalRows { get; set; }
    public int ValidCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public IReadOnlyList<MemberImportIssueRow> Issues { get; set; } = Array.Empty<MemberImportIssueRow>();
}

/// <summary>نتيجة اعتماد السجلات الصالحة من جلسة الاستيراد.</summary>
public class MemberImportCommitResult
{
    public Guid ImportId { get; set; }
    public int CommittedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public int SkippedInvalidCount { get; set; }
    public IReadOnlyList<long> CreatedFileNumbers { get; set; } = Array.Empty<long>();
}

/// <summary>صف تصدير قائمة المنتسبين مع هوية المركز.</summary>
public class MemberExportRowDto
{
    public long FileNumber { get; set; }
    public int QueueNumber { get; set; }
    public DateOnly QueueDate { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CurrentMedicalCenterId { get; set; }
    public string? CurrentMedicalCenterNameAr { get; set; }
    public decimal? Bmi { get; set; }
    public string? BmiCategoryNameAr { get; set; }
}
