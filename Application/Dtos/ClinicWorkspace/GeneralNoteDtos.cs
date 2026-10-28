namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب إدخال أو تحديث أو إفراغ الملاحظة العامة للمنتسب (FR-043).</summary>
public class UpsertMemberGeneralNoteRequest
{
    /// <summary>عيادة الطبيب الكاتب ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نص الملاحظة؛ النص الفارغ أو المسافات يوقف التنبيه دون حذف الصف.</summary>
    public string? NoteText { get; set; }
}

/// <summary>استجابة حفظ الملاحظة العامة مع بيانات التنبيه.</summary>
public class MemberGeneralNoteSavedDto
{
    public Guid NoteId { get; set; }
    public Guid MemberId { get; set; }
    public string NoteText { get; set; } = null!;
    public bool HasGeneralNotesAlert { get; set; }
    public string AuthorUserId { get; set; } = null!;
    public string? AuthorUserName { get; set; }
    public Guid AuthorClinicId { get; set; }
    public string? AuthorClinicNameAr { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
}
