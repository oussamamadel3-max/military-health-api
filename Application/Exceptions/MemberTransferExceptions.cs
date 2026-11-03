using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>بيانات نقل غير صالحة (سبب فارغ أو طول زائد أو هدف مفقود).</summary>
public class InvalidMemberTransferDataException : BusinessException
{
    public InvalidMemberTransferDataException(string message)
        : base(message, "MEMBER_TRANSFER_INVALID_DATA", 400)
    {
    }
}

/// <summary>محاولة نقل إلى نفس المركز الحالي.</summary>
public class MemberTransferSameCenterException : BusinessException
{
    public MemberTransferSameCenterException()
        : base(
            "لا يمكن نقل الملف إلى مركزه الحالي. اختر مركزاً هدفاً مختلفاً.",
            "MEMBER_TRANSFER_SAME_CENTER",
            400)
    {
    }
}

/// <summary>الملف غير مؤهل للنقل وفق السياسة المؤقتة (قفل تحرير أو إجراء مؤجل مفتوح).</summary>
public class MemberTransferNotEligibleException : BusinessException
{
    public MemberTransferNotEligibleException(string message)
        : base(message, "MEMBER_TRANSFER_NOT_ELIGIBLE", 409)
    {
    }
}

/// <summary>تعارض تزامن أو قيد فريد أثناء النقل (هوية مفتوحة مكررة / مراجعة بيانات).</summary>
public class MemberTransferConflictException : BusinessException
{
    public MemberTransferConflictException(string message = "تعذر إتمام النقل بسبب تعارض متزامن. أعد المحاولة.")
        : base(message, "MEMBER_TRANSFER_CONFLICT", 409)
    {
    }
}
