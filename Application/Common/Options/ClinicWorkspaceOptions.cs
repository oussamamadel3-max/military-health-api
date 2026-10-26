namespace Application.Common.Options;

/// <summary>
/// إعدادات مساحة عمل العيادة المؤقتة. مهلة قفل التحرير مؤقتة حتى تعتمد الجهة قيمة رسمية (SRS FR-045 «مهلة معتمدة»).
/// </summary>
public sealed class ClinicWorkspaceOptions
{
    public const string SectionName = "ClinicWorkspace";

    /// <summary>مدة القفل بالدقائق (افتراضي 30). ليست قرار CON معتمداً.</summary>
    public int EditLockTimeoutMinutes { get; set; } = 30;
}
