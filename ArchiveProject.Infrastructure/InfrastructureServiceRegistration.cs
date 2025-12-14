using ArchiveProject.Application.Common.Interfaces;
using ArchiveProject.Application.Common.Interfaces.AttachmentInterfaces;
using ArchiveProject.Application.Common.Interfaces.AuditLogInterfaces;
using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Interfaces.FileStorageInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Infrastructure.Persistence;
using ArchiveProject.Infrastructure.Services;
using ArchiveProject.Infrastructure.Services.AttachmentService;
using ArchiveProject.Infrastructure.Services.AuthServices;
using ArchiveProject.Infrastructure.Services.Dashboard;
using ArchiveProject.Infrastructure.Services.FileStorageService;
using FinanceProject.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiveProject.Infrastructure
{
    public static class InfrastructureServiceRegistration
    {
        public static IServiceCollection ConfigureInfrastructureServices(this IServiceCollection services)
        {
            services.AddScoped<AuditInterceptor>();
            services.AddScoped<IAuditLogService, AuditLogService>();



            #region   === Repositories Authorization  ===
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IRoleService, RoleService>();
            services.AddScoped<IPermissionService, PermissionService>();
            services.AddScoped<IFieldPermissionService, FieldPermissionService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserPermissionService, UserPermissionService>();
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<ITokenService, TokenService>();

            services.AddScoped<FieldPermissionHelper>();
            services.AddScoped<IUserContextService, HttpUserContextService>();
            #endregion

            #region === يمكنك إضافة خدمات المشروع الأخرى هنا ===
            services.AddScoped<IArchiveGroupService, ArchiveGroupService>();
            services.AddScoped<IBranchService, BranchService>();
            services.AddScoped<IDepartmentService, DepartmentService>();
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<IDocumentTypeService, DocumentTypeService>();


            services.AddScoped<IAttachmentService, AttachmentService>();
            services.AddScoped<IFileStorageService, FileStorageService>();


            services.AddScoped<IDashboardService, DashboardService>();

            #endregion


            return services;
        }
    }
}

