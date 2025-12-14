using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.ArchiveGroupDtos;

namespace ArchiveProject.Application.Common.Interfaces
{
    public interface IArchiveGroupService
    {
        Task<Result<ArchiveGroupDto>> CreateAsync(CreateArchiveGroupDto dto);

        Task<Result<string>> UpdateAsync(long archiveGroupId, UpdateArchiveGroupDto dto);

        Task<Result<string>> DeleteAsync(long archiveGroupId);

        Task<Result<ArchiveGroupDto>> GetByIdAsync(long archiveGroupId);


        Task<Result<PaginatedList<ArchiveGroupDto>>> GetArchiveGroupsAsync(int pageNumber = 1, int pageSize = 10, string? title = null);

        Task<Result<IEnumerable<DropDownDto>>> GetArchiveGroupsDropdownAsync();

        Task<Result<IEnumerable<ArchiveGroupDto>>> GetArchiveGroupsByDepartmentAsync(long departmentId);

        Task<Result<PaginatedList<ArchiveGroupDto>>> SearchAsync(ArchiveGroupSearchCriteria criteria, int pageNumber = 1, int pageSize = 10);
    }

}
