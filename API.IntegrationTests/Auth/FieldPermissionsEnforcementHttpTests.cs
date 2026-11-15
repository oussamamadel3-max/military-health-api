using System.Net;
using System.Text.Json;
using API.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

/// <summary>Phase C: إنفاذ MotherName عبر HTTP مع العلم مفعّلاً في مصنع معزول.</summary>
[Collection(FieldPermissionsEnforcementHttpCollection.Name)]
public sealed class FieldPermissionsEnforcementHttpTests
{
    private readonly FieldPermissionsEnforcementWebApplicationFactory _factory;

    public FieldPermissionsEnforcementHttpTests(FieldPermissionsEnforcementWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task GetById_masks_MotherName_for_None_and_Write_users()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);

        foreach (var userName in new[]
                 {
                     FieldPermissionsEnforcementHttpSeed.NoneUserName,
                     FieldPermissionsEnforcementHttpSeed.WriterUserName
                 })
        {
            var session = await LoginAndSelectCenterAsync(userName);
            var response = await session.Client.GetAsync(
                $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = await session.ReadJsonAsync(response);
            var data = doc.RootElement.GetProperty("data");
            Assert.False(data.TryGetProperty("motherName", out var mother) &&
                         mother.ValueKind == JsonValueKind.String &&
                         mother.GetString() == FieldPermissionsEnforcementHttpSeed.SeededMotherName);
        }

        var stored = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .Select(m => m.MotherName)
                .SingleAsync());
        Assert.Equal(FieldPermissionsEnforcementHttpSeed.SeededMotherName, stored);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task GetById_reveals_MotherName_for_Read_and_ReadWrite()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);

        foreach (var userName in new[]
                 {
                     FieldPermissionsEnforcementHttpSeed.ReaderUserName,
                     FieldPermissionsEnforcementHttpSeed.ReadWriteUserName
                 })
        {
            var session = await LoginAndSelectCenterAsync(userName);
            var response = await session.Client.GetAsync(
                $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var doc = await session.ReadJsonAsync(response);
            Assert.Equal(
                FieldPermissionsEnforcementHttpSeed.SeededMotherName,
                doc.RootElement.GetProperty("data").GetProperty("motherName").GetString());
        }
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_MotherName_forbidden_for_Read_only_user()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(FieldPermissionsEnforcementHttpSeed.ReaderUserName);

        var get = await session.Client.GetAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var details = await session.ReadJsonAsync(get);
        var data = details.RootElement.GetProperty("data");

        var payload = BuildUpdatePayload(
            data,
            motherName: "أم معدّلة عبر HTTP");
        var update = await session.PutRawJsonAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}",
            payload);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);

        var stored = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .Select(m => m.MotherName)
                .SingleAsync());
        Assert.Equal(FieldPermissionsEnforcementHttpSeed.SeededMotherName, stored);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_other_fields_allowed_when_MotherName_unchanged_for_None_user()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(FieldPermissionsEnforcementHttpSeed.NoneUserName);

        var get = await session.Client.GetAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var details = await session.ReadJsonAsync(get);
        var data = details.RootElement.GetProperty("data");

        var payload = BuildUpdatePayload(
            data,
            motherName: FieldPermissionsEnforcementHttpSeed.SeededMotherName,
            fullName: "اسم محدّث دون لمس الأم");
        var update = await session.PutRawJsonAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}",
            payload);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var stored = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .SingleAsync());
        Assert.Equal("اسم محدّث دون لمس الأم", stored.FullName);
        Assert.Equal(FieldPermissionsEnforcementHttpSeed.SeededMotherName, stored.MotherName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_with_MotherName_forbidden_for_Read_only_user()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(FieldPermissionsEnforcementHttpSeed.ReaderUserName);

        var payload = JsonSerializer.Serialize(new
        {
            fullName = "منتسب جديد",
            motherName = "أم جديدة",
            birthDate = "2000-01-01",
            maritalStatusId = 1,
            occupation = "طالب",
            recruitmentNumber = $"HTTP-FP-C-{Guid.NewGuid():N}"[..20],
            recruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            bloodTypeId = 1,
            heightCm = 170,
            weightKg = 65
        });
        var response = await session.PostRawJsonAsync("/api/members", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = await session.ReadJsonAsync(response);
        Assert.Equal("FORBIDDEN_ACCESS", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Module_View_still_required_when_field_ReadWrite_present()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);

        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        var response = await session.Client.GetAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC1")]
    public async Task Enforcement_factory_has_flag_enabled_and_masks_without_db_mutation()
    {
        await FieldPermissionsEnforcementHttpSeed.EnsureAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(FieldPermissionsEnforcementHttpSeed.NoneUserName);

        var before = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .Select(m => m.MotherName)
                .SingleAsync());

        var response = await session.Client.GetAsync(
            $"/api/members/{FieldPermissionsEnforcementHttpSeed.SeededMemberId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await session.ReadJsonAsync(response);
        var mother = doc.RootElement.GetProperty("data").GetProperty("motherName");
        Assert.True(mother.ValueKind is JsonValueKind.Null or JsonValueKind.String
            && string.IsNullOrEmpty(mother.ValueKind == JsonValueKind.String ? mother.GetString() : null));

        var after = await _factory.WithDbAsync(db =>
            db.Members.AsNoTracking()
                .Where(m => m.MemberId == FieldPermissionsEnforcementHttpSeed.SeededMemberId)
                .Select(m => m.MotherName)
                .SingleAsync());
        Assert.Equal(before, after);
        Assert.Equal(FieldPermissionsEnforcementHttpSeed.SeededMotherName, after);
    }

    private async Task<AuthHttpSession> LoginAndSelectCenterAsync(string userName)
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(userName, FieldPermissionsEnforcementHttpSeed.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await session.SelectCenterAsync(FieldPermissionsEnforcementHttpSeed.CenterB)).StatusCode);
        return session;
    }

    private static string BuildUpdatePayload(
        JsonElement data,
        string motherName,
        string? fullName = null)
    {
        string ReqString(string name, string? fallback = null)
        {
            if (data.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString()!;
            return fallback ?? string.Empty;
        }

        var body = new Dictionary<string, object?>
        {
            ["fullName"] = fullName ?? ReqString("fullName", "منتسب"),
            ["motherName"] = motherName,
            ["birthDate"] = ReqString("birthDate", "2001-02-02"),
            ["maritalStatusId"] = data.TryGetProperty("maritalStatusId", out var ms) ? ms.GetInt16() : (short)1,
            ["occupation"] = ReqString("occupation", "طالب"),
            ["recruitmentNumber"] = ReqString("recruitmentNumber", "HTTP-FP-C-001"),
            ["recruitmentCenterId"] = data.TryGetProperty("recruitmentCenterId", out var rc)
                ? rc.GetGuid()
                : Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ["bloodTypeId"] = data.TryGetProperty("bloodTypeId", out var bt) && bt.ValueKind != JsonValueKind.Null
                ? bt.GetInt16()
                : (short)1,
            ["bloodSugarMgDl"] = null,
            ["heightCm"] = data.TryGetProperty("heightCm", out var h) && h.ValueKind != JsonValueKind.Null
                ? h.GetDecimal()
                : null,
            ["weightKg"] = data.TryGetProperty("weightKg", out var w) && w.ValueKind != JsonValueKind.Null
                ? w.GetDecimal()
                : null,
            ["bloodPressure"] = data.TryGetProperty("bloodPressure", out var bp) && bp.ValueKind != JsonValueKind.Null
                ? bp.GetString()
                : null,
            ["pulseBpm"] = data.TryGetProperty("pulseBpm", out var pulse) && pulse.ValueKind != JsonValueKind.Null
                ? pulse.GetInt16()
                : null,
            ["hasTattoo"] = data.TryGetProperty("hasTattoo", out var tattoo) && tattoo.ValueKind == JsonValueKind.True,
            ["distinguishingMarks"] =
                data.TryGetProperty("distinguishingMarks", out var marks) && marks.ValueKind != JsonValueKind.Null
                    ? marks.GetString()
                    : null
        };
        return JsonSerializer.Serialize(body);
    }
}
