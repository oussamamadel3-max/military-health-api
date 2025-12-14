//using ArchiveProject.Application.Common.Interfaces;
//using ArchiveProject.Application.Common.Results;
//using ArchiveProject.Application.Dtos;
//using ArchiveProject.Application.Dtos.AuthDtos;
//using ArchiveProject.Application.Dtos.BranchDtos;
//using Microsoft.AspNetCore.Mvc;

//namespace ArchiveProject.API.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class BranchController : ControllerBase
//    {
//        private readonly IBranchService _branchService;

//        public BranchController(IBranchService branchService)
//        {
//            _branchService = branchService;
//        }

//        // POST: api/Branch
//        [HttpPost]
//        public async Task<Result<BranchDto>> Create([FromForm] CreateBranchDto dto)
//            => await _branchService.CreateAsync(dto);


//        // PUT: api/Branch/{id}
//        [HttpPut("{id:long}")]
//        public async Task<Result<string>> Update(long id, [FromForm] UpdateBranchDto dto)
//            => await _branchService.UpdateAsync(id, dto);


//        // DELETE: api/Branch/{id}
//        [HttpDelete("{id:long}")]
//        public async Task<Result<string>> Delete(long id)
//            => await _branchService.DeleteAsync(id);


//        // GET: api/Branch/{id}
//        [HttpGet("{id:long}")]
//        public async Task<Result<BranchDto>> GetById(long id)
//        {
//            var branch = await _branchService.GetByIdAsync(id);

//            if (branch.Value == null || !branch.Succeeded)
//            {
//                return Result<BranchDto>.Failure(branch.Errors, null);
//            }
//            return Result<BranchDto>.Success(branch.Value);
//        }




//        // GET: api/Branch/paged?pageNumber=1&pageSize=20&code=&name=
//        [HttpGet("GetAllBranches")]
//        public Task<Result<PaginatedList<BranchDto>>> GetPaged([FromQuery] int pageNumber = 1,[FromQuery] int pageSize = 10,[FromQuery] string? code = null,[FromQuery] string? name = null)
//            => _branchService.GetBranchesAsync(pageNumber, pageSize, code, name);



//        // POST: api/Branch/search-paged?pageNumber=1&pageSize=20
//        [HttpPost("search")]
//        public Task<Result<PaginatedList<BranchDto>>> SearchPaged([FromForm] BranchSearchCriteria criteria,[FromForm] int pageNumber = 1,[FromForm] int pageSize = 20)
//            => _branchService.SearchAsync(criteria, pageNumber, pageSize);



//        // GET: api/Branch/dropdown  (تبقى كما هي)
//        [HttpGet("dropdown")]
//        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
//            => await _branchService.GetBranchesAsync();
//    }
//}

using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.BranchDtos;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BranchController : ControllerBase
    {
        private readonly IBranchService _branchService;

        public BranchController(IBranchService branchService)
        {
            _branchService = branchService;
        }

        // Helper لمعادلة الريسبونس وتوحيده
        private static Result<T> Pipe<T>(Result<T> res)
        {
            if (res is null)
                return Result<T>.Failure(new[] { "Null response from service." });

            if (!res.Succeeded || EqualityComparer<T>.Default.Equals(res.Value!, default!))
                return Result<T>.Failure(res.Errors, res.Value, res.Warnings);

            return Result<T>.Success(res.Value!, res.Warnings);
        }

        // POST: api/Branch
        [HttpPost]
        //[AuthorizePermission("Branch.Create")]
        public async Task<Result<BranchDto>> Create([FromBody] CreateBranchDto dto)
        {
            var result = await _branchService.CreateAsync(dto);
            return Pipe(result);
        }

        // PUT: api/Branch/{id}
        [HttpPut("{id:long}")]
        //[AuthorizePermission("Branch.Edit")]
        public async Task<Result<string>> Update(long id, [FromBody] UpdateBranchDto dto)
        {
            var result = await _branchService.UpdateAsync(id, dto);
            return Pipe(result);
        }

        // DELETE: api/Branch/{id}
        [HttpDelete("{id:long}")]
        //[AuthorizePermission("Branch.Delete")]
        public async Task<Result<string>> Delete(long id)
        {
            var result = await _branchService.DeleteAsync(id);
            return Pipe(result);
        }

        // GET: api/Branch/{id}
        [HttpGet("{id:long}")]
        //[AuthorizePermission("Branch.Read")]
        public async Task<Result<BranchDto>> GetById(long id)
        {
            var result = await _branchService.GetByIdAsync(id);
            return Pipe(result);
        }

        // GET: api/Branch/GetAllBranches?pageNumber=1&pageSize=10&code=&name=
        [HttpGet("GetAllBranches")]
        //[AuthorizePermission("Branch.Read")]
        public async Task<Result<PaginatedList<BranchDto>>> GetPaged(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? code = null,
            [FromQuery] string? name = null)
        {
            var result = await _branchService.GetBranchesAsync(pageNumber, pageSize, code, name);
            return Pipe(result);
        }

        //// POST: api/Branch/search?pageNumber=1&pageSize=10
        //[HttpPost("search")]
        ////[AuthorizePermission("Branch.Read")]
        //public async Task<Result<PaginatedList<BranchDto>>> SearchPaged(
        //    [FromForm] BranchSearchCriteria criteria,
        //    [FromForm] int pageNumber = 1,
        //    [FromForm] int pageSize = 10)
        //{
        //    var result = await _branchService.SearchAsync(criteria, pageNumber, pageSize);
        //    return Pipe(result);
        //}

        // GET: api/Branch/dropdown
        [HttpGet("dropdown")]
        //[AuthorizePermission("Branch.Read")]
        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
        {
            var result = await _branchService.GetBranchesAsync();
            return Pipe(result);
        }
    }
}
