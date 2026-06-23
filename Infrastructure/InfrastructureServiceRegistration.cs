using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Application.Interfaces.Services;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Infrastructure.Services.AttachmentService;
using Infrastructure.Services.Auth;
using Infrastructure.Services.AuthServices;
using Infrastructure.Services.Dashboard;
using Infrastructure.Services.FileStorageService;
using Infrastructure.Services.Reports;
using Infrastructure.Services.Sanitization;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection ConfigureInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<ITextInputSanitizer, HtmlTextInputSanitizer>();

        services.AddScoped<AuditInterceptor>();

        #region Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IActorScopeProvider, ActorScopeProvider>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IFieldPermissionRepository, FieldPermissionRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        #endregion

        #region Auth HTTP services
        services.AddScoped<AuthCookieService>();
        services.AddScoped<ICsrfService, CsrfService>();
        #endregion

        #region Authorization services
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

        #region Application services
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IReportsService, ReportsService>();
        #endregion

        return services;
    }
}
