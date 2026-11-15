using Application.Attributes;
using Application.Common.Permissions;
using Application.Exceptions;
using Domain.Attributes;

namespace Application.Tests;

public class FieldPermissionCatalogTests
{
    [Fact]
    public void Production_catalog_registers_Member_MotherName_only_in_phase_B()
    {
        var catalog = new FieldPermissionCatalog();

        var entities = catalog.GetEntities();
        Assert.Single(entities);
        Assert.Equal("Member", entities[0].Name);
        Assert.Equal("المنتسب", entities[0].DisplayName);

        var fields = catalog.GetFields("Member");
        Assert.Single(fields);
        Assert.Equal("MotherName", fields[0].Name);
        Assert.Equal("اسم الأم", fields[0].DisplayName);

        Assert.True(catalog.IsFieldRegistered("Member", "MotherName"));
        Assert.False(catalog.IsFieldRegistered("Member", "BirthDate"));
        Assert.False(catalog.IsFieldRegistered("Member", "FullName"));
        Assert.False(catalog.IsEntityRegistered("FinalDecision"));
    }

    [Fact]
    public void Catalog_includes_only_annotated_entity_and_explicit_dto_fields()
    {
        var catalog = new FieldPermissionCatalog(
            typeof(CatalogProbeEntity).Assembly,
            typeof(CatalogProbeEntityDto).Assembly);

        var entities = catalog.GetEntities();
        Assert.Contains(entities, e => e.Name == nameof(CatalogProbeEntity));
        Assert.Equal("كيان تجريبي", entities.Single(e => e.Name == nameof(CatalogProbeEntity)).DisplayName);

        var fields = catalog.GetFields(nameof(CatalogProbeEntity));
        Assert.Single(fields);
        Assert.Equal("SecretValue", fields[0].Name);
        Assert.Equal("قيمة سرية", fields[0].DisplayName);
        Assert.True(catalog.IsFieldRegistered(nameof(CatalogProbeEntity), "SecretValue"));
        Assert.False(catalog.IsFieldRegistered(nameof(CatalogProbeEntity), "PublicValue"));
    }

    [Fact]
    public void GetFields_throws_for_unregistered_entity()
    {
        var catalog = new FieldPermissionCatalog(
            typeof(CatalogProbeEntity).Assembly,
            typeof(CatalogProbeEntityDto).Assembly);

        var error = Assert.Throws<RoleNotFoundException>(() => catalog.GetFields("MissingEntity"));
        Assert.Contains("غير موجود في كتالوج", error.Message, StringComparison.Ordinal);
    }
}

[FieldPermissionEntity("كيان تجريبي")]
public sealed class CatalogProbeEntity
{
    public Guid Id { get; set; }
    public string PublicValue { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
}

public sealed class CatalogProbeEntityDto
{
    public string PublicValue { get; set; } = string.Empty;

    [FieldPermission("SecretValue", DisplayName = "قيمة سرية")]
    public string SecretValue { get; set; } = string.Empty;
}
