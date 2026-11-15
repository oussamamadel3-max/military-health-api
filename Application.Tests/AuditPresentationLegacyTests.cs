using System.Text.Json;
using Application.Common.Localization;
using Domain.Entities.Audit;

namespace Application.Tests;

public class AuditPresentationLegacyTests
{
    [Fact]
    public void Missing_snapshots_do_not_invent_a_name_or_summary()
    {
        var display = AuditPresentation.Present(new AuditEvent
        {
            EventType = "Logout",
            EntityType = "User",
            Result = "SUCCESS",
            EntityId = Guid.NewGuid().ToString()
        });

        Assert.Null(display.EntityDisplayName);
        Assert.Null(display.ChangedValuesSummary);
        Assert.Null(display.ChangedFields);
        Assert.Equal("تسجيل خروج", display.EventLabel);
    }

    [Fact]
    public void Unknown_fields_and_sensitive_values_are_omitted_before_the_summary()
    {
        var display = AuditPresentation.Present(new AuditEvent
        {
            EventType = "Modified",
            EntityType = "User",
            Result = "SUCCESS",
            ReasonText = "الخصائص التي جرى تعديل عليها: IsActive, PasswordHash, UnknownField",
            OldValues = JsonSerializer.Serialize(new
            {
                IsActive = true,
                PasswordHash = "old-secret",
                UnknownField = "legacy"
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                IsActive = false,
                PasswordHash = "new-secret",
                UnknownField = "changed"
            })
        });

        Assert.Equal("الحالة: نعم ← لا", display.ChangedValuesSummary);
        Assert.Equal("الحالة", display.ChangedFields);
        Assert.DoesNotContain("secret", display.ChangedValuesSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UnknownField", display.ExportSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", display.ExportSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_event_and_value_do_not_throw()
    {
        var display = AuditPresentation.Present(new AuditEvent
        {
            EventType = "NotInCatalog",
            EntityType = "NotAnEntity",
            Result = "MAYBE",
            NewValues = JsonSerializer.Serialize(new { NameAr = "اسم محفوظ", IsActive = "NOT-A-BOOL" })
        });

        Assert.Null(display.EntityLabel);
        Assert.Null(display.EventLabel);
        Assert.Null(display.ResultLabel);
        Assert.Equal("اسم محفوظ", display.EntityDisplayName);
        Assert.NotNull(display.ChangedValuesSummary);
        Assert.Contains("—", display.ChangedValuesSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT-A-BOOL", display.ChangedValuesSummary, StringComparison.Ordinal);
    }
}
