using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عند نقص أو تعارض بيانات إصدار القرار النهائي (FR-053 / FR-054).</summary>
public class InvalidFinalDecisionDataException : BusinessException
{
    public InvalidFinalDecisionDataException(string message, object? meta = null)
        : base(message, "FINAL_DECISION_INVALID_DATA", 400, meta)
    {
    }
}

/// <summary>يُرمى عند محاولة إصدار قرار قبل اكتمال تقييمات العيادات الإلزامية.</summary>
public class IncompleteClinicAssessmentsException : BusinessException
{
    public IncompleteClinicAssessmentsException(IReadOnlyList<string> missingClinicCodes)
        : base(
            "لا يمكن إصدار القرار النهائي قبل اكتمال تقييمات العيادات الإلزامية.",
            "FINAL_DECISION_INCOMPLETE_CLINICS",
            400,
            new { MissingClinicCodes = missingClinicCodes })
    {
    }
}

/// <summary>يُرمى عند وجود قرار نهائي سابق لنفس المنتسب (BR-016).</summary>
public class FinalDecisionAlreadyExistsException : BusinessException
{
    public FinalDecisionAlreadyExistsException()
        : base(
            "يوجد قرار نهائي سابق لهذا المنتسب؛ يُعدَّل القرار القائم وفق الصلاحية ولا يُنشأ قرار جديد.",
            "FINAL_DECISION_ALREADY_EXISTS",
            409)
    {
    }
}

/// <summary>يُرمى عند اختلاف القرار عن تقييم عيادة دون مبرر (FR-054 / BR-066).</summary>
public class FinalDecisionDisagreementJustificationRequiredException : BusinessException
{
    public FinalDecisionDisagreementJustificationRequiredException(IReadOnlyList<string> disagreeingClinicCodes)
        : base(
            "يجب تسجيل مبرر الاختلاف عند تعارض القرار النهائي مع تقييم إحدى العيادات.",
            "FINAL_DECISION_DISAGREEMENT_JUSTIFICATION_REQUIRED",
            400,
            new { DisagreeingClinicCodes = disagreeingClinicCodes })
    {
    }
}

/// <summary>يُرمى عند تعارض مراجعة المنتسب بين جلستين أثناء الاعتماد.</summary>
public class FinalDecisionConcurrencyException : BusinessException
{
    public FinalDecisionConcurrencyException()
        : base(
            "تم تعديل ملف المنتسب من جلسة أخرى. أعد فتح الملف ثم حاول إصدار القرار مجدداً.",
            "FINAL_DECISION_CONCURRENCY_CONFLICT",
            409)
    {
    }
}
