using Domain.Exceptions;

namespace Application.Exceptions;

public class MemberNotFoundException : BusinessException
{
    public MemberNotFoundException(string message = "ملف المنتسب غير موجود.")
        : base(message, "MEMBER_NOT_FOUND", 404)
    {
    }
}

public class InvalidMemberDataException : BusinessException
{
    public InvalidMemberDataException(string message)
        : base(message, "MEMBER_INVALID_DATA", 400)
    {
    }
}

public class MemberRecruitmentNumberExistsException : BusinessException
{
    public MemberRecruitmentNumberExistsException(string recruitmentNumber = "")
        : base(
            string.IsNullOrWhiteSpace(recruitmentNumber)
                ? "رقم المنتسب/التجنيد مسجّل مسبقاً."
                : $"رقم المنتسب/التجنيد '{recruitmentNumber}' مسجّل مسبقاً.",
            "MEMBER_RECRUITMENT_NUMBER_EXISTS",
            400)
    {
    }
}

public class MemberPhysicalDeleteNotAllowedException : BusinessException
{
    public MemberPhysicalDeleteNotAllowedException()
        : base(
            "لا يُسمح بالحذف النهائي لملف المنتسب.",
            "MEMBER_DELETE_FORBIDDEN",
            400)
    {
    }
}
