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

/// <summary>يُرمى عند محاولة تعديل قرار نهائي غير موجود للمنتسب في المركز النشط (FR-056).</summary>
public class FinalDecisionNotFoundException : BusinessException
{
    public FinalDecisionNotFoundException()
        : base(
            "لا يوجد قرار نهائي محفوظ لهذا المنتسب في المركز النشط.",
            "FINAL_DECISION_NOT_FOUND",
            404)
    {
    }
}

/// <summary>يُرمى عند محاولة تعديل القرار بنفس النتيجة دون تغيير فعلي (قيد التاريخ old≠new).</summary>
public class FinalDecisionUnchangedException : BusinessException
{
    public FinalDecisionUnchangedException()
        : base(
            "النتيجة الجديدة مطابقة للنتيجة الحالية؛ اختر نتيجة مختلفة مع سبب التعديل.",
            "FINAL_DECISION_UNCHANGED",
            400)
    {
    }
}

/// <summary>
/// يُرمى عند محاولة تأكيد مراجعة النسخة الحالية لملف سبق تصديره
/// (امتداد تشغيلي لـ FR-058؛ المراجعة موجّهة لملفات غير مصدَّرة).
/// </summary>
public class FinalDecisionReviewBlockedForExportedMemberException : BusinessException
{
    public FinalDecisionReviewBlockedForExportedMemberException()
        : base(
            "لا يمكن تأكيد مراجعة النسخة لملف سبق تصديره؛ عدّل القرار النهائي أولاً لإعادته إلى غير مصدَّر عند الحاجة.",
            "FINAL_DECISION_REVIEW_BLOCKED_EXPORTED",
            400)
    {
    }
}
