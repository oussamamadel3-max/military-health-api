using Application.Common.Results;
using Application.Dtos;
using Application.Dtos.DocumentDtos;

namespace Application.Interfaces.Services
{
    public interface IDocumentService
    {
        Task<List<DocumentDto>> CreateAsync(CreateDocumentManyDto dto);
        Task UpdateAsync(long documentId, UpdateDocumentDto dto);
        Task DeleteAsync(long documentId);
        Task<DocumentDto> GetByIdAsync(long documentId);
        Task<PaginatedList<DocumentDto>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, string? title = null);
        Task<IEnumerable<DropDownDto>> GetDocumentsDropdownAsync();
        Task<PaginatedList<DocumentDto>> SearchAsync(DocumentSearchCriteria criteria, int pageNumber = 1, int pageSize = 10);
    }
}
