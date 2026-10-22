namespace Application.Dtos.Centers;

public class LinkCenterClinicDto
{
    public Guid ClinicId { get; set; }
}

public class CenterClinicListItemDto
{
    public Guid CenterClinicId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public short ClinicSortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

public class CenterClinicDetailsDto
{
    public Guid CenterClinicId { get; set; }
    public Guid CenterId { get; set; }
    public string CenterCode { get; set; } = null!;
    public string CenterNameAr { get; set; } = null!;
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public short ClinicSortOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}
