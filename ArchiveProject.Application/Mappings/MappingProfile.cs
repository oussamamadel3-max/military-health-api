using ArchiveProject.Application.Dtos;
using ArchiveProject.Application.Dtos.ArchiveGroupDtos;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Application.Dtos.BranchDtos;
using ArchiveProject.Application.Dtos.DepartmentDtos;
using ArchiveProject.Application.Dtos.DocumentDtos;
using ArchiveProject.Application.Dtos.DocumentTypeDtos;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using ArchiveProject.Domain.Entities;
using ArchiveProject.Domain.Entities.Auth;
using AutoMapper;

namespace ArchiveProject.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {

            #region === Authorization Mappings ===
            // User
            CreateMap<User, UserListItemDto>()
                .ForMember(dest => dest.BranchId, opt => opt.MapFrom(src => src.Branch.BranchId))
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.Branch.BranchName))
                .ForMember(dest => dest.DepartmentId, opt => opt.MapFrom(src => src.Department.DepartmentId))
                .ForMember(dest => dest.DepartmentName, opt => opt.MapFrom(src => src.Department.DepartmentName))
                .ReverseMap();

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


            #region Additional mappings can be added here

            //       Branch
            CreateMap<Branch, BranchDto>();
            CreateMap<CreateBranchDto, Branch>();
            CreateMap<UpdateBranchDto, Branch>();

            //      Department
            CreateMap<Department, DepartmentDto>();
            CreateMap<CreateDepartmentDto, Department>();
            CreateMap<UpdateDepartmentDto, Department>();

            //     DocumentType
            CreateMap<DocumentType, DocumentTypeDto>();
            CreateMap<CreateDocumentTypeDto, DocumentType>();
            CreateMap<UpdateDocumentTypeDto, DocumentType>();

            //     ArchiveGroup
            CreateMap<ArchiveGroup, ArchiveGroupDto>();
            CreateMap<CreateArchiveGroupDto, ArchiveGroup>()
                // لو أردت توليد GroupNumber تلقائيًا ممكن ضمه هنا
                .ForMember(dest => dest.ArchiveGroupNumber, opt => opt.Ignore());
            CreateMap<UpdateArchiveGroupDto, ArchiveGroup>();

            //        Document
            CreateMap<Document, DocumentDto>();
            CreateMap<CreateDocumentDto, Document>()
                .ForMember(dest => dest.DocumentId, opt => opt.Ignore())
                .ForMember(dest => dest.DocumentNumber, opt => opt.Ignore());

            CreateMap<UpdateDocumentDto, Document>();

            CreateMap<Attachment, AttachmentReadDto>();

            #endregion


        }
    }
}

