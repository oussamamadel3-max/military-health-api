using System.Net;
using System.Net.Http.Json;
using API.IntegrationTests.Infrastructure;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

[Collection(AuthHttp091Collection.Name)]
public sealed class UserClinicAssignmentAssignHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public UserClinicAssignmentAssignHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Local_manager_creates_assign_revokes_reactivates_and_rejects_duplicate()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var b = UserClinicAssignmentHttpSeed.CenterB;
        var clinic = UserClinicAssignmentHttpSeed.ClinicEyes;

        var session = await LoginAndSelectAsync(
            UserClinicAssignmentHttpSeed.DualCenterManagerUserName,
            b);

        var create = await session.AssignUserClinicAsync(
            UserClinicAssignmentHttpSeed.Doctor2UserId, b, clinic);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ApiEnvelope<UserClinicAssignmentDto>>();
        Assert.NotNull(created?.Data);
        Assert.True(created.Data.IsActive);

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.UserClinicAssignments
                .SingleAsync(a => a.AssignmentId == created.Data.AssignmentId);
            Assert.True(row.IsActive);
        });

        var duplicate = await session.AssignUserClinicAsync(
            UserClinicAssignmentHttpSeed.Doctor2UserId, b, clinic);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);

        var revoke = await session.RevokeUserClinicAsync(created.Data.AssignmentId, "اختبار HTTP");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.UserClinicAssignments
                .SingleAsync(a => a.AssignmentId == created.Data.AssignmentId);
            Assert.False(row.IsActive);
        });

        var reactivate = await session.AssignUserClinicAsync(
            UserClinicAssignmentHttpSeed.Doctor2UserId, b, clinic);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var active = await db.UserClinicAssignments.CountAsync(a =>
                a.UserId == UserClinicAssignmentHttpSeed.Doctor2UserId
                && a.CenterId == b
                && a.ClinicId == clinic
                && a.IsActive);
            Assert.Equal(1, active);
        });
    }

    [Fact]
    public async Task Central_super_admin_assigns_doctor_at_foreign_center_after_selecting_center()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(
                UserClinicAssignmentHttpSeed.CentralSuperUserName,
                UserClinicAssignmentHttpSeed.ClinicTestPassword)).StatusCode);

        var b = UserClinicAssignmentHttpSeed.CenterB;
        var clinic = UserClinicAssignmentHttpSeed.ClinicEyes;
        Assert.Equal(HttpStatusCode.OK, (await session.SelectCenterAsync(b)).StatusCode);

        var seededStillActive = await _factory.WithDbAsync(db =>
            db.UserClinicAssignments.AnyAsync(a =>
                a.AssignmentId == UserClinicAssignmentHttpSeed.SeededAssignmentCenterB && a.IsActive));
        if (seededStillActive)
        {
            Assert.Equal(HttpStatusCode.OK,
                (await session.RevokeUserClinicAsync(
                    UserClinicAssignmentHttpSeed.SeededAssignmentCenterB,
                    "تحضير إسناد مركزي HTTP")).StatusCode);
        }

        var assign = await session.AssignUserClinicAsync(
            UserClinicAssignmentHttpSeed.DoctorUserId, b, clinic);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.UserClinicAssignments
                .SingleAsync(a =>
                    a.UserId == UserClinicAssignmentHttpSeed.DoctorUserId
                    && a.CenterId == b
                    && a.ClinicId == clinic
                    && a.IsActive);
            Assert.Equal(UserClinicAssignmentHttpSeed.DoctorUserId, row.UserId);
        });
    }

    [Fact]
    public async Task Cross_center_assign_still_returns_forbidden()
    {
        await UserClinicAssignmentHttpSeed.EnsureFixturesAsync(_factory.Services);
        var session = await LoginAndSelectAsync(
            UserClinicAssignmentHttpSeed.LocalCrossUserName,
            UserClinicAssignmentHttpSeed.CenterA);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await session.AssignUserClinicAsync(
                UserClinicAssignmentHttpSeed.Doctor2UserId,
                UserClinicAssignmentHttpSeed.CenterB,
                UserClinicAssignmentHttpSeed.ClinicEyes)).StatusCode);
    }

    private async Task<AuthHttpSession> LoginAndSelectAsync(string userName, Guid centerId)
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await session.LoginAsync(userName, UserClinicAssignmentHttpSeed.ClinicTestPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.SelectCenterAsync(centerId)).StatusCode);
        return session;
    }

    private sealed class ApiEnvelope<T>
    {
        public T? Data { get; set; }
    }
}
