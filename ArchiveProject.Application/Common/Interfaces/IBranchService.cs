using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.BranchDtos;

namespace ArchiveProject.Application.Common.Interfaces
{
    public interface IBranchService
    {
        Task<Result<BranchDto>> CreateAsync(CreateBranchDto dto);

        Task<Result<string>> UpdateAsync(long branchId, UpdateBranchDto dto);

        Task<Result<string>> DeleteAsync(long branchId);

        Task<Result<BranchDto>> GetByIdAsync(long branchId);

        Task<Result<PaginatedList<BranchDto>>> GetBranchesAsync(int pageNumber = 1, int pageSize = 10, string? code = null, string? name = null);

        Task<Result<IEnumerable<DropDownDto>>> GetBranchesAsync();

        Task<Result<PaginatedList<BranchDto>>> SearchAsync(BranchSearchCriteria criteria, int pageNumber = 1, int pageSize = 10);




    }
}
