namespace Application.Dtos.Centers;

public class CreateMedicalCenterDto
{
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? LocationText { get; set; }
    public string? ParentOrgText { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? DeactivationReason { get; set; }
}

public class UpdateMedicalCenterDto
{
    public string NameAr { get; set; } = null!;
    public string? LocationText { get; set; }
    public string? ParentOrgText { get; set; }
    public string? ContactInfo { get; set; }
}

public class DeactivateMedicalCenterDto
{
    public string Reason { get; set; } = null!;
}

public class MedicalCenterListItemDto
{
    public Guid CenterId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? LocationText { get; set; }
    public string? ParentOrgText { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; }
    public int UsersCount { get; set; }
    public int ClinicsCount { get; set; }
}

public class MedicalCenterDetailsDto
{
    public Guid CenterId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? LocationText { get; set; }
    public string? ParentOrgText { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; }
    public string? DeactivationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int UsersCount { get; set; }
    public int ClinicsCount { get; set; }
}
