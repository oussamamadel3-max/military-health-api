using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentDtos;

namespace ArchiveProject.Application.Common.Interfaces
{
    public interface IDocumentService
    {
        Task<Result<List<DocumentDto>>> CreateAsync(CreateDocumentManyDto dto);

        Task<Result<string>> UpdateAsync(long documentId, UpdateDocumentDto dto);

        Task<Result<string>> DeleteAsync(long documentId);

        Task<Result<DocumentDto>> GetByIdAsync(long documentId);

        Task<Result<PaginatedList<DocumentDto>>> GetDocumentsAsync(int pageNumber = 1, int pageSize = 10, string? title = null);

        Task<Result<IEnumerable<DropDownDto>>> GetDocumentsDropdownAsync();

        Task<Result<PaginatedList<DocumentDto>>> SearchAsync(DocumentSearchCriteria criteria, int pageNumber = 1, int pageSize = 10);
    }
}
