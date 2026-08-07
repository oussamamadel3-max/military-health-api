using API.Infrastructure;
using Application.Dtos;
using Application.Dtos.DocumentDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _service;

        public DocumentsController(IDocumentService service)
        {
            _service = service;
        }

        // POST: api/Documents
        [HttpPost]
        [AuthorizePermission("Document.Create")]
        public async Task<IResult> Create([FromForm] CreateDocumentManyDto dto)
        {
            var result = await _service.CreateAsync(dto);
            return ApiResponse.Success("تم إنشاء الوثيقة بنجاح.", result);
        }

        // PUT: api/Documents/{id}
        [AuthorizePermission("Document.Edit")]
        [HttpPut("{id:long}")]
        public async Task<IResult> Update(long id, [FromForm] UpdateDocumentDto dto)
        {
            await _service.UpdateAsync(id, dto);
            return ApiResponse.Success("تم تحديث الوثيقة والمرفقات بنجاح.");
        }

        // DELETE: api/Documents/{id}
        [HttpDelete("{id:long}")]
        [AuthorizePermission("Document.Delete")]
        public async Task<IResult> Delete(long id)
        {
            await _service.DeleteAsync(id);
            return ApiResponse.Success("تم حذف الوثيقة بنجاح.");
        }

        // GET: api/Documents/{id}
        [HttpGet("{id:long}")]
        [AuthorizePermission("Document.Read")]
        public async Task<IResult> GetById(long id)
        {
            var result = await _service.GetByIdAsync(id);
            return ApiResponse.Success("تم جلب بيانات الوثيقة بنجاح.", result);
        }

        // GET: api/Documents/dropdown
        [HttpGet("dropdown")]
        [AuthorizePermission("Document.Read")]
        public async Task<IResult> GetDropdown()
        {
            var result = await _service.GetDocumentsDropdownAsync();
            return ApiResponse.Success("تم جلب قائمة الوثائق المنسدلة.", result);
        }

        // GET: api/Documents/GetAllDocuments?pageNumber=1&pageSize=10&title=
        [HttpGet("GetAllDocuments")]
        [AuthorizePermission("Document.Read")]
        public async Task<IResult> GetAll(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? title = null)
        {
            var result = await _service.GetDocumentsAsync(pageNumber, pageSize, title);
            return ApiResponse.Success("تم جلب قائمة الوثائق بنجاح.", result);
        }

        // POST: api/Documents/search?pageNumber=1&pageSize=10
        [HttpPost("search")]
        [AuthorizePermission("Document.Read")]
        public async Task<IResult> Search(
            [FromForm] DocumentSearchCriteria criteria,
            [FromForm][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromForm][Range(1, 500)] int pageSize = 10)
        {
            var result = await _service.SearchAsync(criteria, pageNumber, pageSize);
            return ApiResponse.Success("تم البحث عن الوثائق بنجاح.", result);
        }
    }
}
