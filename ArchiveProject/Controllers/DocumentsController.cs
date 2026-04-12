using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _service;

        public DocumentsController(IDocumentService service)
        {
            _service = service;
        }

        private static Result<T> Pipe<T>(Result<T> res)
        {
            if (res is null)
                return Result<T>.Failure(new[] { "Null response from service." });

            if (!res.Succeeded || EqualityComparer<T>.Default.Equals(res.Value!, default!))
                return Result<T>.Failure(res.Errors, res.Value, res.Warnings);

            return Result<T>.Success(res.Value!, res.Warnings);
        }


        // POST: api/Documents
        [HttpPost]
        [AuthorizePermission("Document.Create")]
        public async Task<Result<List<DocumentDto>>> Create([FromForm] CreateDocumentManyDto dto)
            => Pipe(await _service.CreateAsync(dto));


        // PUT: api/Documents/{id}
        [AuthorizePermission("Document.Edit")]
        [HttpPut("{id:long}")]
        public async Task<Result<string>> Update(long id, [FromForm] UpdateDocumentDto dto)
            => Pipe(await _service.UpdateAsync(id, dto));


        // DELETE: api/Documents/{id}
        [HttpDelete("{id:long}")]
        [AuthorizePermission("Document.Delete")]
        public async Task<Result<string>> Delete(long id)
            => Pipe(await _service.DeleteAsync(id));


        // GET: api/Documents/{id}
        [HttpGet("{id:long}")]
        [AuthorizePermission("Document.Read")]
        public async Task<Result<DocumentDto>> GetById(long id)
            => Pipe(await _service.GetByIdAsync(id));


        // GET: api/Documents/dropdown
        [HttpGet("dropdown")]
        [AuthorizePermission("Document.Read")]
        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
            => Pipe(await _service.GetDocumentsDropdownAsync());


        // GET: api/Documents/GetAllDocuments?pageNumber=1&pageSize=10&title=
        [HttpGet("GetAllDocuments")]
        [AuthorizePermission("Document.Read")]
        public async Task<Result<PaginatedList<DocumentDto>>> GetAll(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? title = null)
            => Pipe(await _service.GetDocumentsAsync(pageNumber, pageSize, title));


        // POST: api/Documents/search?pageNumber=1&pageSize=10
        [HttpPost("search")]
        [AuthorizePermission("Document.Read")]
        public async Task<Result<PaginatedList<DocumentDto>>> Search(
            [FromForm] DocumentSearchCriteria criteria,
            [FromForm] int pageNumber = 1,
            [FromForm] int pageSize = 10)
            => Pipe(await _service.SearchAsync(criteria, pageNumber, pageSize));
    }
}
