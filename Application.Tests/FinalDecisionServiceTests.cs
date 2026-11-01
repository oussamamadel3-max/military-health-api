using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services.ClinicWorkspace;
using Application.Services.Supervisor;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class FinalDecisionServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicInternal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly Guid[] AllClinics =
    [
        ClinicEyes, ClinicEnt, ClinicInternal, ClinicSurgery, ClinicOrtho
    ];

    [Fact]
    public async Task Issue_accepted_compatible_with_all_clinics_succeeds()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-OK");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 1,
            ExpectedDataRevision = 1
        });

        Assert.Equal(memberId, result.MemberId);
        Assert.Equal(CenterA, result.CenterId);
        Assert.Equal((short)1, result.ResultStatusId);
        Assert.Equal("مقبول", result.ResultStatusNameAr);
        Assert.Null(result.RejectionReason);
        Assert.Null(result.DisagreementJustification);
        Assert.Empty(result.DisagreeingClinicCodes);
        Assert.Equal(0, result.ObjectionCount);
        Assert.False(result.ArchiveCreated);
        Assert.Equal(2, result.MemberDataRevision);
        Assert.Equal("actor-1", result.SupervisorUserId);

        Assert.Equal(1, await db.FinalDecisions.CountAsync());
        Assert.Equal(0, await db.SupervisorObjections.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == FinalDecisionService.FinalDecisionIssuedEventType
            && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Issue_rejected_requires_reason_and_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-REJ");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 2);

        var service = CreateService(db, CenterA, allow: true);

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 2 }));

        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "غير لائق طبياً"
        });

        Assert.Equal((short)2, result.ResultStatusId);
        Assert.Equal("غير لائق طبياً", result.RejectionReason);
        Assert.Equal("مرفوض", result.ResultStatusNameAr);
    }

    [Fact]
    public async Task Issue_disagreement_without_justification_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DIS");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var error = await Assert.ThrowsAsync<FinalDecisionDisagreementJustificationRequiredException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض مشرف"
            }));

        Assert.Equal("FINAL_DECISION_DISAGREEMENT_JUSTIFICATION_REQUIRED", error.ErrorCode);
        Assert.Equal(0, await db.FinalDecisions.CountAsync());
        Assert.Equal(0, await db.SupervisorObjections.CountAsync());
    }

    [Fact]
    public async Task Issue_disagreement_with_justification_creates_objections()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-OBJ");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "رفض بعد مراجعة شاملة",
            DisagreementJustification = "تعارض مع تقدير العيادات ويُعتمد الرفض"
        });

        Assert.Equal(5, result.ObjectionCount);
        Assert.Equal(5, result.DisagreeingClinicCodes.Count);
        Assert.Contains("EYE", result.DisagreeingClinicCodes);
        Assert.Equal("تعارض مع تقدير العيادات ويُعتمد الرفض", result.DisagreementJustification);

        var objections = await db.SupervisorObjections.ToListAsync();
        Assert.Equal(5, objections.Count);
        Assert.All(objections, o =>
        {
            Assert.Equal(FinalDecisionService.TargetScopeOneClinic, o.TargetScope);
            Assert.NotNull(o.ClinicAssessmentId);
            Assert.Equal(result.FinalDecisionId, o.FinalDecisionId);
            Assert.Equal(FinalDecisionService.ObjectionStatusOpen, o.Status);
        });
    }

    [Fact]
    public async Task Issue_incomplete_clinics_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-INC");
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, 1);
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEnt, 1);

        var service = CreateService(db, CenterA, allow: true);
        var error = await Assert.ThrowsAsync<IncompleteClinicAssessmentsException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));

        Assert.Equal("FINAL_DECISION_INCOMPLETE_CLINICS", error.ErrorCode);
        Assert.Equal(0, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_second_decision_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DUP");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<FinalDecisionAlreadyExistsException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));

        Assert.Equal(1, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-PERM");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));
    }

    [Fact]
    public async Task Issue_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-XCTR");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var serviceB = CreateService(db, CenterB, allow: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            serviceB.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));
    }

    [Fact]
    public async Task Issue_rejects_stale_data_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-REV");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await Assert.ThrowsAsync<FinalDecisionConcurrencyException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 1,
                ExpectedDataRevision = 99
            }));

        Assert.Equal(0, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_deferred_creates_requested_action_linked_to_final_decision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF-OK");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);

        var service = CreateService(db, CenterA, allow: true);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        var result = await service.IssueAsync(memberId, ValidDeferredRequest(until, actionTypeId: 1));

        Assert.Equal((short)3, result.ResultStatusId);
        Assert.Equal("مؤجل", result.ResultStatusNameAr);
        Assert.NotNull(result.DeferredActionId);
        Assert.NotNull(result.DeferredAction);
        Assert.Equal(FinalDecisionService.DeferredStatusRequested, result.DeferredAction!.Status);
        Assert.Equal(until, result.DeferredAction.DeferUntil);
        Assert.Equal("يحتاج استمارة", result.DeferredAction.DeferReason);
        Assert.Equal("متابعة بعد الاستمارة", result.DeferredAction.FollowupNote);
        Assert.Null(result.RejectionReason);

        var action = Assert.Single(await db.DeferredActions.ToListAsync());
        Assert.Equal(result.FinalDecisionId, action.FinalDecisionId);
        Assert.Null(action.ClinicId);
        Assert.Null(action.ClinicAssessmentId);
        Assert.Equal(FinalDecisionService.DeferredStatusRequested, action.Status);
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == FinalDecisionService.FinalDecisionIssuedEventType));
    }

    [Fact]
    public async Task Issue_deferred_without_reason_or_until_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF-MISS");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);
        var service = CreateService(db, CenterA, allow: true);

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 3,
                ActionTypeId = 1,
                FollowupNote = "ملاحظة",
                DeferUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7)
            }));

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 3,
                DeferReason = "سبب",
                ActionTypeId = 1,
                FollowupNote = "ملاحظة"
            }));

        Assert.Equal(0, await db.FinalDecisions.CountAsync());
        Assert.Equal(0, await db.DeferredActions.CountAsync());
    }

    [Fact]
    public async Task Issue_deferred_other_action_requires_description()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF-OTHER");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);
        var service = CreateService(db, CenterA, allow: true);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, ValidDeferredRequest(until, actionTypeId: 2)));

        var result = await service.IssueAsync(memberId, ValidDeferredRequest(
            until,
            actionTypeId: 2,
            actionDescription: "إجراء مخصص من المشرف"));

        Assert.Equal("إجراء مخصص من المشرف", result.DeferredAction!.ActionDescription);
        Assert.Equal(2, result.DeferredAction.ActionTypeId);
    }

    [Fact]
    public async Task Issue_deferred_appears_on_supervisor_assessment_file()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF-VIEW");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);

        var decide = CreateService(db, CenterA, allow: true);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var issued = await decide.IssueAsync(memberId, ValidDeferredRequest(until, actionTypeId: 1));

        var fileService = CreateAssessmentFileService(db, CenterA);
        var file = await fileService.GetAssessmentFileAsync(memberId);

        Assert.False(file.CanIssueFinalDecision);
        Assert.NotNull(file.ExistingFinalDecision);
        Assert.Equal((short)3, file.ExistingFinalDecision!.ResultStatusId);
        Assert.Contains(file.DeferredActions, d => d.DeferredActionId == issued.DeferredActionId
            && d.FinalDecisionId == issued.FinalDecisionId
            && d.Status == FinalDecisionService.DeferredStatusRequested);
        Assert.True(file.HasOpenDeferredActions);
    }

    [Fact]
    public async Task Issue_deferred_investigation_create_does_not_close_action()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF-INV");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);

        var decide = CreateService(db, CenterA, allow: true);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var issued = await decide.IssueAsync(memberId, ValidDeferredRequest(until, actionTypeId: 1));

        db.Investigations.Add(new Domain.Entities.Medical.Investigation
        {
            InvestigationId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            RequestedByUserId = "actor-1",
            InvestigationTypeId = 1,
            TestName = "CBC",
            RequestReason = "مرتبط بتأجيل القرار",
            Status = "PENDING",
            DeferredActionId = issued.DeferredActionId,
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var action = await db.DeferredActions.SingleAsync(d => d.DeferredActionId == issued.DeferredActionId);
        Assert.Equal(FinalDecisionService.DeferredStatusRequested, action.Status);
        Assert.Null(action.CompletedAt);
        Assert.Null(action.CompletedByUserId);
    }

    [Fact]
    public async Task Issue_compatible_reject_does_not_require_justification()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-COMP");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 2);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "موافق لجميع العيادات"
        });

        Assert.Equal(0, result.ObjectionCount);
        Assert.Null(result.DisagreementJustification);
    }

    [Fact]
    public async Task Issue_accept_rejects_defer_fields()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-NODEFER");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);
        var service = CreateService(db, CenterA, allow: true);

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 1,
                DeferReason = "لا يجوز"
            }));
    }

    [Fact]
    public async Task Amend_with_reason_updates_decision_and_writes_history()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-OK");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var issued = await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.ExportStatus = FinalDecisionService.ExportStatusExported;
        member.LastExportedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var amended = await service.AmendAsync(memberId, new AmendFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "رفض بعد نتائج جديدة",
            DisagreementJustification = "اختلاف مع العيادات بعد مراجعة إضافية",
            ChangeReason = "ظهور نتائج مخبرية جديدة",
            ExpectedDataRevision = issued.MemberDataRevision
        });

        Assert.Equal(issued.FinalDecisionId, amended.FinalDecisionId);
        Assert.Equal((short)2, amended.ResultStatusId);
        Assert.Equal((short)1, amended.OldResultStatusId);
        Assert.Equal("ظهور نتائج مخبرية جديدة", amended.ChangeReason);
        Assert.True(amended.ExportResetPerformed);
        Assert.Equal(FinalDecisionService.ExportStatusExported, amended.PreviousExportStatus);
        Assert.Equal(1, await db.FinalDecisions.CountAsync());
        Assert.Equal(1, await db.FinalDecisionHistories.CountAsync());

        var history = await db.FinalDecisionHistories.SingleAsync();
        Assert.Equal(amended.HistoryId, history.HistoryId);
        Assert.Equal((short)1, history.OldStatusId);
        Assert.Equal((short)2, history.NewStatusId);
        Assert.Equal("ظهور نتائج مخبرية جديدة", history.ChangeReason);
        Assert.True(history.ExportResetPerformed);
        Assert.Equal("actor-1", history.ChangedByUserId);

        member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        Assert.Equal(FinalDecisionService.ExportStatusUnexported, member.ExportStatus);
        Assert.Null(member.LastExportedAt);

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == FinalDecisionService.FinalDecisionAmendedEventType
            && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Amend_without_change_reason_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-NOREASON");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض",
                DisagreementJustification = "مبرر",
                ChangeReason = "   "
            }));

        Assert.Equal(0, await db.FinalDecisionHistories.CountAsync());
        Assert.Equal((short)1, (await db.FinalDecisions.SingleAsync()).ResultStatusId);
    }

    [Fact]
    public async Task Amend_same_result_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-SAME");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<FinalDecisionUnchangedException>(() =>
            service.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 1,
                ChangeReason = "لا تغيير فعلي"
            }));

        Assert.Equal(0, await db.FinalDecisionHistories.CountAsync());
    }

    [Fact]
    public async Task Amend_deferred_to_accepted_keeps_prior_deferred_row_closed()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-DEF2ACC");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 3);

        var service = CreateService(db, CenterA, allow: true);
        var until = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var issued = await service.IssueAsync(memberId, ValidDeferredRequest(until, actionTypeId: 1));

        // عيادات مقبولة بعد النتائج الجديدة — تعديل إلى مقبول يتطلب مبرر اختلاف مع تقييمات مؤجلة.
        var amended = await service.AmendAsync(memberId, new AmendFinalDecisionRequest
        {
            ResultStatusId = 1,
            DisagreementJustification = "اكتمال الإجراء المطلوب وقبول الملف",
            ChangeReason = "إنجاز الإجراء المؤجل وظهور نتيجة نهائية",
            ExpectedDataRevision = issued.MemberDataRevision
        });

        Assert.Equal((short)1, amended.ResultStatusId);
        Assert.Equal((short)3, amended.OldResultStatusId);
        Assert.Null(amended.DeferredActionId);

        var actions = await db.DeferredActions.Where(d => d.FinalDecisionId == issued.FinalDecisionId).ToListAsync();
        Assert.Single(actions);
        Assert.Equal(issued.DeferredActionId, actions[0].DeferredActionId);
        Assert.Equal(FinalDecisionService.DeferredStatusClosed, actions[0].Status);
        Assert.Equal(1, await db.FinalDecisionHistories.CountAsync());
    }

    [Fact]
    public async Task Amend_rejects_stale_data_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-REV");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<FinalDecisionConcurrencyException>(() =>
            service.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض",
                DisagreementJustification = "مبرر",
                ChangeReason = "تعديل متزامن",
                ExpectedDataRevision = 99
            }));

        Assert.Equal(0, await db.FinalDecisionHistories.CountAsync());
    }

    [Fact]
    public async Task Amend_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-XCTR");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var serviceA = CreateService(db, CenterA, allow: true);
        await serviceA.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        var serviceB = CreateService(db, CenterB, allow: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            serviceB.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض",
                DisagreementJustification = "مبرر",
                ChangeReason = "محاولة من مركز آخر"
            }));

        Assert.Equal(0, await db.FinalDecisionHistories.CountAsync());
    }

    [Fact]
    public async Task Amend_without_existing_decision_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-NONE");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await Assert.ThrowsAsync<FinalDecisionNotFoundException>(() =>
            service.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 1,
                ChangeReason = "لا يوجد قرار"
            }));
    }

    [Fact]
    public async Task GetHistory_returns_rows_newest_first_for_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-HIST");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var issued = await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        var first = await service.AmendAsync(memberId, new AmendFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "رفض أولي",
            DisagreementJustification = "مبرر أول",
            ChangeReason = "تعديل أول",
            ExpectedDataRevision = issued.MemberDataRevision
        });

        var second = await service.AmendAsync(memberId, new AmendFinalDecisionRequest
        {
            ResultStatusId = 1,
            DisagreementJustification = null,
            ChangeReason = "تعديل ثانٍ إلى قبول",
            ExpectedDataRevision = first.MemberDataRevision
        });

        // التعديل الثاني من مرفوض إلى مقبول يحتاج مبرر لأن العيادات مقبولة؟ العيادات كلها 1، القرار الجديد 1 → لا اختلاف.
        var history = await service.GetHistoryAsync(memberId);

        Assert.Equal(2, history.Count);
        Assert.Equal(second.HistoryId, history[0].HistoryId);
        Assert.Equal((short)2, history[0].OldStatusId);
        Assert.Equal((short)1, history[0].NewStatusId);
        Assert.Equal("تعديل ثانٍ إلى قبول", history[0].ChangeReason);
        Assert.Equal("مرفوض", history[0].OldStatusNameAr);
        Assert.Equal("مقبول", history[0].NewStatusNameAr);
        Assert.Equal(first.HistoryId, history[1].HistoryId);
        Assert.Equal((short)1, history[1].OldStatusId);
        Assert.Equal((short)2, history[1].NewStatusId);
        Assert.Equal("مقبول", history[1].OldStatusNameAr);
        Assert.Equal("مرفوض", history[1].NewStatusNameAr);
    }

    [Fact]
    public async Task Amend_reapplies_disagreement_justification_rule()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-AMD-DIS");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var issued = await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<FinalDecisionDisagreementJustificationRequiredException>(() =>
            service.AmendAsync(memberId, new AmendFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض",
                ChangeReason = "بدون مبرر اختلاف",
                ExpectedDataRevision = issued.MemberDataRevision
            }));

        Assert.Equal(0, await db.FinalDecisionHistories.CountAsync());
    }

    #region Helpers

    private static IssueFinalDecisionRequest ValidDeferredRequest(
        DateOnly deferUntil,
        short actionTypeId,
        string? actionDescription = null)
        => new()
        {
            ResultStatusId = 3,
            DeferReason = "يحتاج استمارة",
            DeferUntil = deferUntil,
            ActionTypeId = actionTypeId,
            ActionDescription = actionDescription,
            FollowupNote = "متابعة بعد الاستمارة",
            DisagreementJustification = null
        };

    private static FinalDecisionService CreateService(AppDbContext db, Guid centerId, bool allow)
    {
        var gate = new Mock<IPermissionGate>();
        if (allow)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        return new FinalDecisionService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new AuditLogRepository(db),
            new FinalDecisionTestUnitOfWork(db));
    }

    private static SupervisorAssessmentFileService CreateAssessmentFileService(AppDbContext db, Guid centerId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Clinical.FinalDecision,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);
        gate.Setup(g => g.Has(MilitaryHealthPermissions.Medical.AttachmentsRead)).Returns(true);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);

        var medicalRequests = new MemberMedicalRequestService(
            new ConsultationRepository(db),
            new InvestigationRepository(db),
            new AttachmentRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            gate.Object);

        return new SupervisorAssessmentFileService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            medicalRequests,
            new AuditLogRepository(db),
            new FinalDecisionTestUnitOfWork(db),
            new Mapper(config));
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب قرار نهائي",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 66,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });
        return created.MemberId;
    }

    private static async Task SeedAllAssessmentsAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        short resultStatusId)
    {
        foreach (var clinicId in AllClinics)
        {
            await SeedAssessmentAsync(
                db,
                memberId,
                centerId,
                clinicId,
                resultStatusId,
                rejectionReason: resultStatusId == 2 ? "سبب عيادة" : null);
        }
    }

    private static async Task SeedAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        short resultStatusId,
        string? rejectionReason = null)
    {
        var examId = Guid.NewGuid();
        var assessmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = "actor-1",
            ExaminedAt = now,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        });

        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = assessmentId,
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            ClinicalNotes = "ملاحظات",
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedClinicAsync(db, ClinicInternal, "INTERNAL", "الباطنة", 3);
        await SeedClinicAsync(db, ClinicSurgery, "SURGERY", "الجراحة", 4);
        await SeedClinicAsync(db, ClinicOrtho, "ORTHOPEDIC", "العظمية", 5);

        foreach (var clinicId in AllClinics)
        {
            await SeedCenterClinicAsync(db, CenterA, clinicId);
            await SeedCenterClinicAsync(db, CenterB, clinicId);
        }

        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        if (!await db.RequiredActionTypes.AnyAsync(t => t.ActionTypeId == 1))
        {
            db.RequiredActionTypes.Add(new RequiredActionType
            {
                ActionTypeId = 1,
                Code = "FORM",
                NameAr = "طلب استمارة",
                RequiresDescription = false,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        if (!await db.RequiredActionTypes.AnyAsync(t => t.ActionTypeId == 2))
        {
            db.RequiredActionTypes.Add(new RequiredActionType
            {
                ActionTypeId = 2,
                Code = "OTHER",
                NameAr = "غير ذلك",
                RequiresDescription = true,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        if (!await db.InvestigationTypes.AnyAsync(t => t.InvestigationTypeId == 1))
        {
            db.InvestigationTypes.Add(new InvestigationType
            {
                InvestigationTypeId = 1,
                Code = "LAB",
                NameAr = "مخبري",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
    {
        if (await db.Clinics.AnyAsync(c => c.ClinicId == clinicId))
            return;

        db.Clinics.Add(new Clinic
        {
            ClinicId = clinicId,
            Code = code,
            NameAr = nameAr,
            SortOrder = (short)sort,
            CountsForFinal = true,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
    {
        if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
            return;

        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private sealed class FinalDecisionTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public FinalDecisionTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTransaction());
    }

    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
