using Application.Common.Permissions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.Audit;
using Application.Services.AttachmentService;
using Application.Services.AuthServices;
using Application.Services.Centers;
using Application.Services.ClinicWorkspace;
using Application.Services.Dashboard;
using Application.Services.Members;
using Application.Services.ReferenceData;
using Application.Services.ReferenceData.Handlers;
using Application.Services.FileStorageService;
using Application.Services.Reports;
using Application.Services.Sanitization;
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
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAccessContextRepository, AccessContextRepository>();
        services.AddScoped<ICrossCenterAccessRepository, CrossCenterAccessRepository>();
        services.AddScoped<IMedicalCenterRepository, MedicalCenterRepository>();
        services.AddScoped<ICenterClinicRepository, CenterClinicRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMemberImportBatchRepository, MemberImportBatchRepository>();
        services.AddScoped<IClinicWorkspaceRepository, ClinicWorkspaceRepository>();
        services.AddScoped<IConsultationRepository, ConsultationRepository>();
        #endregion

        #region Auth HTTP services
        services.AddScoped<AuthCookieService>();
        services.AddScoped<ICsrfService, CsrfService>();
        #endregion

        #region Authorization services
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserAssignmentService, UserAssignmentService>();
        services.AddScoped<IUserClinicAssignmentService, UserClinicAssignmentService>();
        services.AddScoped<IUserEffectiveAccessService, UserEffectiveAccessService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IFieldPermissionService, FieldPermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ILoginLockoutService, LoginLockoutService>();
        services.AddScoped<IUserPermissionService, UserPermissionService>();
        services.AddScoped<IPermissionGate, PermissionGate>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentCenterService, CurrentCenterService>();
        services.AddScoped<IActiveCenterService, ActiveCenterService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<FieldPermissionHelper>();
        services.AddScoped<IUserContextService, HttpUserContextService>();
        #endregion

        #region Application services
        services.AddScoped<ICrossCenterAccessService, CrossCenterAccessService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IExportService, ExportService>();
        services.AddScoped<IReportsService, ReportsService>();
        services.AddScoped<IMedicalCenterService, MedicalCenterService>();
        services.AddScoped<ICenterClinicService, CenterClinicService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IClinicWorkspaceService, ClinicWorkspaceService>();
        services.AddScoped<IClinicAssessmentService, ClinicAssessmentService>();
        services.AddScoped<IEyeExamService, EyeExamService>();
        services.AddScoped<IInternalExamService, InternalExamService>();
        services.AddScoped<ISurgeryExamService, SurgeryExamService>();
        services.AddScoped<IOrthopedicExamService, OrthopedicExamService>();
        services.AddScoped<IEntExamService, EntExamService>();
        services.AddScoped<IConsultationService, ConsultationService>();
        services.AddScoped<IReferenceCatalogHandler, RecruitmentCenterCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, ClinicCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, ResultStatusCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, RequiredActionTypeCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, MaritalStatusCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, BloodTypeCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, ConsultationTypeCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, InvestigationTypeCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, BmiCategoryCatalogHandler>();
        services.AddScoped<IReferenceCatalogHandler, ValidationRuleCatalogHandler>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        #endregion

        return services;
    }
}
