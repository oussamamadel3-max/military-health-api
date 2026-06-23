//using Application.Dtos.ExportDtos;

//namespace Application.Interfaces.Services.ExportInterfaces
//{
//    public interface IExportService
//    {
//        Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options);
//        Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options);

//        Task<ExportFileDto> QuickExportToExcelAsync<T>(IEnumerable<T> data, string reportName);
//        Task<ExportFileDto> QuickExportToPdfAsync<T>(IEnumerable<T> data, string reportName);
//    }

//    public class ExportOptions
//    {
//        public string ReportName { get; set; } = "Report";
//        public string SheetName { get; set; } = "تقرير";
//        public string? CustomTitle { get; set; }

//        public bool IncludeSummary { get; set; } = true;
//        public bool IncludeLogo { get; set; } = false;
//        public bool IncludeFooter { get; set; } = true;

//        public bool AutoFilter { get; set; } = true;
//        public bool FreezeHeaders { get; set; } = true;

//        public Dictionary<string, string>? ColumnMappings { get; set; }
//        public List<string>? ExcludedColumns { get; set; }

//        public string? DateFormat { get; set; } = "yyyy/MM/dd";
//        public string? NumberFormat { get; set; } = "#,##0.00";

//        // معلومات سياقية للتقرير
//        public string? BranchName { get; set; }
//        public string? DepartmentName { get; set; }
//        public string? PeriodText { get; set; }  // من..إلى
//    }
//}

using Application.Dtos.ExportDtos;

namespace Application.Interfaces.Services
{
    public interface IExportService
    {
        // طرق محسنة مع خيارات أكثر للتخصيص
        Task<ExportFileDto> ExportToExcelAsync<T>(IEnumerable<T> data, ExportOptions options);
        Task<ExportFileDto> ExportToPdfAsync<T>(IEnumerable<T> data, ExportOptions options);

        // طرق مساعدة للاستخدام السريع مع التقارير
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
        public ExportTheme Theme { get; set; } = ExportTheme.Modern;
        public string? DateFormat { get; set; } = "yyyy/MM/dd";
        public string? NumberFormat { get; set; } = "#,##0.00";

        // معلومات سياقية للتقرير
        public string? BranchName { get; set; }
        public string? DepartmentName { get; set; }
        public string? PeriodText { get; set; }  // من..إلى

        public List<string>? NonSummarizedColumns { get; set; } = new List<string>();

    }

    public enum ExportTheme
    {
        Modern,
        Classic,
        Minimal,
        Corporate,
        Financial
    }

    public enum ExportFormat
    {
        Excel,
        PDF
    }
}

