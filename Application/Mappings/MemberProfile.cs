using Application.Dtos.Members;
using Domain.Entities.Patient;
using Mapster;

namespace Application.Mappings;

/// <summary>تحويلات Mapster لملف المنتسب (القائمة والتفاصيل؛ الإنشاء/التعديل يُEnrich يدوياً).</summary>
public class MemberProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Member, MemberListItemDto>()
            .Ignore(dest => dest.BmiCategoryNameAr);

        config.NewConfig<Member, MemberDetailsDto>()
            .Map(dest => dest.CurrentMedicalCenterNameAr, src => src.CurrentMedicalCenter != null ? src.CurrentMedicalCenter.NameAr : null)
            .Map(dest => dest.MaritalStatusNameAr, src => src.MaritalStatus != null ? src.MaritalStatus.NameAr : null)
            .Map(dest => dest.RecruitmentCenterNameAr, src => src.RecruitmentCenter != null ? src.RecruitmentCenter.NameAr : null)
            .Map(dest => dest.BloodTypeNameAr, src => src.BloodType != null ? src.BloodType.NameAr : null)
            .Ignore(dest => dest.BmiCategoryNameAr);
    }
}
