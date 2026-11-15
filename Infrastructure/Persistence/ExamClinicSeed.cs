using Application.Common.ReferenceData;
using Application.Exceptions;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence;

/// <summary>
/// يدرج عيادات الفحص الخمس داخل معاملة واحدة بعد قفل استشاري مشترك بين نسخ الـ API.
/// الصف الموجود بالرمز نفسه لا يُعدَّل. تعارض الترتيب يفشل التهيئة بلا إدراج.
/// </summary>
public static class ExamClinicSeed
{
    /// <summary>مفتاح pg_advisory_xact_lock الثابت لكل نسخ الـ API. لا يُستخدم مفتاح آخر لبذر العيادات.</summary>
    public const int SharedLockClass = 720_240;

    /// <summary>النصف الثاني من مفتاح القفل نفسه.</summary>
    public const int SharedLockKey = 1;

    public static async Task<ExamClinicSeedResult> ApplyAsync(
        AppDbContext context,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync(context, cancellationToken);

        var existing = await context.Clinics.AsNoTracking().ToListAsync(cancellationToken);
        var classification = ExamClinicCatalog.Classify(existing.Select(ToSnapshot));

        if (classification.Conflicts.Count > 0)
        {
            throw new ExamClinicSeedConflictException(
                ExamClinicSeedConflictException.Format(classification.Conflicts, classification.Diagnostics));
        }

        if (classification.ToInsert.Count > 0)
        {
            context.Clinics.AddRange(classification.ToInsert.Select(definition => new Clinic
            {
                ClinicId = Guid.NewGuid(),
                Code = definition.Code,
                NameAr = definition.NameAr,
                SortOrder = definition.SortOrder,
                IsActive = true,
                CountsForFinal = true
            }));

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new ExamClinicSeedConflictException(
                    "تعذر بذر عيادات الفحص بسبب تعارض فهرس فريد على الرمز أو الترتيب. أُلغي الإدراج بالكامل. "
                    + DescribeUniqueViolation(ex),
                    ex);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new ExamClinicSeedResult(classification.Diagnostics);
    }

    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
                return true;

            if (current.Message.Contains("23505", StringComparison.Ordinal)
                || current.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string DescribeUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                var constraint = string.IsNullOrWhiteSpace(postgres.ConstraintName)
                    ? "غير مسمى"
                    : postgres.ConstraintName;
                return $"القيد {constraint} والحالة {postgres.SqlState}.";
            }
        }

        return "انتهاك فهرس فريد على الرمز أو الترتيب.";
    }

    private static async Task AcquireLockAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
            return;

        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            new object[] { SharedLockClass, SharedLockKey },
            cancellationToken);
    }

    private static ExamClinicSnapshot ToSnapshot(Clinic clinic)
        => new(clinic.ClinicId, clinic.Code, clinic.NameAr, clinic.SortOrder);
}
