using Application.Common.Permissions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.AuthServices;
using Application.Services.Dashboard;
using Application.Services.FileStorageService;
using Application.Services.Reports;
using Application.Services.Sanitization;
using Application.Services.Core;
using Infrastructure.Repositories;
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
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IOrgUnitTypeRepository, OrgUnitTypeRepository>();
        services.AddScoped<IOrgUnitRepository, OrgUnitRepository>();
        services.AddScoped<IUserOrgScopeRepository, UserOrgScopeRepository>();
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
        services.AddScoped<ILoginLockoutService, LoginLockoutService>();
        services.AddScoped<IUserPermissionService, UserPermissionService>();
        services.AddScoped<IPermissionGate, PermissionGate>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<FieldPermissionHelper>();
        services.AddScoped<IUserContextService, HttpUserContextService>();
        #endregion

        #region Application services
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IReportsService, ReportsService>();
        services.AddScoped<IOrgUnitTypeService, OrgUnitTypeService>();
        services.AddScoped<IOrgUnitService, OrgUnitService>();
        services.AddScoped<IUserOrgScopeService, UserOrgScopeService>();
        services.AddScoped<IOrgScopeResolver, OrgScopeResolver>();
        services.AddScoped<IOrgAccessService, OrgAccessService>();
        #endregion

        return services;
    }
}
