using Domain.Entities.Clinical;

namespace Application.Services.ClinicWorkspace;

/// <summary>
/// BR-069: المنفّذ الأصلي (<see cref="MedicalExam.ExaminerUserId"/>) يُثبت عند الإنشاء؛
/// التعديلات اللاحقة تُسجّل عبر <see cref="MedicalExam.UpdatedByUserId"/> فقط.
/// </summary>
internal static class MedicalExamIdentity
{
    public static void RecordModification(MedicalExam exam, string modifierUserId, DateTimeOffset now)
    {
        exam.VersionNo += 1;
        exam.UpdatedByUserId = modifierUserId;
        exam.UpdatedAt = now;
    }
}
