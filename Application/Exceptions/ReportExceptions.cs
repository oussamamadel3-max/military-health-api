using Domain.Exceptions;

namespace Application.Exceptions;

// ==========================================
// Report Domain Custom Exceptions
// ==========================================

public class ReportNotFoundException : BusinessException
{
    public ReportNotFoundException(string message = "التقرير غير موجود.")
        : base(
            message: message,
            errorCode: "REPORT_NOT_FOUND",
            status: 404)
    {
    }
}

public class InvalidReportFilterException : BusinessException
{
    public InvalidReportFilterException(string message = "فلاتر التقرير غير صالحة.")
        : base(
            message: message,
            errorCode: "REPORT_INVALID_FILTER",
            status: 400)
    {
    }
}
