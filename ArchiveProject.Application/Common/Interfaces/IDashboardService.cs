using ArchiveProject.Application.Dtos.DashboardDtos;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Common.Interfaces.DashboardInterfaces
{
    public class DashboardFilter : IValidatableObject
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        [Range(1, 500, ErrorMessage = "عدد الأنشطة الأخيرة يجب أن يكون بين 1 و 500.")]
        public int RecentCount { get; set; } = 10;

        [Range(1, 500, ErrorMessage = "عدد المستخدمين الأكثر نشاطاً يجب أن يكون بين 1 و 500.")]
        public int TopUsersCount { get; set; } = 5;

        [Range(1, 120, ErrorMessage = "عدد أشهر النمو يجب أن يكون بين 1 و 120.")]
        public int GrowthMonths { get; set; } = 6;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (From.HasValue && To.HasValue && From > To)
                yield return new ValidationResult("تاريخ البداية يجب ألا يتعدى تاريخ النهاية.", new[] { nameof(From), nameof(To) });
        }
    }

    public interface IDashboardService
    {
        Task<Result<DashboardStatsDto>> GetAsync(DashboardFilter filter);
    }
}
