using System.Net;
using API.IntegrationTests.Infrastructure;
using Application.Common.Security;
using Application.Services.AuthServices;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

[Collection(AuthHttp091Collection.Name)]
public sealed class UserClinicAssignmentPermissionHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public UserClinicAssignmentPermissionHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Cross_center_requests_return_403_for_all_five_routes_while_session_is_center_a()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.LocalCrossUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        var b = UserClinicAssignmentHttpSeed.CenterB;
        var clinic = UserClinicAssignmentHttpSeed.ClinicEyes;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{b}/clinic-assignments/clinics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{b}/clinic-assignments/doctors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{b}/clinics/{clinic}/doctors")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.AssignUserClinicAsync(UserClinicAssignmentHttpSeed.DoctorUserId, b, clinic)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.RevokeUserClinicAsync(UserClinicAssignmentHttpSeed.SeededAssignmentCenterB, "اختبار")).StatusCode);
    }

    [Fact]
    public async Task Same_center_requests_succeed_for_all_five_routes()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.LocalCrossUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        var a = UserClinicAssignmentHttpSeed.CenterA;
        var clinic = UserClinicAssignmentHttpSeed.ClinicEyes;

        Assert.Equal(HttpStatusCode.OK,
            (await session.Client.GetAsync($"/api/centers/{a}/clinic-assignments/clinics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await session.Client.GetAsync($"/api/centers/{a}/clinic-assignments/doctors")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await session.Client.GetAsync($"/api/centers/{a}/clinics/{clinic}/doctors")).StatusCode);

        var assignResponse = await session.AssignUserClinicAsync(
            UserClinicAssignmentHttpSeed.Doctor2UserId, a, clinic);
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await session.RevokeUserClinicAsync(UserClinicAssignmentHttpSeed.SeededAssignmentCenterA, "اختبار")).StatusCode);
    }

    [Fact]
    public async Task Central_super_admin_can_read_and_mutate_clinic_assignments_for_foreign_center()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var centralSession = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await centralSession.LoginAsync(
                UserClinicAssignmentHttpSeed.CentralSuperUserName,
                UserClinicAssignmentHttpSeed.ClinicTestPassword)).StatusCode);

        var b = UserClinicAssignmentHttpSeed.CenterB;
        var clinic = UserClinicAssignmentHttpSeed.ClinicEyes;

        Assert.Equal(HttpStatusCode.OK,
            (await centralSession.Client.GetAsync($"/api/centers/{b}/clinic-assignments/clinics")).StatusCode);

        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.CentralSuperUserName,
            b);

        Assert.Equal(HttpStatusCode.OK,
            (await session.RevokeUserClinicAsync(
                UserClinicAssignmentHttpSeed.SeededAssignmentCenterB,
                "اختبار إعادة إسناد مركزي")).StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var revoked = await db.UserClinicAssignments.FindAsync(UserClinicAssignmentHttpSeed.SeededAssignmentCenterB);
            Assert.NotNull(revoked);
            Assert.False(revoked!.IsActive);
        });

    }

    [Fact]
    public async Task Dual_local_manager_with_manage_in_a_and_b_session_a_still_gets_403_for_center_b()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.DualCenterManagerUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        var b = UserClinicAssignmentHttpSeed.CenterB;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{b}/clinic-assignments/clinics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.AssignUserClinicAsync(
                UserClinicAssignmentHttpSeed.DoctorUserId,
                b,
                UserClinicAssignmentHttpSeed.ClinicEyes)).StatusCode);
    }

    [Fact]
    public async Task Central_auditor_without_clinic_manage_is_denied_on_read_and_write()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(
                UserClinicAssignmentHttpSeed.CentralAuditorUserName,
                UserClinicAssignmentHttpSeed.ClinicTestPassword)).StatusCode);

        var a = UserClinicAssignmentHttpSeed.CenterA;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{a}/clinic-assignments/doctors")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.AssignUserClinicAsync(
                UserClinicAssignmentHttpSeed.DoctorUserId,
                a,
                UserClinicAssignmentHttpSeed.ClinicEyes)).StatusCode);
    }

    [Fact]
    public async Task Mixed_central_auditor_and_local_manager_cannot_manage_foreign_center()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.MixedAuditorManagerUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        var b = UserClinicAssignmentHttpSeed.CenterB;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{b}/clinic-assignments/clinics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.AssignUserClinicAsync(
                UserClinicAssignmentHttpSeed.DoctorUserId,
                b,
                UserClinicAssignmentHttpSeed.ClinicEyes)).StatusCode);
    }

    [Fact]
    public async Task Revoked_center_assignment_yields_403_for_operational_clinic_routes()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectCenterAsync(
            UserClinicAssignmentHttpSeed.RevokedCenterUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        await _factory.WithDbAsync(async db =>
        {
            var userId = db.Users
                .Where(u => u.UserName == UserClinicAssignmentHttpSeed.RevokedCenterUserName)
                .Select(u => u.Id)
                .Single();
            var row = db.UserCenters.Single(uc =>
                uc.UserId == userId && uc.CenterId == UserClinicAssignmentHttpSeed.CenterA);
            IamRetention.Revoke(row, userId, "اختبار إلغاء الإسناد");
            await db.SaveChangesAsync();
        });

        var a = UserClinicAssignmentHttpSeed.CenterA;
        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.Client.GetAsync($"/api/centers/{a}/clinic-assignments/clinics")).StatusCode);
    }

    private async Task<AuthHttpSession> LoginAndSelectCenterAsync(string userName, Guid centerId)
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(userName, UserClinicAssignmentHttpSeed.ClinicTestPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.SelectCenterAsync(centerId)).StatusCode);
        return session;
    }

}
