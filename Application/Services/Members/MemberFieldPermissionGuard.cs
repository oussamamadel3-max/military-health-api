using Application.Common.Options;
using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.Extensions.Options;

namespace Application.Services.Members;

/// <summary>
/// حارس إنفاذ <c>Member.MotherName</c>: Default Deny للحقل المحمي فقط عند تفعيل العلم.
/// يدمج صلاحيات الأدوار والمستخدم عبر <see cref="IUserPermissionService.GetFieldAccessAsync"/>.
/// </summary>
public sealed class MemberFieldPermissionGuard : IMemberFieldPermissionGuard
{
    public const string EntityName = "Member";
    public const string MotherNameField = "MotherName";

    private readonly FieldPermissionsOptions _options;
    private readonly IUserPermissionService _userPermissions;
    private readonly ICurrentUserService _currentUser;

    public MemberFieldPermissionGuard(
        IOptions<FieldPermissionsOptions> options,
        IUserPermissionService userPermissions,
        ICurrentUserService currentUser)
    {
        _options = options?.Value ?? new FieldPermissionsOptions();
        _userPermissions = userPermissions;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public bool IsEnforcementEnabled => _options.EnforcementEnabled;

    /// <inheritdoc />
    public async Task ApplyReadToDetailsAsync(MemberDetailsDto dto, CancellationToken cancellationToken = default)
    {
        if (!IsEnforcementEnabled || dto is null)
            return;

        var access = await ResolveAccessAsync(cancellationToken);
        if (!FieldAccessSemantics.CanRead(access))
            dto.MotherName = null!;
    }

    /// <inheritdoc />
    public async Task EnsureMotherNameWriteAllowedAsync(
        string? proposedMotherName,
        string? existingMotherName,
        bool isCreate,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnforcementEnabled)
            return;

        if (isCreate)
        {
            // غياب/فراغ الحقل: لا رفض إضافي من Field Permission (قواعد الأعمال المنفصلة قد ترفض).
            if (string.IsNullOrWhiteSpace(proposedMotherName))
                return;

            await RequireWriteAsync(cancellationToken);
            return;
        }

        var next = Normalize(proposedMotherName);
        var previous = Normalize(existingMotherName);
        if (string.Equals(next, previous, StringComparison.Ordinal))
            return;

        await RequireWriteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemberImportValidRow>> MaskImportPreviewRowsAsync(
        IReadOnlyList<MemberImportValidRow> rows,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnforcementEnabled || rows is null || rows.Count == 0)
            return rows ?? Array.Empty<MemberImportValidRow>();

        var access = await ResolveAccessAsync(cancellationToken);
        if (FieldAccessSemantics.CanRead(access))
            return rows;

        return rows.Select(CloneImportRowWithoutMotherName).ToList();
    }

    private async Task<FieldAccessType> ResolveAccessAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
            return FieldAccessSemantics.DefaultWhenUnset;

        return await _userPermissions.GetFieldAccessAsync(
            userId, EntityName, MotherNameField, cancellationToken);
    }

    private async Task RequireWriteAsync(CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(cancellationToken);
        if (FieldAccessSemantics.CanWrite(access))
            return;

        throw new ForbiddenPermissionException(
            "ليس لديك صلاحية كتابة حقل اسم الأم.");
    }

    private static MemberImportValidRow CloneImportRowWithoutMotherName(MemberImportValidRow row)
        => new()
        {
            RowNumber = row.RowNumber,
            FullName = row.FullName,
            MotherName = null!,
            BirthDate = row.BirthDate,
            MaritalStatusId = row.MaritalStatusId,
            Occupation = row.Occupation,
            RecruitmentNumber = row.RecruitmentNumber,
            RecruitmentCenterId = row.RecruitmentCenterId,
            RecruitmentCenterNameAr = row.RecruitmentCenterNameAr,
            BloodTypeId = row.BloodTypeId,
            BloodSugarMgDl = row.BloodSugarMgDl,
            HeightCm = row.HeightCm,
            WeightKg = row.WeightKg,
            BloodPressure = row.BloodPressure,
            PulseBpm = row.PulseBpm,
            HasTattoo = row.HasTattoo,
            DistinguishingMarks = row.DistinguishingMarks
        };

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();
}
