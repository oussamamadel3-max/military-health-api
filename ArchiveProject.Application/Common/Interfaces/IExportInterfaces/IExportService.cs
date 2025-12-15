using ArchiveProject.Application.Dtos.ExportDtos;

namespace ArchiveProject.Application.Common.Interfaces.ExportInterfaces
{
    public interface IExportService
    {
        Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options);
        Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options);

        Task<ExportFileDto> QuickExportToExcelAsync<T>(IEnumerable<T> data, string reportName);
        Task<ExportFileDto> QuickExportToPdfAsync<T>(IEnumerable<T> data, string reportName);
    }

    public class ExportOptions
    {
        public string ReportName { get; set; } = "Report";
        public string SheetName { get; set; } = "تقرير";
        public string? CustomTitle { get; set; }

        public bool IncludeSummary { get; set; } = true;
        public bool IncludeLogo { get; set; } = false;
        public bool IncludeFooter { get; set; } = true;

        public bool AutoFilter { get; set; } = true;
        public bool FreezeHeaders { get; set; } = true;

        public Dictionary<string, string>? ColumnMappings { get; set; }
        public List<string>? ExcludedColumns { get; set; }

        public string? DateFormat { get; set; } = "yyyy/MM/dd";
        public string? NumberFormat { get; set; } = "#,##0.00";

        // معلومات سياقية للتقرير
        public string? BranchName { get; set; }
        public string? DepartmentName { get; set; }
        public string? PeriodText { get; set; }  // من..إلى
    }
}
