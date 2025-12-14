using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.DocumentTypeDtos;

namespace ArchiveProject.Application.Common.Interfaces
{
    public interface IDocumentTypeService
    {
        Task<Result<DocumentTypeDto>> CreateAsync(CreateDocumentTypeDto dto);

        Task<Result<string>> UpdateAsync(long documentTypeId, UpdateDocumentTypeDto dto);

        Task<Result<string>> DeleteAsync(long documentTypeId);

        Task<Result<DocumentTypeDto>> GetByIdAsync(long documentTypeId);

        Task<Result<IEnumerable<DropDownDto>>> GetDocumentTypesDropdownAsync();

        Task<Result<PaginatedList<DocumentTypeDto>>> GetDocumentTypesAsync(int pageNumber = 1, int pageSize = 10,string? name = null);

        Task<Result<PaginatedList<DocumentTypeDto>>> SearchAsync(DocumentTypeSearchCriteria criteria,int pageNumber = 1, int pageSize = 10);
    }

}
