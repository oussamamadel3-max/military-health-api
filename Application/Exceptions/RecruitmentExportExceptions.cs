using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عند بيانات/معايير غير صالحة لتصدير التجنيد.</summary>
public class InvalidRecruitmentExportDataException : BusinessException
{
    public InvalidRecruitmentExportDataException(string message, object? meta = null)
        : base(message, "RECRUITMENT_EXPORT_INVALID_DATA", 400, meta)
    {
    }
}

/// <summary>يُرمى عند عدم وجود ملفات مؤهلة لإنشاء دفعة التصدير.</summary>
public class RecruitmentExportEmptyException : BusinessException
{
    public RecruitmentExportEmptyException(string? message = null)
        : base(
            message ?? "لا توجد ملفات جاهزة للتصدير ضمن التصفية الحالية.",
            "RECRUITMENT_EXPORT_EMPTY",
            400)
    {
    }
}

/// <summary>يُرمى عند فشل توليد ملف التصدير دون تعليم المنتسبين كمصدَّرين.</summary>
public class RecruitmentExportGenerationFailedException : BusinessException
{
    public RecruitmentExportGenerationFailedException(Guid exportBatchId, string detail)
        : base(
            "فشل توليد ملف التصدير. لم تُحدَّث حالة تصدير المنتسبين.",
            "RECRUITMENT_EXPORT_GENERATION_FAILED",
            500,
            new { ExportBatchId = exportBatchId, Detail = detail })
    {
    }
}

/// <summary>يُرمى عند طلب دفعة تصدير غير موجودة ضمن المركز النشط.</summary>
public class RecruitmentExportBatchNotFoundException : BusinessException
{
    public RecruitmentExportBatchNotFoundException()
        : base("دفعة التصدير غير موجودة.", "RECRUITMENT_EXPORT_BATCH_NOT_FOUND", 404)
    {
    }
}

/// <summary>يُرمى عند محاولة تنزيل دفعة غير مكتملة أو بلا ملف مولَّد.</summary>
public class RecruitmentExportFileNotReadyException : BusinessException
{
    public RecruitmentExportFileNotReadyException()
        : base(
            "ملف التصدير غير جاهز للتنزيل. الدفعة غير مكتملة أو فشل توليدها.",
            "RECRUITMENT_EXPORT_FILE_NOT_READY",
            400)
    {
    }
}
