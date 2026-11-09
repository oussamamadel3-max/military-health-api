using System.Text.Json;
using API.Extensions;
using API.Validation;
using Application.Services.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Tests;

public class BusinessClockAndValidationMessageTests
{
    private static BusinessClock PlusThree()
        => new(TimeZoneInfo.CreateCustomTimeZone("Test-Plus3", TimeSpan.FromHours(3), "Test-Plus3", "Test-Plus3"));

    [Fact]
    public void Production_clock_uses_the_process_local_zone()
    {
        var clock = new BusinessClock();
        var localMidnight = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(TimeZoneInfo.Local.Id, clock.TimeZone.Id);
        Assert.True(clock.TimeZone.HasSameRules(TimeZoneInfo.Local));
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(localMidnight), clock.StartOfDay(new DateOnly(2026, 11, 9)).Offset);
    }

    [Fact]
    public void Dashboard_range_and_day_window_match_for_the_same_calendar_date()
    {
        var clock = PlusThree();
        var day = new DateOnly(2026, 11, 9);
        var from = new DateTime(2026, 11, 9);
        var (start, end) = clock.NormalizeDateTimeRange(from, from);

        Assert.Equal(clock.StartOfDay(day), start);
        Assert.Equal(clock.EndOfDayExclusive(day), end);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Instant_just_after_business_midnight_belongs_to_that_business_day()
    {
        var clock = PlusThree();
        var instant = new DateTimeOffset(2026, 11, 9, 0, 30, 0, TimeSpan.FromHours(3));
        var day = new DateOnly(2026, 11, 9);

        Assert.True(instant >= clock.StartOfDay(day) && instant < clock.EndOfDayExclusive(day));
        Assert.False(instant >= BusinessClock.Utc.StartOfDay(day) && instant < BusinessClock.Utc.EndOfDayExclusive(day));
        Assert.Equal("2026/11/10 00:30", clock.Format(
            new DateTimeOffset(2026, 11, 9, 21, 30, 0, TimeSpan.Zero),
            "yyyy/MM/dd HH:mm"));
    }

    [Fact]
    public void Utc_clock_keeps_calendar_midnight_on_utc()
    {
        var day = new DateOnly(2026, 11, 9);
        var start = BusinessClock.Utc.StartOfDay(day);
        var end = BusinessClock.Utc.EndOfDayExclusive(day);

        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), end);
        Assert.Equal(TimeSpan.FromDays(1), end - start);
    }

    [Fact]
    public void Fixed_plus_three_window_is_converted_to_utc_before_comparison()
    {
        var clock = PlusThree();
        var day = new DateOnly(2026, 11, 9);
        var start = clock.StartOfDay(day);

        Assert.Equal(TimeSpan.FromHours(3), start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 0, 0, TimeSpan.Zero), start.ToUniversalTime());
    }

    [Fact]
    public void Daylight_saving_transition_changes_the_day_length()
    {
        var clock = new BusinessClock(CreateCentralEuropeanZone());
        var winter = clock.StartOfDay(new DateOnly(2026, 1, 15));
        var summer = clock.StartOfDay(new DateOnly(2026, 7, 15));
        var spring = new DateOnly(2026, 3, 29);
        var autumn = new DateOnly(2026, 10, 25);

        Assert.Equal(TimeSpan.FromHours(1), winter.Offset);
        Assert.Equal(TimeSpan.FromHours(2), summer.Offset);
        Assert.Equal(TimeSpan.FromHours(23), clock.EndOfDayExclusive(spring) - clock.StartOfDay(spring));
        Assert.Equal(TimeSpan.FromHours(25), clock.EndOfDayExclusive(autumn) - clock.StartOfDay(autumn));
        Assert.NotEqual(winter.Offset, summer.Offset);
    }

    [Fact]
    public void Formatting_does_not_change_the_stored_instant()
    {
        var clock = PlusThree();
        var stored = new DateTimeOffset(2026, 11, 9, 21, 30, 0, TimeSpan.Zero);
        var utc = stored.UtcDateTime;

        var text = clock.Format(stored, "yyyy/MM/dd HH:mm");

        Assert.Equal("2026/11/10 00:30", text);
        Assert.Equal(utc, stored.UtcDateTime);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 21, 30, 0, TimeSpan.Zero), stored);
    }

    private static TimeZoneInfo CreateCentralEuropeanZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2035, 12, 31),
            TimeSpan.FromHours(1),
            start,
            end);
        return TimeZoneInfo.CreateCustomTimeZone(
            "Test-CET",
            TimeSpan.FromHours(1),
            "Test-CET",
            "Test-Standard",
            "Test-Daylight",
            [rule]);
    }

    [Theory]
    [InlineData("fullName", "The FullName field is required.", "الحقل fullName مطلوب.")]
    [InlineData("heightCm", "", "قيمة الحقل heightCm غير صالحة.")]
    [InlineData("pageSize", "The value 'abc' is not valid.", "قيمة الحقل pageSize غير صالحة.")]
    [InlineData("$.birthDate", "The JSON value could not be converted to System.DateOnly. Path: $.birthDate", "قيمة الحقل birthDate غير صالحة.")]
    [InlineData("email", "The email field is not a valid e-mail address.", "قيمة الحقل email ليست بريداً إلكترونياً صالحاً.")]
    [InlineData("roles", "يجب اختيار دور واحد على الأقل.", "يجب اختيار دور واحد على الأقل.")]
    public void Model_messages_become_arabic_without_framework_types(string key, string raw, string expected)
    {
        var localized = ValidationErrorText.Localize(key, raw);

        Assert.Equal(expected, localized);
        Assert.DoesNotContain("System.", localized);
        Assert.DoesNotContain("Invalid value.", localized);
        Assert.DoesNotContain("Validation error", localized);
    }

    [Fact]
    public void Invalid_model_state_factory_keeps_422_contract_and_arabic_text()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiValidationBehavior();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiBehaviorOptions>>().Value;
        var context = new ActionContext(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        context.ModelState.AddModelError("fullName", "The FullName field is required.");
        context.ModelState.AddModelError("heightCm", "");

        var result = Assert.IsType<UnprocessableEntityObjectResult>(options.InvalidModelStateResponseFactory(context));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var error = document.RootElement.GetProperty("error");
        var fields = error.GetProperty("meta").GetProperty("errors");

        Assert.Equal(422, result.StatusCode);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal(ValidationErrorText.EnvelopeMessage, error.GetProperty("message").GetString());
        Assert.Equal("الحقل fullName مطلوب.", fields.GetProperty("fullName")[0].GetString());
        Assert.Equal("قيمة الحقل heightCm غير صالحة.", fields.GetProperty("heightCm")[0].GetString());
    }
}
