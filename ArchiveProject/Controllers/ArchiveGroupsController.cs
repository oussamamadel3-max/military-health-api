using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.ArchiveGroupDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ArchiveGroupsController : ControllerBase
    {
        private readonly IArchiveGroupService _service;

        public ArchiveGroupsController(IArchiveGroupService service)
        {
            _service = service;
        }

        // 🔹 Helper لتوحيد الريسبونس
        private static Result<T> Pipe<T>(Result<T> result)
        {
            if (result is null)
                return Result<T>.Failure(new[] { "Null response from service." });

            if (!result.Succeeded || EqualityComparer<T>.Default.Equals(result.Value!, default!))
                return Result<T>.Failure(result.Errors, result.Value, result.Warnings);

            return Result<T>.Success(result.Value!, result.Warnings);
        }



        // POST: api/ArchiveGroups
        [HttpPost]
        //[AuthorizePermission("ArchiveGroup.Create")]
        public async Task<Result<ArchiveGroupDto>> Create([FromBody] CreateArchiveGroupDto dto)
        {
            var res = await _service.CreateAsync(dto);
            return Pipe(res);
        }

        // PUT: api/ArchiveGroups/{id}
        [HttpPut("{id:long}")]
        //[AuthorizePermission("ArchiveGroup.Edit")]
        public async Task<Result<string>> Update(long id, [FromBody] UpdateArchiveGroupDto dto)
        {
            var res = await _service.UpdateAsync(id, dto);
            return Pipe(res);
        }

        // DELETE: api/ArchiveGroups/{id}
        [HttpDelete("{id:long}")]
        //[AuthorizePermission("ArchiveGroup.Delete")]
        public async Task<Result<string>> Delete(long id)
        {
            var res = await _service.DeleteAsync(id);
            return Pipe(res);
        }

        // GET: api/ArchiveGroups/{id}
        [HttpGet("{id:long}")]
        //[AuthorizePermission("ArchiveGroup.Read")]
        public async Task<Result<ArchiveGroupDto>> GetById(long id)
        {
            var res = await _service.GetByIdAsync(id);
            return Pipe(res);
        }


        // GET: api/ArchiveGroups/dropdown
        [HttpGet("dropdown")]
        //[AuthorizePermission("ArchiveGroup.Read")]
        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
        {
            var res = await _service.GetArchiveGroupsDropdownAsync();
            return Pipe(res);
        }

        // GET: api/ArchiveGroups/by-department/5
        [HttpGet("by-department/{departmentId:long}")]
        //[AuthorizePermission("ArchiveGroup.Read")]
        public async Task<Result<IEnumerable<ArchiveGroupDto>>> GetByDepartment(long departmentId)
        {
            var res = await _service.GetArchiveGroupsByDepartmentAsync(departmentId);
            if (res.Value == null || !res.Succeeded)
                return Result<IEnumerable<ArchiveGroupDto>>.Failure(res.Errors, null);

            return Result<IEnumerable<ArchiveGroupDto>>.Success(res.Value);
        }


        // GET: api/ArchiveGroups/GetAllArchiveGroups?pageNumber=1&pageSize=10&title=
        [HttpGet("GetAllArchiveGroups")]
        //[AuthorizePermission("ArchiveGroup.Read")]
        public async Task<Result<PaginatedList<ArchiveGroupDto>>> GetAll(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? title = null)
        {
            var res = await _service.GetArchiveGroupsAsync(pageNumber, pageSize, title);
            return Pipe(res);
        }

        // POST: api/ArchiveGroups/search?pageNumber=1&pageSize=10
        [HttpPost("search")]
        public async Task<Result<PaginatedList<ArchiveGroupDto>>> Search(
            [FromForm] ArchiveGroupSearchCriteria criteria,
            [FromForm] int pageNumber = 1,
            [FromForm] int pageSize = 10)
            => Pipe(await _service.SearchAsync(criteria, pageNumber, pageSize));

    }
}
