using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DepartmentDtos;

namespace ArchiveProject.Application.Common.Interfaces
{

    public interface IDepartmentService
    {
        Task<Result<DepartmentDto>> CreateAsync(CreateDepartmentDto dto);

        Task<Result<string>> UpdateAsync(long departmentId, UpdateDepartmentDto dto);

        Task<Result<string>> DeleteAsync(long departmentId);

        Task<Result<DepartmentDto>> GetByIdAsync(long departmentId);

        Task<Result<IEnumerable<DropDownDto>>> GetDepartmentsDropdownAsync();

        Task<Result<PaginatedList<DepartmentDto>>> GetDepartmentsAsync(int pageNumber = 1, int pageSize = 10, string? name = null, bool? isActive = null);

        Task<Result<PaginatedList<DepartmentDto>>> SearchAsync(DepartmentSearchCriteria criteria,int pageNumber = 1, int pageSize = 10);
    }

}
