namespace Application.Dtos.ReferenceData;

public static class ReferenceCatalogs
{
    public const string RecruitmentCenters = "recruitment-centers";
    public const string Clinics = "clinics";
    public const string ResultStatuses = "result-statuses";
    public const string RequiredActionTypes = "required-action-types";
    public const string MaritalStatuses = "marital-statuses";
    public const string BloodTypes = "blood-types";
    public const string ConsultationTypes = "consultation-types";
    public const string InvestigationTypes = "investigation-types";
    public const string BmiCategories = "bmi-categories";
    public const string ValidationRules = "validation-rules";

    public static readonly IReadOnlyList<string> All =
    [
        RecruitmentCenters,
        Clinics,
        ResultStatuses,
        RequiredActionTypes,
        MaritalStatuses,
        BloodTypes,
        ConsultationTypes,
        InvestigationTypes,
        BmiCategories,
        ValidationRules
    ];
}

public class ReferenceDataItemDto
{
    public string Id { get; set; } = null!;
    public string Catalog { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public short? SortOrder { get; set; }
    public bool IsActive { get; set; }
    public bool? IsSystem { get; set; }
    public bool? IsLocked { get; set; }
    public bool? CountsForFinal { get; set; }
    public bool? RequiresDescription { get; set; }
    public bool? RequiresTargetClinic { get; set; }
    public decimal? MinBmi { get; set; }
    public decimal? MaxBmi { get; set; }
    public string? TargetEntity { get; set; }
    public string? TargetField { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Unit { get; set; }
    public int? VersionNo { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class ReferenceDataOptionDto
{
    public string Id { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public short? SortOrder { get; set; }
}

public class ReferenceDataWriteDto
{
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public short? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool? CountsForFinal { get; set; }
    public bool? RequiresDescription { get; set; }
    public bool? RequiresTargetClinic { get; set; }
    public decimal? MinBmi { get; set; }
    public decimal? MaxBmi { get; set; }
    public string? TargetEntity { get; set; }
    public string? TargetField { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Unit { get; set; }
    public int? VersionNo { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
}

public class ReferenceDataSetActiveDto
{
    public bool IsActive { get; set; }
}
