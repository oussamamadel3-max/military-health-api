using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عندما لا يكون الطبيب مسنداً بفعالية إلى العيادة المطلوبة في المركز النشط.</summary>
public class DoctorClinicNotAssignedException : BusinessException
{
    public DoctorClinicNotAssignedException()
        : base(
            "لست مسنداً إلى هذه العيادة في المركز النشط.",
            "DOCTOR_CLINIC_NOT_ASSIGNED",
            403)
    {
    }
}

/// <summary>يُرمى عندما يكون clinicId مفقوداً أو غير صالح لعمليات مساحة عمل العيادة.</summary>
public class InvalidClinicWorkspaceDataException : BusinessException
{
    public InvalidClinicWorkspaceDataException(string message)
        : base(message, "CLINIC_WORKSPACE_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عند نقص أو تعارض بيانات تقييم العيادة أو التأجيل (FR-040 / FR-041).</summary>
public class InvalidClinicAssessmentDataException : BusinessException
{
    public InvalidClinicAssessmentDataException(string message)
        : base(message, "CLINIC_ASSESSMENT_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عند محاولة الحفظ دون قفل تحرير ساري يملكه الطبيب الحالي (FR-045).</summary>
public class ClinicExamEditLockConflictException : BusinessException
{
    public ClinicExamEditLockConflictException(string message = "الفحص قيد التحرير من طبيب آخر أو انتهت مهلة القفل. أعد فتح الملف ثم حاول مجدداً.")
        : base(message, "CLINIC_EXAM_EDIT_LOCK_CONFLICT", 409)
    {
    }
}

/// <summary>يُرمى عند محاولة إنشاء فحص أساسي حالي مكرر لنفس المنتسب والعيادة (FR-045 / BR-009).</summary>
public class DuplicateCurrentMedicalExamException : BusinessException
{
    public DuplicateCurrentMedicalExamException()
        : base(
            "لا يمكن حفظ أكثر من فحص أساسي حالي لنفس المنتسب والعيادة.",
            "DUPLICATE_CURRENT_MEDICAL_EXAM",
            409)
    {
    }
}
