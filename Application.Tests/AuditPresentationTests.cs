using System.Text.Json;
using Application.Common.Localization;
using Domain.Entities.Audit;

namespace Application.Tests;

public class AuditPresentationTests
{
    [Fact]
    public void Complete_snapshot_uses_arabic_labels_and_the_historical_name()
    {
        var display = AuditPresentation.Present(new AuditEvent
        {
            EventType = "Modified",
            EntityType = "User",
            Result = "SUCCESS",
            ReasonText = "الخصائص التي جرى تعديل عليها: IsActive",
            OldValues = JsonSerializer.Serialize(new { IsActive = false, NameAr = "الاسم وقت الحدث" }),
            NewValues = JsonSerializer.Serialize(new { IsActive = true, NameAr = "الاسم وقت الحدث" })
        });

        Assert.Equal("المستخدم", display.EntityLabel);
        Assert.Equal("تعديل", display.EventLabel);
        Assert.Equal("ناجح", display.ResultLabel);
        Assert.Equal("الاسم وقت الحدث", display.EntityDisplayName);
        Assert.Equal("الحالة: لا ← نعم", display.ChangedValuesSummary);
        Assert.Equal("الحالة", display.ChangedFields);
    }
}
