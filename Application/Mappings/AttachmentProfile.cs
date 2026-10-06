using Application.Dtos;
using Domain.Entities.Core;
using Mapster;

namespace Application.Mappings
{
    public class AttachmentProfile : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<FileAsset, AttachmentReadDto>()
                .Map(dest => dest.AttachmentId, src => src.FileAssetId)
                .Map(dest => dest.FileName, src => src.OriginalName)
                .Map(dest => dest.FileExtension, src => src.Extension)
                .Map(dest => dest.FilePath, src => src.StorageKey)
                .Map(dest => dest.FileSize, src => src.SizeBytes)
                .Map(dest => dest.ContentType, src => src.MimeType)
                .Map(dest => dest.UploadedAt, src => src.CreatedAt.UtcDateTime)
                .Map(dest => dest.CreatedAt, src => src.CreatedAt.UtcDateTime)
                .Ignore(dest => dest.OwnerId)
                .Ignore(dest => dest.OwnerType)
                .Ignore(dest => dest.Description)
                .Ignore(dest => dest.CreatedBy)
                .Ignore(dest => dest.UpdatedAt)
                .Ignore(dest => dest.UpdatedBy)
                .Ignore(dest => dest.FileUrl)
                .Ignore(dest => dest.FileSizeFormatted);

            config.NewConfig<FileAsset, AttachmentLinkDto>()
                .Map(dest => dest.Id, src => src.FileAssetId)
                .Map(dest => dest.FileName, src => src.OriginalName)
                .Ignore(dest => dest.FileUrl);
        }
    }
}
