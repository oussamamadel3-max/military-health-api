using Application.Common.Permissions;

namespace Application.Tests;

public class MilitaryHealthSeedCatalogTests
{
    [Fact]
    public void Roles_match_srs_section_6_2_without_duplicates()
    {
        var names = MilitaryHealthSeedCatalog.Roles.Select(r => r.Name).ToList();
        Assert.Equal(8, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("SuperAdmin", names);
        Assert.Contains("CenterManager", names);
        Assert.Contains("ReceptionClerk", names);
        Assert.Contains("ClinicDoctor", names);
        Assert.Contains("Supervisor", names);
        Assert.Contains("CenterOfficer", names);
        Assert.Contains("ArchiveClerk", names);
        Assert.Contains("Auditor", names);
    }

    [Fact]
    public void Permission_codes_are_unique_across_template_and_domain_catalogs()
    {
        var codes = MilitaryHealthSeedCatalog.AllCatalogPermissions.Select(p => p.Name).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(MilitaryHealthPermissions.AuditLog.Modify, codes);
        Assert.DoesNotContain(
            MilitaryHealthPermissions.AuditLog.Modify,
            MilitaryHealthSeedCatalog.MatrixGrants.SelectMany(g => g.PermissionNames));
    }

    [Fact]
    public void Matrix_grants_only_reference_known_roles_and_permission_codes()
    {
        var roleNames = MilitaryHealthSeedCatalog.Roles.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var permissionNames = MilitaryHealthSeedCatalog.AllCatalogPermissions
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var grant in MilitaryHealthSeedCatalog.MatrixGrants)
        {
            Assert.Contains(grant.RoleName, roleNames);
            Assert.NotEmpty(grant.PermissionNames);
            foreach (var permissionName in grant.PermissionNames)
                Assert.Contains(permissionName, permissionNames);
        }
    }

    [Fact]
    public void Clinic_doctor_receives_exam_permissions_and_not_final_decision()
    {
        var byRole = MilitaryHealthSeedCatalog.GrantsByRoleName();
        var doctor = byRole["ClinicDoctor"];

        Assert.Contains(MilitaryHealthPermissions.Clinical.ExamWrite, doctor);
        Assert.Contains(MilitaryHealthPermissions.Clinical.AssessmentWrite, doctor);
        Assert.Contains(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, doctor);
        Assert.DoesNotContain(MilitaryHealthPermissions.Clinical.FinalDecision, doctor);
        Assert.DoesNotContain(MilitaryHealthPermissions.Centers.Manage, doctor);
    }

    [Fact]
    public void Center_manager_does_not_receive_central_role_catalog_management()
    {
        var byRole = MilitaryHealthSeedCatalog.GrantsByRoleName();
        var manager = byRole["CenterManager"];

        Assert.Contains(SystemPermissions.UserManagement.Read, manager);
        Assert.Contains(MilitaryHealthPermissions.UserCenters.Manage, manager);
        Assert.DoesNotContain(SystemPermissions.RoleManagement.Create, manager);
        Assert.DoesNotContain(MilitaryHealthPermissions.Centers.Manage, manager);
        Assert.DoesNotContain(MilitaryHealthPermissions.Reports.Central, manager);
    }

    [Fact]
    public void Template_bootstrap_list_excludes_domain_permissions()
    {
        var bootstrap = MilitaryHealthSeedCatalog.TemplateBootstrapPermissions
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var domain = MilitaryHealthSeedCatalog.DomainPermissions
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(bootstrap.Intersect(domain, StringComparer.Ordinal));
        Assert.Contains(SystemPermissions.FieldPermissionManagement.Read, bootstrap);
    }
}
