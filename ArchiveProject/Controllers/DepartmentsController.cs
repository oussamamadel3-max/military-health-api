using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DepartmentDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentsController : ControllerBase
    {
        private readonly IDepartmentService _service;

        public DepartmentsController(IDepartmentService service)
        {
            _service = service;
        }

        // Helper لتوحيد الريسبونس
        private static Result<T> Pipe<T>(Result<T> res)
        {
            if (res is null)
                return Result<T>.Failure(new[] { "Null response from service." });

            if (!res.Succeeded || EqualityComparer<T>.Default.Equals(res.Value!, default!))
                return Result<T>.Failure(res.Errors, res.Value, res.Warnings);

            return Result<T>.Success(res.Value!, res.Warnings);
        }

        // POST: api/Departments
        [HttpPost]
        [AuthorizePermission("Department.Create")]
        public async Task<Result<DepartmentDto>> Create([FromBody] CreateDepartmentDto dto)
            => Pipe(await _service.CreateAsync(dto));


        // PUT: api/Departments/{id}
        [HttpPut("{id:long}")]
        [AuthorizePermission("Department.Edit")]
        public async Task<Result<string>> Update(long id, [FromBody] UpdateDepartmentDto dto)
            => Pipe(await _service.UpdateAsync(id, dto));


        // DELETE: api/Departments/{id}
        [HttpDelete("{id:long}")]
        [AuthorizePermission("Department.Delete")]
        public async Task<Result<string>> Delete(long id)
            => Pipe(await _service.DeleteAsync(id));


        // GET: api/Departments/{id}
        [HttpGet("{id:long}")]
        [AuthorizePermission("Department.Read")]
        public async Task<Result<DepartmentDto>> GetById(long id)
            => Pipe(await _service.GetByIdAsync(id));


        // GET: api/Departments/dropdown
        [HttpGet("dropdown")]
        [AuthorizePermission("Department.Read")]
        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
            => Pipe(await _service.GetDepartmentsDropdownAsync());


        // GET: api/Departments/GetAllDepartments?pageNumber=1&pageSize=10&name=&isActive=
        [HttpGet("GetAllDepartments")]
        [AuthorizePermission("Department.Read")]
        public async Task<Result<PaginatedList<DepartmentDto>>> GetAll(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? name = null,
            [FromQuery] bool? isActive = null)
            => Pipe(await _service.GetDepartmentsAsync(pageNumber, pageSize, name, isActive));


        //// POST: api/Departments/search?pageNumber=1&pageSize=10
        //[HttpPost("search")]
        ////[AuthorizePermission("Department.Read")]
        //public async Task<Result<PaginatedList<DepartmentDto>>> Search(
        //    [FromForm] DepartmentSearchCriteria criteria,
        //    [FromForm] int pageNumber = 1,
        //    [FromForm] int pageSize = 10)
        //    => Pipe(await _service.SearchAsync(criteria, pageNumber, pageSize));
    }
}
