using Application.Common.Permissions;
using Domain.Enums;

namespace Application.Tests;

public class FieldAccessSemanticsTests
{
    [Theory]
    [InlineData(FieldAccessType.None, false, false)]
    [InlineData(FieldAccessType.Read, true, false)]
    [InlineData(FieldAccessType.Write, false, true)]
    [InlineData(FieldAccessType.ReadWrite, true, true)]
    public void CanRead_and_CanWrite_match_documented_semantics(
        FieldAccessType access,
        bool canRead,
        bool canWrite)
    {
        Assert.Equal(canRead, FieldAccessSemantics.CanRead(access));
        Assert.Equal(canWrite, FieldAccessSemantics.CanWrite(access));
    }

    [Fact]
    public void MergeRoleAccess_unions_read_and_write_capabilities()
    {
        var merged = FieldAccessSemantics.MergeRoleAccess(
        [
            FieldAccessType.Read,
            FieldAccessType.Write
        ]);

        Assert.Equal(FieldAccessType.ReadWrite, merged);
    }

    [Fact]
    public void ResolveEffective_user_assignment_overrides_roles()
    {
        var effective = FieldAccessSemantics.ResolveEffective(
            FieldAccessType.None,
            [FieldAccessType.ReadWrite]);

        Assert.Equal(FieldAccessType.None, effective);
    }

    [Fact]
    public void ResolveEffective_without_user_uses_role_union()
    {
        var effective = FieldAccessSemantics.ResolveEffective(
            null,
            [FieldAccessType.Read, FieldAccessType.None]);

        Assert.Equal(FieldAccessType.Read, effective);
    }

    [Fact]
    public void DefaultWhenUnset_is_None()
    {
        Assert.Equal(FieldAccessType.None, FieldAccessSemantics.DefaultWhenUnset);
    }
}
