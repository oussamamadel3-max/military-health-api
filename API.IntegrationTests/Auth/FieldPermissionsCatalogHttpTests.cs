using System.Net;
using System.Text.Json;
using API.IntegrationTests.Infrastructure;

namespace API.IntegrationTests.Auth;

/// <summary>Phase B: كتالوج Member/MotherName + رفض خارج الكتالوج + CRUD إداري دون إنفاذ على Members.</summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class FieldPermissionsCatalogHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public FieldPermissionsCatalogHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task GetEntities_returns_Member_only()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.Client.GetAsync("/api/FieldPermissions/GetEntities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await admin.ReadJsonAsync(response);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("Member", data[0].GetProperty("name").GetString());
        Assert.Equal("المنتسب", data[0].GetProperty("displayName").GetString());
    }

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task GetFields_returns_MotherName_only()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.Client.GetAsync("/api/FieldPermissions/GetFields/Member");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await admin.ReadJsonAsync(response);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("MotherName", data[0].GetProperty("name").GetString());
        Assert.Equal("اسم الأم", data[0].GetProperty("displayName").GetString());
    }

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task Create_rejects_unapproved_field_on_Member()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.PostRawJsonAsync(
            "/api/FieldPermissions/Create",
            """
            {
              "entityName": "Member",
              "fieldNames": ["BirthDate"],
              "accessType": 1,
              "roleIds": [],
              "userIds": []
            }
            """);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("غير معتمدة", body, StringComparison.Ordinal);
    }

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task Create_rejects_unapproved_entity()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.PostRawJsonAsync(
            "/api/FieldPermissions/Create",
            """
            {
              "entityName": "FinalDecision",
              "fieldNames": ["MotherName"],
              "accessType": 1,
              "roleIds": [],
              "userIds": []
            }
            """);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("غير معتمد في كتالوج", body, StringComparison.Ordinal);
    }

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task Create_update_list_delete_MotherName_permission_roundtrip()
    {
        var admin = await LoginAdminAsync();

        var create = await admin.PostRawJsonAsync(
            "/api/FieldPermissions/Create",
            """
            {
              "entityName": "Member",
              "fieldNames": ["MotherName"],
              "accessType": 1,
              "roleIds": [],
              "userIds": []
            }
            """);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        Guid permissionId;
        using (var createdDoc = await admin.ReadJsonAsync(create))
        {
            var permissions = createdDoc.RootElement.GetProperty("data").GetProperty("permissions");
            Assert.Equal(1, permissions.GetArrayLength());
            Assert.Equal("Member", permissions[0].GetProperty("entityName").GetString());
            Assert.Equal("MotherName", permissions[0].GetProperty("fieldName").GetString());
            Assert.Equal(1, permissions[0].GetProperty("accessType").GetInt32());
            permissionId = permissions[0].GetProperty("id").GetGuid();
        }

        var list = await admin.Client.GetAsync("/api/FieldPermissions/GetFieldPermissions?entityName=Member&fieldName=MotherName");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var listDoc = await admin.ReadJsonAsync(list))
        {
            var items = listDoc.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(items.GetArrayLength() >= 1);
            Assert.Contains(items.EnumerateArray(), e => e.GetProperty("id").GetGuid() == permissionId);
        }

        var update = await admin.PutRawJsonAsync(
            $"/api/FieldPermissions/Update/{permissionId}",
            """
            {
              "accessType": 3,
              "roleIds": [],
              "userIds": []
            }
            """);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var byId = await admin.Client.GetAsync($"/api/FieldPermissions/GetById/{permissionId}");
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        using (var byIdDoc = await admin.ReadJsonAsync(byId))
        {
            Assert.Equal(3, byIdDoc.RootElement.GetProperty("data").GetProperty("accessType").GetInt32());
        }

        var delete = await admin.DeleteRawAsync($"/api/FieldPermissions/Delete/{permissionId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var missing = await admin.Client.GetAsync($"/api/FieldPermissions/GetById/{permissionId}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact(Skip = "Field Permissions admin API temporarily disabled")]
    [Trait("Feature", "FieldPermissions-PhaseB")]
    public async Task GetFieldPermissions_without_permission_is_forbidden()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        var response = await session.Client.GetAsync("/api/FieldPermissions/GetEntities");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<AuthHttpSession> LoginAdminAsync()
    {
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        return admin;
    }
}
