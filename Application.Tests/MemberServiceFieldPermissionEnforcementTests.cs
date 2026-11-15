using Application.Common.Options;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.Members;
using Domain.Enums;
using Moq;

namespace Application.Tests;

/// <summary>Phase C: إنفاذ MotherName عبر MemberService مع علم التفعيل.</summary>
public class MemberServiceFieldPermissionEnforcementTests
{
    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task GetById_masks_MotherName_when_Read_denied_and_enforcement_on()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var createService = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true);
        var created = await createService.CreateAsync(ValidCreate("FP-C-READ-1"));

        var viewService = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: false, allowView: true,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.None));
        var details = await viewService.GetByIdAsync(created.MemberId);

        Assert.True(string.IsNullOrEmpty(details.MotherName));
        var stored = await db.Members.FindAsync(created.MemberId);
        Assert.Equal("أم الاختبار", stored!.MotherName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task GetById_returns_MotherName_when_enforcement_off_even_without_field_grant()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var createService = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true);
        var created = await createService.CreateAsync(ValidCreate("FP-C-OFF-1"));

        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var viewService = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: false, allowView: true,
            fieldPermissions: CreateGuard(enabled: false, users.Object));
        var details = await viewService.GetByIdAsync(created.MemberId);

        Assert.Equal("أم الاختبار", details.MotherName);
        users.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_with_MotherName_denied_when_Write_missing()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var service = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.Read));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.CreateAsync(ValidCreate("FP-C-CREATE-DENY")));
        Assert.Empty(db.Members);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Create_with_MotherName_succeeds_with_Write_and_masks_response_without_Read()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var service = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.Write));
        var created = await service.CreateAsync(ValidCreate("FP-C-CREATE-WRITE"));

        Assert.True(string.IsNullOrEmpty(created.MotherName));
        var stored = await db.Members.FindAsync(created.MemberId);
        Assert.Equal("أم الاختبار", stored!.MotherName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_other_fields_succeeds_when_MotherName_unchanged_without_Write()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var bootstrap = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true);
        var created = await bootstrap.CreateAsync(ValidCreate("FP-C-UPD-SAME"));

        var service = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.None));
        var updated = await service.UpdateAsync(created.MemberId, new UpdateMemberRequest
        {
            FullName = "اسم معدّل",
            MotherName = created.MotherName,
            BirthDate = created.BirthDate,
            MaritalStatusId = created.MaritalStatusId,
            Occupation = created.Occupation,
            RecruitmentNumber = created.RecruitmentNumber,
            RecruitmentCenterId = created.RecruitmentCenterId,
            BloodTypeId = created.BloodTypeId,
            BloodSugarMgDl = created.BloodSugarMgDl,
            HeightCm = created.HeightCm,
            WeightKg = created.WeightKg,
            BloodPressure = created.BloodPressure,
            PulseBpm = created.PulseBpm,
            HasTattoo = created.HasTattoo,
            DistinguishingMarks = created.DistinguishingMarks
        });

        Assert.Equal("اسم معدّل", updated.FullName);
        Assert.True(string.IsNullOrEmpty(updated.MotherName));
        var stored = await db.Members.FindAsync(created.MemberId);
        Assert.Equal("أم الاختبار", stored!.MotherName);
        Assert.Equal("اسم معدّل", stored.FullName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Update_changing_MotherName_denied_without_Write()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var bootstrap = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true);
        var created = await bootstrap.CreateAsync(ValidCreate("FP-C-UPD-CHG"));

        var service = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.Read));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.UpdateAsync(created.MemberId, new UpdateMemberRequest
            {
                FullName = created.FullName,
                MotherName = "أم معدّلة",
                BirthDate = created.BirthDate,
                MaritalStatusId = created.MaritalStatusId,
                Occupation = created.Occupation,
                RecruitmentNumber = created.RecruitmentNumber,
                RecruitmentCenterId = created.RecruitmentCenterId,
                BloodTypeId = created.BloodTypeId,
                BloodSugarMgDl = created.BloodSugarMgDl,
                HeightCm = created.HeightCm,
                WeightKg = created.WeightKg,
                BloodPressure = created.BloodPressure,
                PulseBpm = created.PulseBpm,
                HasTattoo = created.HasTattoo,
                DistinguishingMarks = created.DistinguishingMarks
            }));

        var stored = await db.Members.FindAsync(created.MemberId);
        Assert.Equal("أم الاختبار", stored!.MotherName);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC")]
    public async Task Module_RBAC_still_blocks_without_Members_View()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var bootstrap = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: true, allowView: true);
        var created = await bootstrap.CreateAsync(ValidCreate("FP-C-RBAC"));

        var denied = MemberServiceTests.CreateService(
            db, MemberServiceTests.CenterA, allowCreate: false, allowView: false,
            fieldPermissions: CreateGuard(enabled: true, FieldAccessType.ReadWrite));

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.GetByIdAsync(created.MemberId));
    }

    private static IMemberFieldPermissionGuard CreateGuard(bool enabled, FieldAccessType access)
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync(
                It.IsAny<string>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        return CreateGuard(enabled, users.Object);
    }

    private static IMemberFieldPermissionGuard CreateGuard(bool enabled, IUserPermissionService users)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns("actor-1");
        return new MemberFieldPermissionGuard(
            Microsoft.Extensions.Options.Options.Create(
                new FieldPermissionsOptions { EnforcementEnabled = enabled }),
            users,
            current.Object);
    }

    private static CreateMemberRequest ValidCreate(string recruitment) => new()
    {
        FullName = "منتسب صلاحيات حقل",
        MotherName = "أم الاختبار",
        BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
        MaritalStatusId = 1,
        Occupation = "موظف",
        RecruitmentNumber = recruitment,
        RecruitmentCenterId = MemberServiceTests.RecruitmentCenterId,
        BloodTypeId = 1,
        HeightCm = 175,
        WeightKg = 70
    };
}
