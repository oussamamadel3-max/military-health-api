using Application.Common.Results;
using Application.Dtos.ExportDtos;
using Application.Dtos.OperationalReports;

namespace Application.Interfaces.Services;

/// <summary>
/// مؤشرات وتقارير تشغيلية محلية/مركزية (FR-062…FR-068) دون الاعتماد على Dashboard/Reports القالبية.
/// </summary>
public interface IOperationalReportsService
{
    Task<CenterKpiDto> GetLocalKpisAsync(
        LocalKpiQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<CentralKpiResponseDto> GetCentralKpisAsync(
        CentralKpiQuery query,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportCentralKpisAsync(
        CentralKpiQuery query,
        CancellationToken cancellationToken = default);

    Task<PaginatedList<FinalMemberListItemDto>> ListFinalMembersAsync(
        FinalMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportFinalMembersAsync(
        FinalMembersQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<DeferredReportResponseDto> GetDeferredReportAsync(
        DeferredReportQuery query,
        Guid? clientCenterId = null,
        bool central = false,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportDeferredReportAsync(
        DeferredReportQuery query,
        Guid? clientCenterId = null,
        bool central = false,
        CancellationToken cancellationToken = default);

    Task<LocalSummaryDto> GetLocalSummaryAsync(
        LocalSummaryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportLocalSummaryAsync(
        LocalSummaryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<CentralCompareResponseDto> GetCentralCompareAsync(
        CentralCompareQuery query,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportCentralCompareAsync(
        CentralCompareQuery query,
        CancellationToken cancellationToken = default);

    Task<PaginatedList<DoctorLinkageByMemberItemDto>> ListDoctorLinkageByMemberAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<PaginatedList<DoctorLinkageByDoctorItemDto>> ListDoctorLinkageByDoctorAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<ExportFileDto> ExportDoctorLinkageAsync(
        DoctorLinkageQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
