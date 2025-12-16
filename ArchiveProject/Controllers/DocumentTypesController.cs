
using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentTypeDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentTypesController : ControllerBase
    {
        private readonly IDocumentTypeService _service;

        public DocumentTypesController(IDocumentTypeService service)
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

        // POST: api/DocumentTypes
        [HttpPost]
        [AuthorizePermission("DocumentType.Create")]
        public async Task<Result<DocumentTypeDto>> Create([FromBody] CreateDocumentTypeDto dto)
            => Pipe(await _service.CreateAsync(dto));


        // PUT: api/DocumentTypes/{id}
        [HttpPut("{id:long}")]
        [AuthorizePermission("DocumentType.Edit")]
        public async Task<Result<string>> Update(long id, [FromBody] UpdateDocumentTypeDto dto)
            => Pipe(await _service.UpdateAsync(id, dto));


        // DELETE: api/DocumentTypes/{id}
        [HttpDelete("{id:long}")]
        [AuthorizePermission("DocumentType.Delete")]
        public async Task<Result<string>> Delete(long id)
            => Pipe(await _service.DeleteAsync(id));


        // GET: api/DocumentTypes/{id}
        [HttpGet("{id:long}")]
        [AuthorizePermission("DocumentType.Read")]
        public async Task<Result<DocumentTypeDto>> GetById(long id)
            => Pipe(await _service.GetByIdAsync(id));


        // GET: api/DocumentTypes/dropdown
        [HttpGet("dropdown")]
        [AuthorizePermission("DocumentType.Read")]
        public async Task<Result<IEnumerable<DropDownDto>>> GetDropdown()
            => Pipe(await _service.GetDocumentTypesDropdownAsync());


        // GET: api/DocumentTypes/GetAllDocumentTypes?pageNumber=1&pageSize=10&name=
        [HttpGet("GetAllDocumentTypes")]
        [AuthorizePermission("DocumentType.Read")]
        public async Task<Result<PaginatedList<DocumentTypeDto>>> GetAll(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? name = null)
            => Pipe(await _service.GetDocumentTypesAsync(pageNumber, pageSize, name));

        // لو بدك ترجع البحث كمان نفس الأسلوب:
        // POST: api/DocumentTypes/search?pageNumber=1&pageSize=10
        //[HttpPost("search")]
        //public async Task<Result<PaginatedList<DocumentTypeDto>>> Search(
        //    [FromForm] DocumentTypeSearchCriteria criteria,
        //    [FromForm] int pageNumber = 1,
        //    [FromForm] int pageSize = 10)
        //    => Pipe(await _service.SearchAsync(criteria, pageNumber, pageSize));
    }
}
