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

/// <summary>يُرمى عند نقص أو تعارض بيانات فحص العيون أو الانكسار (FR-033 / FR-034).</summary>
public class InvalidEyeExamDataException : BusinessException
{
    public InvalidEyeExamDataException(string message)
        : base(message, "EYE_EXAM_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عندما لا يُوجد فحص عيون محفوظ للمنتسب في العيادة المطلوبة.</summary>
public class EyeExamNotFoundException : BusinessException
{
    public EyeExamNotFoundException()
        : base("لا يوجد فحص عيون مسجّل لهذا المنتسب في العيادة.", "EYE_EXAM_NOT_FOUND", 404)
    {
    }
}

/// <summary>يُرمى عند نقص أو تعارض بيانات فحص الباطنة (FR-035).</summary>
public class InvalidInternalExamDataException : BusinessException
{
    public InvalidInternalExamDataException(string message)
        : base(message, "INTERNAL_EXAM_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عندما لا يُوجد فحص باطنة محفوظ للمنتسب في العيادة المطلوبة.</summary>
public class InternalExamNotFoundException : BusinessException
{
    public InternalExamNotFoundException()
        : base("لا يوجد فحص باطنة مسجّل لهذا المنتسب في العيادة.", "INTERNAL_EXAM_NOT_FOUND", 404)
    {
    }
}

/// <summary>يُرمى عند نقص أو تعارض بيانات فحص الجراحة (FR-036).</summary>
public class InvalidSurgeryExamDataException : BusinessException
{
    public InvalidSurgeryExamDataException(string message)
        : base(message, "SURGERY_EXAM_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عندما لا يُوجد فحص جراحة محفوظ للمنتسب في العيادة المطلوبة.</summary>
public class SurgeryExamNotFoundException : BusinessException
{
    public SurgeryExamNotFoundException()
        : base("لا يوجد فحص جراحة مسجّل لهذا المنتسب في العيادة.", "SURGERY_EXAM_NOT_FOUND", 404)
    {
    }
}

/// <summary>يُرمى عند نقص أو تعارض بيانات فحص العظمية (FR-037).</summary>
public class InvalidOrthopedicExamDataException : BusinessException
{
    public InvalidOrthopedicExamDataException(string message)
        : base(message, "ORTHOPEDIC_EXAM_INVALID_DATA", 400)
    {
    }
}

/// <summary>يُرمى عندما لا يُوجد فحص عظمية محفوظ للمنتسب في العيادة المطلوبة.</summary>
public class OrthopedicExamNotFoundException : BusinessException
{
    public OrthopedicExamNotFoundException()
        : base("لا يوجد فحص عظمية مسجّل لهذا المنتسب في العيادة.", "ORTHOPEDIC_EXAM_NOT_FOUND", 404)
    {
    }
}
