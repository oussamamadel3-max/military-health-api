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

public class MemberImportException : BusinessException
{
    public MemberImportException(string message, string errorCode = "MEMBER_IMPORT_INVALID", int status = 400)
        : base(message, errorCode, status)
    {
    }
}

public class MemberImportBatchNotFoundException : BusinessException
{
    public MemberImportBatchNotFoundException()
        : base("جلسة الاستيراد غير موجودة.", "MEMBER_IMPORT_NOT_FOUND", 404)
    {
    }
}

public class MemberImportAlreadyCommittedException : BusinessException
{
    public MemberImportAlreadyCommittedException()
        : base("تم اعتماد جلسة الاستيراد مسبقاً ولا يمكن إعادة اعتمادها.", "MEMBER_IMPORT_ALREADY_COMMITTED", 409)
    {
    }
}

public class MemberImportNotPreviewedException : BusinessException
{
    public MemberImportNotPreviewedException()
        : base("جلسة الاستيراد ليست في حالة معاينة صالحة للاعتماد أو الإلغاء.", "MEMBER_IMPORT_NOT_PREVIEWED", 400)
    {
    }
}

public class MemberExportEmptyException : BusinessException
{
    public MemberExportEmptyException()
        : base("لا توجد سجلات مطابقة للتصفية لتصديرها.", "MEMBER_EXPORT_EMPTY", 400)
    {
    }
}
