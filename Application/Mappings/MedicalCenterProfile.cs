using Application.Dtos.Centers;
using Domain.Entities.Core;
using Mapster;

namespace Application.Mappings;

public class MedicalCenterProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<MedicalCenter, MedicalCenterListItemDto>()
            .Ignore(dest => dest.UsersCount)
            .Ignore(dest => dest.ClinicsCount);

        config.NewConfig<MedicalCenter, MedicalCenterDetailsDto>()
            .Ignore(dest => dest.UsersCount)
            .Ignore(dest => dest.ClinicsCount);

        config.NewConfig<CreateMedicalCenterDto, MedicalCenter>()
            .Ignore(dest => dest.CenterId)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt)
            .Ignore(dest => dest.DeactivationReason);

        config.NewConfig<UpdateMedicalCenterDto, MedicalCenter>()
            .Ignore(dest => dest.CenterId)
            .Ignore(dest => dest.Code)
            .Ignore(dest => dest.IsActive)
            .Ignore(dest => dest.DeactivationReason)
            .Ignore(dest => dest.CreatedAt)
            .Ignore(dest => dest.UpdatedAt);
    }
}
