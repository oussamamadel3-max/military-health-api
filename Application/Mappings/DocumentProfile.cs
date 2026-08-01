using Application.Dtos.DocumentDtos;
using Application.Dtos.ReportDtos;
using AutoMapper;
using Domain.Entities;

namespace Application.Mappings
{
    public class DocumentProfile : Profile
    {
        public DocumentProfile()
        {
            CreateMap<Document, DocumentDto>();
            CreateMap<CreateDocumentDto, Document>()
                .ForMember(dest => dest.DocumentId, opt => opt.Ignore())
                .ForMember(dest => dest.DocumentNumber, opt => opt.Ignore());
            CreateMap<UpdateDocumentDto, Document>();

            CreateMap<Document, DocumentReportRowDto>();
        }
    }
}
