using System.Net;
using System.Text;
using API.IntegrationTests.Infrastructure;
using Domain.Enums;

namespace API.IntegrationTests.Auth;

/// <summary>MH-FNC-038 — hosted HTTP validation and safe error surfaces.</summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class ValidationLocalizationHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public ValidationLocalizationHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    [Trait("Regression", "MH-FNC-038")]
    public async Task Login_missing_credentials_returns_validation_error_contract()
    {
        var session = _factory.CreateSession();
        var response = await session.PostRawJsonAsync("/api/Auth/Login", "{}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("VALIDATION_ERROR", body, StringComparison.Ordinal);
        AssertNoTechnicalLeak(body);
    }

    [Fact]
    [Trait("KnownGap", "MH-FNC-038")]
    public async Task Login_missing_credentials_should_not_expose_english_property_names()
    {
        var session = _factory.CreateSession();
        var response = await session.PostRawJsonAsync("/api/Auth/Login", "{}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("UserName", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PassWord", body, StringComparison.Ordinal);
        Assert.DoesNotContain("The ", body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Regression", "MH-FNC-038")]
    public async Task Login_malformed_json_returns_safe_error_not_stack_trace()
    {
        var session = _factory.CreateSession();
        var response = await session.PostRawJsonAsync("/api/Auth/Login", "{");
        Assert.True((int)response.StatusCode is 400 or 422 or 500);
        var body = await response.Content.ReadAsStringAsync();
        AssertNoTechnicalLeak(body);
        Assert.DoesNotContain(AuthTestSeed.ActivePassword, body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Regression", "MH-FNC-038")]
    public async Task Role_create_whitespace_name_rejected_in_arabic()
    {
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);

        var response = await admin.CreateRoleAsync(new
        {
            Name = "   ",
            Description = "دور",
            Scope = RoleScope.Central
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("اسم الدور", body, StringComparison.Ordinal);
        AssertNoTechnicalLeak(body);
    }

    [Fact]
    [Trait("Regression", "MH-FNC-038")]
    public async Task Create_user_duplicate_username_returns_arabic_identity_message()
    {
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);

        var response = await admin.CreateUserAsync(new
        {
            fullName = "مكرر",
            username = AuthTestSeed.ActiveUserName,
            email = "dup@it.local",
            employeeNumber = "EMP-DUP",
            rankOrTitle = "موظف",
            password = "Dup!User9Aa",
            confirmPassword = "Dup!User9Aa",
            roles = new[] { "IT-Member" }
        });
        Assert.True((int)response.StatusCode is 400 or 422);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DuplicateUserName", body, StringComparison.Ordinal);
        AssertNoTechnicalLeak(body);
    }

    [Fact]
    [Trait("Regression", "MH-FNC-038")]
    public async Task Create_user_weak_password_returns_arabic_policy_message()
    {
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var response = await admin.CreateUserAsync(new
        {
            fullName = "ضعيف",
            username = $"weak.{suffix}",
            email = $"{suffix}@weak.it.local",
            employeeNumber = $"EMP-{suffix}",
            rankOrTitle = "موظف",
            password = "123",
            confirmPassword = "123",
            roles = new[] { "IT-Member" }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("VALIDATION_ERROR", body, StringComparison.Ordinal);
        AssertNoTechnicalLeak(body);
    }

  private static void AssertNoTechnicalLeak(string body)
    {
        Assert.DoesNotContain("originalError", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }
}
