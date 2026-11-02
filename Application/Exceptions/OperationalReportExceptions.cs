using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عند فلاتر تقارير تشغيلية غير صالحة.</summary>
public class InvalidOperationalReportFilterException : BusinessException
{
    public InvalidOperationalReportFilterException(string message, object? meta = null)
        : base(message, "OPERATIONAL_REPORT_INVALID_FILTER", 400, meta)
    {
    }
}

/// <summary>يُرمى عند محاولة إطلاق تقرير مركزي بوجود سجلات بلا مركز معتمد.</summary>
public class OperationalReportUnlinkedCenterException : BusinessException
{
    public OperationalReportUnlinkedCenterException(int orphanCount)
        : base(
            "لا يمكن إطلاق التقرير المركزي لوجود سجلات غير مرتبطة بمركز معتمد.",
            "OPERATIONAL_REPORT_UNLINKED_CENTER",
            400,
            new { OrphanCount = orphanCount })
    {
    }
}

/// <summary>يُرمى عند محاولة مستخدم محلي طلب تقرير مركزي.</summary>
public class OperationalReportCentralForbiddenException : BusinessException
{
    public OperationalReportCentralForbiddenException()
        : base(
            "التقارير المركزية تتطلب صلاحية مركزية صريحة.",
            "OPERATIONAL_REPORT_CENTRAL_FORBIDDEN",
            403)
    {
    }
}

/// <summary>يُرمى عند تصدير تقرير بلا نتائج.</summary>
public class OperationalReportExportEmptyException : BusinessException
{
    public OperationalReportExportEmptyException()
        : base("لا توجد بيانات ضمن التصفية الحالية.", "OPERATIONAL_REPORT_EXPORT_EMPTY", 400)
    {
    }
}
