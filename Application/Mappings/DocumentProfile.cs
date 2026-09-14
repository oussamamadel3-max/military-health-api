using Application.Dtos;
using Application.Dtos.DocumentDtos;
using Application.Dtos.ReportDtos;
using Domain.Entities;
using Mapster;

namespace Application.Mappings
{
    public class DocumentProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<Document, DocumentDto>();
            config.NewConfig<CreateDocumentDto, Document>()
                .Ignore(dest => dest.DocumentId)
                .Ignore(dest => dest.DocumentNumber);
            config.NewConfig<UpdateDocumentDto, Document>();

            config.NewConfig<Document, DocumentReportRowDto>()
                .Map(dest => dest.CreatedBy, src => src.User != null ? (src.User.UserName ?? string.Empty) : string.Empty)
                .Map(dest => dest.Title, src => src.Title ?? string.Empty);

            config.NewConfig<Document, DropDownDto>()
                .Map(dest => dest.Id, src => src.DocumentId)
                .Map(dest => dest.Name, src => src.Title);
        }
    }
}
