using Application.Common.Results;
using Application.Dtos.ExportDtos;
using Application.Dtos.Members;

namespace Application.Interfaces.Services;

/// <summary>خدمات ملف المنتسب في المركز النشط (تسجيل MH-P3-020 على أساس MH-P3-010).</summary>
public interface IMemberService
{
    Task<PaginatedList<MemberListItemDto>> ListAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> GetByIdAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> CreateAsync(
        CreateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> UpdateAsync(
        Guid memberId,
        UpdateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يولّد PDF إيصال المنتسب والباركود ضمن المركز النشط دون تعديل أرقام الملف أو الدور.</summary>
    Task<ExportFileDto> GetReceiptPdfAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
