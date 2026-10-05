using Application.Dtos;
using Domain.Entities;
using Mapster;

namespace Application.Mappings
{
    public class AttachmentProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<Attachment, AttachmentReadDto>();

            config.NewConfig<Attachment, AttachmentLinkDto>()
                .Map(dest => dest.Id, src => src.AttachmentId)
                .Map(dest => dest.FileName, src => src.FileName ?? src.AttachmentId.ToString())
                .Ignore(dest => dest.FileUrl);
        }
    }
}
