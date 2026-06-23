using Application.Dtos;
using Application.Dtos.AuthDtos;
using Application.Dtos.DocumentDtos;
using Application.Dtos.ReportDtos;
using Application.ModelsDto.AuditLogDtos;
using Domain.Entities;
using Domain.Entities.Auth;
using AutoMapper;

namespace Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {

            #region === Authorization Mappings ===
            // Role
            CreateMap<Role, RoleDto>().ReverseMap();
            CreateMap<Role, RoleDetailsDto>().ReverseMap();
            CreateMap<RoleStatusUpdateDto, Role>().ReverseMap();

            CreateMap<CreateRoleDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore());

            CreateMap<CreateRoleWithPermissionsDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore());

            CreateMap<UpdateRoleDto, Role>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UserRoleAssociations, opt => opt.Ignore())
                .ForMember(dest => dest.RolePermissions, opt => opt.Ignore())
                .ForMember(dest => dest.RoleFieldPermissions, opt => opt.Ignore());

            // Permission => PermissionWithRolesDto
            CreateMap<Permission, PermissionWithRolesDto>()
                .ForMember(dest => dest.AssignedRoleIds, opt => opt.MapFrom(src => src.RolePermissions.Select(rp => rp.RoleId)))
                .ForMember(dest => dest.AssignedRolesCount, opt => opt.MapFrom(src => src.RolePermissions.Count));

            // Permission => PermissionWithRolesResultDto
            CreateMap<Permission, PermissionWithRolesResultDto>()
                .ForMember(dest => dest.Roles, opt => opt.MapFrom(src => src.RolePermissions.Select(rp => rp.Role)))
                .ForMember(dest => dest.Permissions, opt => opt.MapFrom(src => src));

            #endregion

            // AuditLog
            CreateMap<AuditLog, AuditLogListDto>();
            CreateMap<AuditLog, AuditLogDetailsDto>();
            CreateMap<CreateAuditLogDto, AuditLog>()
                .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(_ => DateTime.Now));


            CreateMap<AuditLog, AuditLogReportRowDto>();


            #region Additional mappings can be added here

            //        Document
            CreateMap<Document, DocumentDto>();
            CreateMap<CreateDocumentDto, Document>()
                .ForMember(dest => dest.DocumentId, opt => opt.Ignore())
                .ForMember(dest => dest.DocumentNumber, opt => opt.Ignore());
            CreateMap<UpdateDocumentDto, Document>();

            CreateMap<Document, DocumentReportRowDto>();


            //      Attachment
            CreateMap<Attachment, AttachmentReadDto>();

            #endregion


        }
    }
}
