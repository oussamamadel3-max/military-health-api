namespace Domain.Entities.Core;

/// <summary>
/// ملف مخزّن. يحل محل Attachment كنموذج البيانات الوحيد للملفات.
/// CenterId إلزامي ويطابق المركز النشط عند الرفع.
/// </summary>
public class FileAsset
{
    public Guid FileAssetId { get; set; }
    public Guid? CenterId { get; set; }
    public string StorageKey { get; set; } = null!;
    public string OriginalName { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public string Extension { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string UploadedByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public MedicalCenter? Center { get; set; }
    public Auth.User? UploadedByUser { get; set; }
}
