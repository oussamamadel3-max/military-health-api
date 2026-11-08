using Application.Dtos.Centers;
using Domain.Exceptions;

namespace Application.Exceptions;

public class HistoricalMigrationTargetInvalidException : BusinessException
{
    public HistoricalMigrationTargetInvalidException(string message)
        : base(message, "HISTORICAL_MIGRATION_TARGET_INVALID", 400)
    {
    }
}

public class HistoricalMigrationBlockedException : BusinessException
{
    public HistoricalMigrationBlockedException(HistoricalMigrationReportDto report)
        : base(
            "تعذر الترحيل لوجود سجلات لا يمكن نسبتها آلياً. عالجها يدوياً ثم أعد التنفيذ.",
            "HISTORICAL_MIGRATION_BLOCKED",
            409,
            report)
    {
    }
}

public class HistoricalMigrationVerificationFailedException : BusinessException
{
    public HistoricalMigrationVerificationFailedException(HistoricalMigrationReportDto report)
        : base(
            "التحقق لم يكتمل لبقاء سجلات تشغيلية بلا مركز صالح.",
            "HISTORICAL_MIGRATION_VERIFICATION_FAILED",
            409,
            report)
    {
    }
}

public class MultiCenterActivationBlockedException : BusinessException
{
    public MultiCenterActivationBlockedException()
        : base(
            "لا يمكن تفعيل مركز صحي إضافي قبل اكتمال ترحيل السجلات التاريخية والتحقق من نتيجته.",
            "MULTI_CENTER_ACTIVATION_BLOCKED",
            409)
    {
    }
}
