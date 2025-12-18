using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos.AuthDtos;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using ArchiveProject.Infrastructure.Persistence;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;


namespace ArchiveProject.Infrastructure.Services.AuthServices;

public class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;
    private readonly MainDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly ICurrentUserService _currentUserService;
    public UserService(UserManager<User> userManager, MainDbContext context, JwtSettings jwtSettings, RoleManager<Role> roleManager, ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _context = context;
        _jwtSettings = jwtSettings;
        _roleManager = roleManager;
        _currentUserService = currentUserService;
    }


    #region الانشاء 
    //public async Task<Result<CreateUserDto>> Create(CreateUserDto createdUser)
    //{
    //    var errors = new List<string>();

    //    // 0) المُنشئ الحالي (مدير الفرع أو SuperAdmin)
    //    var actorUserId = _currentUserService.UserId ?? string.Empty;

    //    // 1) تحقّقات أساسية
    //    if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
    //    if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
    //    if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
    //    if (errors.Count > 0) return Result<CreateUserDto>.Failure(errors, null);

    //    // 2) منع التكرار
    //    if (await _userManager.FindByNameAsync(createdUser.username) is not null)
    //        return Result<CreateUserDto>.Failure(new[] { "اسم المستخدم موجود مسبقاً." }, null);

    //    if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
    //        return Result<CreateUserDto>.Failure(new[] { "البريد الإلكتروني موجود مسبقاً." }, null);

    //    // 3) تجهيز الأدوار المدخلة (Id أو Name) - إزالة الفارغ وتوحيد الحالة
    //    var rawRoles = createdUser.roles
    //        .Where(s => !string.IsNullOrWhiteSpace(s))
    //        .Select(s => s.Trim())
    //        .Distinct(StringComparer.OrdinalIgnoreCase)
    //        .ToList();

    //    var rawUpper = new HashSet<string>(rawRoles.Select(x => x.ToUpperInvariant()));

    //    // 4) جلب الأدوار الفعّالة من القاعدة (Id أو Name)
    //    var rolesFromDb = await _context.Roles
    //        .Where(r =>
    //            (r.Id != null && rawUpper.Contains(r.Id.ToUpper())) ||
    //            (r.Name != null && rawUpper.Contains(r.Name.ToUpper()))
    //        )
    //        .Where(r => r.IsActive)
    //        .Select(r => new { r.Id, r.Name })
    //        .ToListAsync();

    //    // 5) التحقق من أدوار مفقودة/غير فعّالة
    //    var foundUpper = rolesFromDb
    //        .SelectMany(r => new[]
    //        {
    //        (r.Id   ?? string.Empty).ToUpperInvariant(),
    //        (r.Name ?? string.Empty).ToUpperInvariant(),
    //        })
    //        .ToHashSet();

    //    var missing = rawRoles.Where(x => !foundUpper.Contains(x.ToUpperInvariant())).ToList();
    //    if (missing.Any())
    //        return Result<CreateUserDto>.Failure(new[] { $"أدوار غير موجودة أو غير نشطة: {string.Join(", ", missing)}" }, null);

    //    // 6) صلاحيات المُنشئ: فقط SuperAdmin أو BranchManager يمكنه الإنشاء
    //    var actorRoleNames = await _context.UserRoleAssociations
    //        .Where(ura => ura.UserId == actorUserId)
    //        .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.Name!)
    //        .ToListAsync();

    //    bool actorIsSuperAdmin = actorRoleNames.Any(n => n.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
    //    bool actorIsBranchManager = actorRoleNames.Any(n => n.Equals("BranchManager", StringComparison.OrdinalIgnoreCase));

    //    if (!actorIsSuperAdmin && !actorIsBranchManager)
    //        return Result<CreateUserDto>.Failure(new[] { "غير مسموح بإنشاء المستخدمين." }, null);

    //    // جلب بيانات المُنشئ لمعرفة فرعه
    //    var actor = await _userManager.FindByIdAsync(actorUserId);
    //    long? actorBranchId = (actor as User)?.BranchId;

    //    // 7) منطق الفرع/القسم للمستخدم الجديد
    //    long? newBranchId = createdUser.BranchId;
    //    long? newDepartmentId = createdUser.DepartmentId;

    //    if (newDepartmentId.HasValue)
    //    {
    //        // عند تمرير قسم: نتحقق منه، ونعيّن الفرع تلقائيًا من القسم
    //        var dept = await _context.Departments
    //            .AsNoTracking()
    //            .FirstOrDefaultAsync(d => d.DepartmentId == newDepartmentId.Value && d.IsActive);

    //        if (dept is null)
    //            return Result<CreateUserDto>.Failure(new[] { "القسم غير موجود أو غير فعّال." }, null);

    //        newBranchId = dept.BranchId; // 🔥 الفرع يُضبط تلقائيًا من القسم

    //        if (!actorIsSuperAdmin)
    //        {
    //            if (!actorBranchId.HasValue || actorBranchId.Value != dept.BranchId)
    //                return Result<CreateUserDto>.Failure(new[] { "لا يمكنك إنشاء مستخدم لهذا القسم لأنه لا يتبع فرعك." }, null);
    //        }
    //    }
    //    else if (newBranchId.HasValue)
    //    {
    //        // إنشاء مستخدم على مستوى الفرع
    //        var branchExists = await _context.Branchs
    //            .AsNoTracking()
    //            .AnyAsync(b => b.BranchId == newBranchId.Value);

    //        if (!branchExists)
    //            return Result<CreateUserDto>.Failure(new[] { "الفرع غير موجود." }, null);

    //        if (!actorIsSuperAdmin)
    //        {
    //            if (!actorBranchId.HasValue || actorBranchId.Value != newBranchId.Value)
    //                return Result<CreateUserDto>.Failure(new[] { "لا يمكنك إنشاء مستخدم لفرع آخر." }, null);
    //        }
    //    }
    //    else
    //    {
    //        // لا قسم ولا فرع: مسموح فقط لـ SuperAdmin
    //        if (!actorIsSuperAdmin)
    //            return Result<CreateUserDto>.Failure(new[] { "يجب تحديد قسم أو فرع للمستخدم الجديد." }, null);
    //    }

    //    // 8) بدء معاملة (Transaction)
    //    using var transaction = await _context.Database.BeginTransactionAsync();
    //    try
    //    {
    //        // 9) إنشاء المستخدم
    //        var user = new User
    //        {
    //            UserName = createdUser.username,
    //            Email = createdUser.email,
    //            PhoneNumber = createdUser.phone,
    //            IsActive = createdUser.isActive,
    //            CreationDate = DateTime.Now,
    //            FullName = createdUser.fullName,

    //            BranchId = newBranchId,       // مستخدم فرع أو مستنتج من القسم
    //            DepartmentId = newDepartmentId    // موظف قسم إذا موجود
    //        };

    //        var createRes = await _userManager.CreateAsync(user, createdUser.password);
    //        if (!createRes.Succeeded)
    //            return Result<CreateUserDto>.Failure(createRes.Errors.Select(e => e.Description), null);

    //        // 10) ربط الأدوار بالمستخدم (جدول الجمعيات)
    //        var associations = rolesFromDb.Select(r => new UserRoleAssociation
    //        {
    //            UserId = user.Id,
    //            RoleId = r.Id
    //        }).ToList();
    //        _context.UserRoleAssociations.AddRange(associations);

    //        // 11) منح صلاحيات الدور للمستخدم (Permissions النشطة فقط)
    //        var roleIds = rolesFromDb.Select(r => r.Id).ToList();

    //        var rolePermissions = await _context.RolePermissions
    //            .Where(rp => roleIds.Contains(rp.RoleId))
    //            .Include(rp => rp.Permission)
    //            .Where(rp => rp.Permission != null && rp.Permission.IsActive)
    //            .Select(rp => rp.PermissionId)
    //            .Distinct()
    //            .ToListAsync();

    //        var existingUserPermissions = await _context.UserPermissions
    //            .Where(up => up.UserId == user.Id)
    //            .Select(up => up.PermissionId)
    //            .ToListAsync();

    //        var newPermissions = rolePermissions
    //            .Where(p => !existingUserPermissions.Contains(p))
    //            .Select(p => new UserPermission { UserId = user.Id, PermissionId = p })
    //            .ToList();

    //        if (newPermissions.Any())
    //            _context.UserPermissions.AddRange(newPermissions);

    //        // 12) حفظ المعاملة
    //        await _context.SaveChangesAsync();
    //        await transaction.CommitAsync();

    //        // 13) إعادة القيم النهائية للفرع/القسم في الـ DTO
    //        createdUser.BranchId = newBranchId;
    //        createdUser.DepartmentId = newDepartmentId;

    //        return Result<CreateUserDto>.Success(createdUser);
    //    }
    //    catch
    //    {
    //        await transaction.RollbackAsync();
    //        throw;
    //    }
    //}

    public async Task<Result<CreateUserDto>> Create(CreateUserDto createdUser)
    {
        var errors = new List<string>();

        var (actor, actorBranchId, actorIsSuperAdmin) = await GetActorAsync();

        // تحقّقات أساسية
        if (string.IsNullOrWhiteSpace(createdUser.username)) errors.Add("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.email)) errors.Add("البريد الإلكتروني مطلوب.");
        if (string.IsNullOrWhiteSpace(createdUser.password)) errors.Add("كلمة المرور مطلوبة.");
        if (errors.Count > 0) return Result<CreateUserDto>.Failure(errors, null);

        // منع التكرار
        if (await _userManager.FindByNameAsync(createdUser.username) is not null)
            return Result<CreateUserDto>.Failure(new[] { "اسم المستخدم موجود مسبقاً." }, null);
        if (await _userManager.FindByEmailAsync(createdUser.email) is not null)
            return Result<CreateUserDto>.Failure(new[] { "البريد الإلكتروني موجود مسبقاً." }, null);

        // جلب الأدوار الفعالة (كما عندك)
        var rawRoles = createdUser.roles.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rawUpper = rawRoles.Select(x => x.ToUpperInvariant()).ToHashSet();
        var rolesFromDb = await _context.Roles
            .Where(r => (r.Id != null && rawUpper.Contains(r.Id.ToUpper())) ||
                        (r.Name != null && rawUpper.Contains(r.Name.ToUpper())))
            .Where(r => r.IsActive)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        var foundUpper = rolesFromDb
            .SelectMany(r => new[] { (r.Id ?? "").ToUpperInvariant(), (r.Name ?? "").ToUpperInvariant() })
            .ToHashSet();
        var missing = rawRoles.Where(x => !foundUpper.Contains(x.ToUpperInvariant())).ToList();
        if (missing.Any())
            return Result<CreateUserDto>.Failure(new[] { $"أدوار غير موجودة أو غير نشطة: {string.Join(", ", missing)}" }, null);

        // السماح فقط: SuperAdmin أو مدير فرع (مستخدم فرع = عنده BranchId ولا يملك DepartmentId)
        var actorIsBranchManager = !actorIsSuperAdmin && actorBranchId.HasValue && !actor.DepartmentId.HasValue;
        if (!actorIsSuperAdmin && !actorIsBranchManager)
            return Result<CreateUserDto>.Failure(new[] { "غير مسموح بإنشاء المستخدمين." }, null);

        // منطق الفرع/القسم للمستخدم الجديد
        long? newBranchId = createdUser.BranchId;
        long? newDepartmentId = createdUser.DepartmentId;

        if (newDepartmentId.HasValue)
        {
            var dept = await _context.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DepartmentId == newDepartmentId.Value && d.IsActive);
            if (dept is null) return Result<CreateUserDto>.Failure(new[] { "القسم غير موجود أو غير فعّال." }, null);

            newBranchId = dept.BranchId; // يُستنتج من القسم

            if (!actorIsSuperAdmin)
            {
                if (!actorBranchId.HasValue || actorBranchId.Value != dept.BranchId)
                    return Result<CreateUserDto>.Failure(new[] { "لا يمكنك إنشاء مستخدم لهذا القسم لأنه لا يتبع فرعك." }, null);
            }
        }
        else if (newBranchId.HasValue)
        {
            var branchExists = await _context.Branchs.AsNoTracking().AnyAsync(b => b.BranchId == newBranchId.Value);
            if (!branchExists) return Result<CreateUserDto>.Failure(new[] { "الفرع غير موجود." }, null);

            if (!actorIsSuperAdmin && (!actorBranchId.HasValue || actorBranchId.Value != newBranchId.Value))
                return Result<CreateUserDto>.Failure(new[] { "لا يمكنك إنشاء مستخدم لفرع آخر." }, null);
        }
        else
        {
            if (!actorIsSuperAdmin)
                return Result<CreateUserDto>.Failure(new[] { "يجب تحديد قسم أو فرع للمستخدم الجديد." }, null);
        }

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = new User
            {
                UserName = createdUser.username,
                Email = createdUser.email,
                PhoneNumber = createdUser.phone,
                IsActive = createdUser.isActive,
                CreationDate = DateTime.Now,
                FullName = createdUser.fullName,
                BranchId = newBranchId,
                DepartmentId = newDepartmentId
            };

            var createRes = await _userManager.CreateAsync(user, createdUser.password);
            if (!createRes.Succeeded)
                return Result<CreateUserDto>.Failure(createRes.Errors.Select(e => e.Description), null);

            // ربط أدوار
            var associations = rolesFromDb.Select(r => new UserRoleAssociation { UserId = user.Id, RoleId = r.Id }).ToList();
            _context.UserRoleAssociations.AddRange(associations);

            // صلاحيات الأدوار
            var roleIds = rolesFromDb.Select(r => r.Id).ToList();
            var rolePermissions = await _context.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Include(rp => rp.Permission)
                .Where(rp => rp.Permission != null && rp.Permission.IsActive)
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync();

            var existingUserPermissions = await _context.UserPermissions
                .Where(up => up.UserId == user.Id)
                .Select(up => up.PermissionId)
                .ToListAsync();

            var newPermissions = rolePermissions
                .Where(p => !existingUserPermissions.Contains(p))
                .Select(p => new UserPermission { UserId = user.Id, PermissionId = p })
                .ToList();

            if (newPermissions.Any()) _context.UserPermissions.AddRange(newPermissions);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            createdUser.BranchId = newBranchId;
            createdUser.DepartmentId = newDepartmentId;
            return Result<CreateUserDto>.Success(createdUser);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region       التعديل الكامل    
    //public async Task<Result<string>> Update(string id, UpdateUserDto updatedUser)
    //{
    //    var errors = new List<string>();

    //    // 1) جلب المستخدم
    //    var user = await _userManager.FindByIdAsync(id);
    //    if (user == null)
    //        return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

    //    // 2) هل المستخدم SuperAdmin (من جداول الربط المخصصة لديك)
    //    var isSuperAdmin = await _context.UserRoleAssociations
    //        .Where(ura => ura.UserId == user.Id)
    //        .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.Name)
    //        .AnyAsync(n => n != null && n.ToLower() == "superadmin");

    //    // 3) تعديل الإيميل (مع التحقق عبر UserManager)
    //    if (!string.IsNullOrWhiteSpace(updatedUser.Email) && !string.Equals(updatedUser.Email, user.Email, StringComparison.OrdinalIgnoreCase))
    //    {
    //        // منع التكرار اليدوي لرسالة عربية أوضح
    //        var emailDup = await _userManager.FindByEmailAsync(updatedUser.Email);
    //        if (emailDup != null && emailDup.Id != user.Id)
    //            return Result<string>.Failure(new[] { "البريد الإلكتروني مستخدم من قبل حساب آخر." }, null);

    //        var setEmail = await _userManager.SetEmailAsync(user, updatedUser.Email);
    //        if (!setEmail.Succeeded)
    //            return Result<string>.Failure(setEmail.Errors.Select(e => e.Description), null);
    //    }

    //    // 4) تعديل اسم المستخدم (مع التحقق عبر UserManager)
    //    if (!string.IsNullOrWhiteSpace(updatedUser.UserName) && !string.Equals(updatedUser.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
    //    {
    //        var userNameDup = await _userManager.FindByNameAsync(updatedUser.UserName);
    //        if (userNameDup != null && userNameDup.Id != user.Id)
    //            return Result<string>.Failure(new[] { "اسم المستخدم مستخدم من قبل حساب آخر." }, null);

    //        var setUserName = await _userManager.SetUserNameAsync(user, updatedUser.UserName);
    //        if (!setUserName.Succeeded)
    //            return Result<string>.Failure(setUserName.Errors.Select(e => e.Description), null);
    //    }

    //    // 5) منطق الفرع / القسم
    //    //   الحالات المسموحة:
    //    //   - DepartmentId فقط (موظف قسم) => يُستنتج BranchId من القسم
    //    //   - BranchId فقط (مستخدم فرع)
    //    //   - لا شيء (مسموح فقط إذا SuperAdmin)
    //    //   - كلاهما مع تطابق (القسم يتبع نفس الفرع)
    //    long? newDepartmentId = updatedUser.DepartmentId;
    //    long? newBranchId = updatedUser.BranchId;

    //    if (newDepartmentId.HasValue)
    //    {
    //        // التحقق من القسم وفعاليته
    //        var dept = await _context.Departments.AsNoTracking()
    //            .FirstOrDefaultAsync(d => d.DepartmentId == newDepartmentId.Value && d.IsActive);

    //        if (dept == null)
    //            return Result<string>.Failure(new[] { "القسم المطلوب غير موجود أو غير فعّال." }, null);

    //        // إن لم يُرسل BranchId نضبطه من القسم
    //        if (!newBranchId.HasValue)
    //            newBranchId = dept.BranchId;
    //        else if (newBranchId.Value != dept.BranchId)
    //            return Result<string>.Failure(new[] { "القسم لا يتبع الفرع الممرّر." }, null);

    //        // تعيين قيم المستخدم
    //        user.DepartmentId = newDepartmentId;
    //        user.BranchId = newBranchId;     // للتناسق (لو كنت تخزنه مع موظفي القسم)
    //    }
    //    else if (newBranchId.HasValue)
    //    {
    //        // مستخدم فرع: فرع موجود، وقسم = null
    //        var branchExists = await _context.Branchs.AsNoTracking()
    //            .AnyAsync(b => b.BranchId == newBranchId.Value);

    //        if (!branchExists)
    //            return Result<string>.Failure(new[] { "الفرع المطلوب غير موجود." }, null);

    //        user.BranchId = newBranchId;
    //        user.DepartmentId = null;
    //    }
    //    else
    //    {
    //        // لم تُرسل فرع ولا قسم
    //        if (!isSuperAdmin)
    //            return Result<string>.Failure(new[] { "يجب تحديد الفرع أو القسم (مسموح الإلغاء فقط لحسابات SuperAdmin)." }, null);

    //        // SuperAdmin: مسموح أن تكون null/null
    //        user.BranchId = null;
    //        user.DepartmentId = null;
    //    }

    //    // 6) حالة التفعيل/التعطيل
    //    bool invalidateTokens = false;
    //    if (updatedUser.IsActive.HasValue && updatedUser.IsActive.Value != user.IsActive)
    //    {
    //        user.IsActive = updatedUser.IsActive.Value;

    //        // لو تم التعطيل => ابطال كل الـ Refresh Tokens + تغيير SecurityStamp ليخرج من الجلسات
    //        if (!user.IsActive)
    //            invalidateTokens = true;
    //    }

    //    // 7) تحديث المستخدم
    //    var updateRes = await _userManager.UpdateAsync(user);
    //    if (!updateRes.Succeeded)
    //        return Result<string>.Failure(updateRes.Errors.Select(e => e.Description), null);

    //    // 8) إبطال الجلسات عند التعطيل
    //    if (invalidateTokens)
    //    {
    //        await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id); // تُحدّث SecurityStamp وتُلغي كل Refresh Tokens. :contentReference[oaicite:0]{index=0}
    //    }

    //    return Result<string>.Success("تم التحديث بنجاح");
    //}

    public async Task<Result<string>> Update(string id, UpdateUserDto updatedUser)
    {
        var (actor, actorBranchId, actorIsSuperAdmin) = await GetActorAsync();

        // الهدف
        var target = await _context.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                Entity = u,
                TargetBranch = (long?)u.BranchId ?? (u.DepartmentId != null ? (long?)u.Department.BranchId : null)
            })
            .FirstOrDefaultAsync();

        if (target is null) return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        // نطاق الفرع
        if (!actorIsSuperAdmin)
        {
            if (!actorBranchId.HasValue || target.TargetBranch != actorBranchId.Value)
                return Result<string>.Failure(new[] { "لا يمكنك تعديل مستخدم خارج فرعك." }, null);
        }

        var user = target.Entity;

        // تعديل Email/Username (كما عندك)
        if (!string.IsNullOrWhiteSpace(updatedUser.Email) &&
            !string.Equals(updatedUser.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailDup = await _userManager.FindByEmailAsync(updatedUser.Email);
            if (emailDup != null && emailDup.Id != user.Id)
                return Result<string>.Failure(new[] { "البريد الإلكتروني مستخدم من قبل حساب آخر." }, null);

            var setEmail = await _userManager.SetEmailAsync(user, updatedUser.Email);
            if (!setEmail.Succeeded) return Result<string>.Failure(setEmail.Errors.Select(e => e.Description), null);
        }

        if (!string.IsNullOrWhiteSpace(updatedUser.UserName) &&
            !string.Equals(updatedUser.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var userNameDup = await _userManager.FindByNameAsync(updatedUser.UserName);
            if (userNameDup != null && userNameDup.Id != user.Id)
                return Result<string>.Failure(new[] { "اسم المستخدم مستخدم من قبل حساب آخر." }, null);

            var setUserName = await _userManager.SetUserNameAsync(user, updatedUser.UserName);
            if (!setUserName.Succeeded) return Result<string>.Failure(setUserName.Errors.Select(e => e.Description), null);
        }

        // نقل/تعديل الفرع/القسم
        long? newBranchId = updatedUser.BranchId;
        long? newDepartmentId = updatedUser.DepartmentId;

        if (newDepartmentId.HasValue)
        {
            var dept = await _context.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DepartmentId == newDepartmentId.Value && d.IsActive);
            if (dept == null) return Result<string>.Failure(new[] { "القسم المطلوب غير موجود أو غير فعّال." }, null);

            // لغير السوبر، يجب أن يكون القسم ضمن فرعه
            if (!actorIsSuperAdmin && (!actorBranchId.HasValue || dept.BranchId != actorBranchId.Value))
                return Result<string>.Failure(new[] { "لا يمكنك نقل المستخدم إلى قسم خارج فرعك." }, null);

            newBranchId = dept.BranchId;
            user.DepartmentId = newDepartmentId;
            user.BranchId = newBranchId;
        }
        else if (newBranchId.HasValue)
        {
            var branchExists = await _context.Branchs.AsNoTracking().AnyAsync(b => b.BranchId == newBranchId.Value);
            if (!branchExists) return Result<string>.Failure(new[] { "الفرع المطلوب غير موجود." }, null);

            if (!actorIsSuperAdmin && (!actorBranchId.HasValue || actorBranchId.Value != newBranchId.Value))
                return Result<string>.Failure(new[] { "لا يمكنك نقل المستخدم إلى فرع آخر." }, null);

            user.BranchId = newBranchId;
            user.DepartmentId = null;
        }
        else
        {
            // null/null مسموحة فقط للسوبر
            if (!actorIsSuperAdmin)
                return Result<string>.Failure(new[] { "يجب تحديد الفرع أو القسم (مسموح الإلغاء فقط لحسابات SuperAdmin)." }, null);

            user.BranchId = null;
            user.DepartmentId = null;
        }

        // تفعيل/تعطيل
        bool invalidateTokens = false;
        if (updatedUser.IsActive.HasValue && updatedUser.IsActive.Value != user.IsActive)
        {
            user.IsActive = updatedUser.IsActive.Value;
            if (!user.IsActive) invalidateTokens = true;
        }

        var updateRes = await _userManager.UpdateAsync(user);
        if (!updateRes.Succeeded) return Result<string>.Failure(updateRes.Errors.Select(e => e.Description), null);

        if (invalidateTokens)
            await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id);

        return Result<string>.Success("تم التحديث بنجاح");
    }

    #endregion

    #region         الحذف
    //public async Task<Result<string>> Delete(string userId)
    //{
    //    var errorList = new List<string>();

    //    var user = await _userManager.FindByIdAsync(userId);

    //    if (user == null)
    //    {
    //        errorList.Add("المستخدم غير موجود");
    //        return Result<string>.Failure(errorList, null);
    //    }

    //    var result = await _userManager.DeleteAsync(user);

    //    if (!result.Succeeded)
    //    {
    //        errorList.AddRange(result.Errors.Select(e => e.Description));
    //        return Result<string>.Failure(errorList, null);
    //    }

    //    return Result<string>.Success("تم الحذف بنجاح");
    //}

    public async Task<Result<string>> Delete(string userId)
    {
        var (actor, actorBranchId, actorIsSuperAdmin) = await GetActorAsync();

        var target = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                Entity = u,
                TargetBranch = (long?)u.BranchId ?? (u.DepartmentId != null ? (long?)u.Department.BranchId : null)
            })
            .FirstOrDefaultAsync();

        if (target is null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        if (!actorIsSuperAdmin)
        {
            if (!actorBranchId.HasValue || target.TargetBranch != actorBranchId.Value)
                return Result<string>.Failure(new[] { "لا يمكنك حذف مستخدم خارج فرعك." }, null);
        }

        var result = await _userManager.DeleteAsync(target.Entity);
        if (!result.Succeeded)
            return Result<string>.Failure(result.Errors.Select(e => e.Description), null);

        return Result<string>.Success("تم الحذف بنجاح");
    }

    #endregion

    #region  جلب الكل 
    public async Task<Result<PaginatedList<UserListItemDto>>> GetUsersListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        // === تحديد النطاق بحسب المستخدم الحالي ===
        var currentUserId = _currentUserService.UserId ?? string.Empty;

        var roleNorms = await _context.UserRoleAssociations
            .Where(ura => ura.UserId == currentUserId)
            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .ToListAsync();

        bool isSuperAdmin = roleNorms.Any(r => r == "SUPERADMIN");
        bool isBranchManger = roleNorms.Any(r => r == "BRANCHMANAGER");

        var actor = await _context.Users.AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.BranchId, u.DepartmentId })
            .FirstOrDefaultAsync();

        IQueryable<User> baseQuery = _context.Users.AsNoTracking();

        // 🧭 تطبيق النطاق
        if (!isSuperAdmin)
        {
            if (isBranchManger && actor?.BranchId != null)
            {
                baseQuery = baseQuery.Where(u => u.BranchId == actor.BranchId);
            }
            else if (actor?.DepartmentId != null)
            {
                var depId = actor.DepartmentId.Value;
                baseQuery = baseQuery.Where(u => u.DepartmentId == depId || u.Id == currentUserId);
            }
            else
            {
                // إن لم يكن له قسم/فرع، يَرى نفسه فقط
                baseQuery = baseQuery.Where(u => u.Id == currentUserId);
            }
        }
        baseQuery = baseQuery.Where(u => u.Id != currentUserId);

        // 🔎 بحث موحّد: الاسم (FullName إن وجد، وإلا UserName) أو الإيميل
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            baseQuery = baseQuery.Where(u =>
                EF.Functions.Like((u.FullName ?? u.UserName)!, $"%{s}%") ||
                (u.Email != null && EF.Functions.Like(u.Email, $"%{s}%"))
            );
        }

        // 🟢 فلترة بالحالة
        if (isActive.HasValue)
            baseQuery = baseQuery.Where(u => u.IsActive == isActive.Value);

        baseQuery = baseQuery.OrderBy(u => u.UserName);

        var totalCount = await baseQuery.CountAsync();

        var usersPage = await baseQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                FullName = u.FullName ?? u.UserName,
                u.UserName,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.BranchId,
                BranchName = u.Branch != null ? u.Branch.BranchName : null,
                u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.DepartmentName : null,
                u.CreationDate
            })
            .ToListAsync();

        var emptyPage = new PaginatedList<UserListItemDto>(new List<UserListItemDto>(), totalCount, pageNumber, pageSize);
        if (usersPage.Count == 0)
            return Result<PaginatedList<UserListItemDto>>.Success(emptyPage);

        var userIds = usersPage.Select(u => u.Id).ToList();

        // الأدوار لكل مستخدم
        var rolesByUser = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => userIds.Contains(ura.UserId))
            .Select(ura => new { ura.UserId, ura.RoleId, RoleName = ura.Role.Name })
            .ToListAsync();

        var rolesLookup = rolesByUser
            .GroupBy(x => x.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new RoleLiteDto(x.RoleId, x.RoleName ?? string.Empty)).ToList()
            );

        var roleIdsOnPage = rolesByUser.Select(x => x.RoleId).Distinct().ToList();

        // صلاحيات الأدوار
        var rolePermsLookup = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIdsOnPage.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId, PermissionName = rp.Permission.Name })
            .GroupBy(x => x.RoleId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Where(x => x.PermissionName != null)
                      .Select(x => new PermissionLiteDto(x.PermissionId.ToString(), x.PermissionName!))
                      .ToList()
            );

        // صلاحيات المستخدم المباشرة
        var directPermsLookup = await _context.UserPermissions
            .AsNoTracking()
            .Where(up => userIds.Contains(up.UserId))
            .Select(up => new { up.UserId, up.PermissionId, PermissionName = up.Permission.Name })
            .GroupBy(x => x.UserId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.Where(x => x.PermissionId.HasValue && x.PermissionName != null)
                      .Select(x => new PermissionLiteDto(x.PermissionId!.Value.ToString(), x.PermissionName!))
                      .ToList()
            );

        // تركيب الـ DTO
        var items = usersPage.Select(u =>
        {
            var userRoles = rolesLookup.TryGetValue(u.Id, out var rr) ? rr : new List<RoleLiteDto>();
            var permMap = new Dictionary<string, PermissionLiteDto>(StringComparer.OrdinalIgnoreCase);

            if (directPermsLookup.TryGetValue(u.Id, out var direct))
                foreach (var p in direct) permMap[p.PermissionId] = p;

            foreach (var r in userRoles)
                if (rolePermsLookup.TryGetValue(r.RoleId, out var rperms))
                    foreach (var p in rperms) permMap[p.PermissionId] = p;

            return new UserListItemDto
            {
                userId = u.Id,
                fullName = u.FullName ?? string.Empty,
                username = u.UserName ?? string.Empty,
                email = u.Email ?? string.Empty,
                phone = u.PhoneNumber,
                isActive = u.IsActive,
                BranchId = u.BranchId,
                BranchName = u.BranchName,
                DepartmentId = u.DepartmentId,
                DepartmentName = u.DepartmentName,
                createdAt = u.CreationDate,
                rolesCount = userRoles.Count,
                permissionsCount = permMap.Count,
                roles = userRoles,
                Permissions = permMap.Values
                                       .OrderBy(p => p.PermissionName, StringComparer.OrdinalIgnoreCase)
                                       .ToList()
            };
        }).ToList();

        var page = new PaginatedList<UserListItemDto>(items, totalCount, pageNumber, pageSize);
        return Result<PaginatedList<UserListItemDto>>.Success(page);
    }
    #endregion

    #region     جلب حسب المعرف 
    public async Task<Result<UserListItemDto>> GetUserAsync(string id)
    {
        var (actor, actorBranchId, actorIsSuperAdmin) = await GetActorAsync();

        IQueryable<User> q = _context.Users.AsNoTracking();

        if (!actorIsSuperAdmin)
        {
            if (!actorBranchId.HasValue || actor.DepartmentId.HasValue)
                return Result<UserListItemDto>.Failure(new[] { "غير مسموح بالوصول." }, null);

            q = q.Where(InBranch(actorBranchId.Value));
        }

        var user = await q
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.BranchId,
                BranchName = u.Branch != null ? u.Branch.BranchName : null,
                u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.DepartmentName : null,
                u.CreationDate,
                FullName = u.FullName
            })
            .FirstOrDefaultAsync();

        if (user == null)
            return Result<UserListItemDto>.Failure(new[] { "المستخدم غير موجود" }, null);

        var roles = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == user.Id)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty))
            .ToListAsync();

        var roleIds = roles.Select(r => r.RoleId).ToList();

        var rolePermissions = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.PermissionId, rp.Permission.Name })
            .ToListAsync();

        var directPermissions = await _context.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == user.Id)
            .Select(up => new { up.PermissionId, up.Permission.Name })
            .ToListAsync();

        var permissionPairs = directPermissions
            .Where(p => p.PermissionId.HasValue && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new { Id = p.PermissionId!.Value, Name = p.Name! })
            .Concat(
                rolePermissions
                    .Where(p => p.PermissionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new { Id = p.PermissionId, Name = p.Name! })
            );

        var permissionMap = permissionPairs
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<Guid>.Default);

        var permissionsLite = permissionMap
            .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new PermissionLiteDto(kvp.Key.ToString(), kvp.Value))
            .ToList();

        var dto = new UserListItemDto
        {
            userId = user.Id,
            fullName = user.FullName ?? user.UserName ?? string.Empty,
            username = user.UserName ?? string.Empty,
            email = user.Email ?? string.Empty,
            phone = user.PhoneNumber,
            isActive = user.IsActive,
            BranchId = user.BranchId,
            BranchName = user.BranchName,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.DepartmentName,
            createdAt = user.CreationDate,
            roles = roles,
            rolesCount = roles.Count,
            permissionsCount = permissionsLite.Count,
            Permissions = permissionsLite
        };

        return Result<UserListItemDto>.Success(dto);
    }

    #endregion

    #region     التعديل الجزئي 
    //public async Task<Result<UserListItemDto>> UpdateUserAsync(string id, UpdateUserPatchRequest dto)
    //{
    //    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
    //    if (user is null)
    //        return Result<UserListItemDto>.Failure(new[] { "المستخدم غير موجود" }, null);

    //    // تحققات uniqueness عند تعديل username/email
    //    if (!string.IsNullOrWhiteSpace(dto.username) && !string.Equals(dto.username, user.UserName, StringComparison.OrdinalIgnoreCase))
    //    {
    //        var existsUserName = await _userManager.FindByNameAsync(dto.username);
    //        if (existsUserName is not null && existsUserName.Id != user.Id)
    //            return Result<UserListItemDto>.Failure(new[] { "اسم المستخدم موجود مسبقا." }, null);
    //    }

    //    if (!string.IsNullOrWhiteSpace(dto.email) && !string.Equals(dto.email, user.Email, StringComparison.OrdinalIgnoreCase))
    //    {
    //        var existsEmail = await _userManager.FindByEmailAsync(dto.email);
    //        if (existsEmail is not null && existsEmail.Id != user.Id)
    //            return Result<UserListItemDto>.Failure(new[] { "الايميل موجود مسبقا." }, null);
    //    }
    //    // تعديل الحقول (PATCH)
    //    if (dto.fullName is not null) user.FullName = dto.fullName;
    //    if (dto.username is not null) user.UserName = dto.username;
    //    if (dto.email is not null) user.Email = dto.email;
    //    if (dto.phone is not null) user.PhoneNumber = dto.phone;
    //    if (dto.isActive.HasValue) user.IsActive = dto.isActive.Value;

    //    // حدث المستخدم عبر Identity (يحافظ على قواعد Identity)
    //    var updRes = await _userManager.UpdateAsync(user);
    //    if (!updRes.Succeeded)
    //        return Result<UserListItemDto>.Failure(updRes.Errors.Select(e => e.Description), null);

    //    // الأدوار:
    //    // - null  => لا تعديل
    //    // - empty => إزالة كل الأدوار
    //    // - values=> استبدال كامل (نحسب toAdd/toRemove)
    //    if (dto.roles != null)
    //    {
    //        // نحصل الأدوار الحالية من جدول الربط
    //        var current = await _context.UserRoleAssociations
    //            .Where(ura => ura.UserId == user.Id)
    //            .ToListAsync();

    //        // حوّل المدخلات إلى RoleIds نهائية:
    //        // أولوية roleId إن وُجد، وإلا نحلّ بالاسم.
    //        var requested = (dto.roles ?? new()).Where(r => r is not null).ToList();

    //        // IDs مباشرة
    //        var incomingIds = requested
    //            .Where(r => !string.IsNullOrWhiteSpace(r.RoleId))
    //            .Select(r => r.RoleId)
    //            .ToList();

    //        // أسماء نحتاج نحلها إلى IDs
    //        var incomingNames = requested
    //            .Where(r => string.IsNullOrWhiteSpace(r.RoleId) && !string.IsNullOrWhiteSpace(r.RoleName))
    //            .Select(r => r.RoleName.Trim())
    //            .Distinct(StringComparer.OrdinalIgnoreCase)
    //            .ToList();

    //        // حلّ الأسماء إلى IDs
    //        var rolesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    //        if (incomingNames.Any())
    //        {
    //            var foundByName = await _context.Roles
    //                .Where(r => r.Name != null && incomingNames.Contains(r.Name))
    //                .Select(r => new { r.Id, r.Name })
    //                .ToListAsync();

    //            var validByName = foundByName.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
    //            rolesByName = validByName.ToDictionary(x => x.Name!, x => x.Id, StringComparer.OrdinalIgnoreCase);

    //            var missing = incomingNames.Where(n => !rolesByName.ContainsKey(n)).ToList();
    //            if (missing.Any())
    //                return Result<UserListItemDto>.Failure(new[] { $"بعض الأدوار غير موجودة: {string.Join(", ", missing)}" }, null);
    //        }

    //        var finalIncomingIds = new HashSet<string>(incomingIds, StringComparer.OrdinalIgnoreCase);
    //        foreach (var kv in rolesByName) finalIncomingIds.Add(kv.Value);

    //        // احسب ما يجب إضافته/إزالته
    //        var currentIds = new HashSet<string>(current.Select(c => c.RoleId), StringComparer.OrdinalIgnoreCase);

    //        var toAdd = finalIncomingIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).ToList();
    //        var toRemove = currentIds.Except(finalIncomingIds, StringComparer.OrdinalIgnoreCase).ToList();

    //        if (toRemove.Any())
    //        {
    //            var removeRows = current.Where(c => toRemove.Contains(c.RoleId, StringComparer.OrdinalIgnoreCase)).ToList();
    //            _context.UserRoleAssociations.RemoveRange(removeRows);
    //        }

    //        if (toAdd.Any())
    //        {
    //            var addRows = toAdd.Select(rid => new UserRoleAssociation
    //            {
    //                UserId = user.Id,
    //                RoleId = rid
    //            });
    //            await _context.UserRoleAssociations.AddRangeAsync(addRows);
    //        }

    //        await _context.SaveChangesAsync();
    //    }
    //    // اعداد الخرج: roles + permissions
    //    var rolesLite = await _context.UserRoleAssociations
    //        .AsNoTracking()
    //        .Where(ura => ura.UserId == user.Id)
    //        .Select(ura => new RoleLiteDto(
    //            ura.RoleId,
    //            (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty
    //        ))
    //        .ToListAsync();

    //    var roleIds = rolesLite.Select(r => r.RoleId).ToList();

    //    var rolePerms = await _context.RolePermissions
    //        .AsNoTracking()
    //        .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission != null && rp.Permission.IsActive) // فقط الصلاحيات النشطة
    //        .Select(rp => new { rp.PermissionId, rp.Permission!.Name })
    //        .ToListAsync();

    //    var directPerms = await _context.UserPermissions
    //        .AsNoTracking()
    //        .Where(up => up.UserId == user.Id && up.Permission != null && up.Permission.IsActive) // فقط الصلاحيات النشطة
    //        .Select(up => new { up.PermissionId, up.Permission!.Name })
    //        .ToListAsync();

    //    var permissionPairs2 = directPerms
    //        .Where(p => p.PermissionId.HasValue && !string.IsNullOrWhiteSpace(p.Name))
    //        .Select(p => new { Id = p.PermissionId!.Value, Name = p.Name! })
    //        .Concat(
    //            rolePerms
    //                .Where(p => p.PermissionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
    //                .Select(p => new { Id = p.PermissionId, Name = p.Name! })
    //        );

    //    var permissionMap2 = permissionPairs2
    //        .GroupBy(p => p.Id)
    //        .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<Guid>.Default);

    //    var permissionsLite2 = permissionMap2
    //        .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
    //        .Select(kvp => new PermissionLiteDto(kvp.Key.ToString(), kvp.Value))
    //        .ToList();

    //    var dtoOut = new UserListItemDto
    //    {
    //        userId = user.Id,
    //        fullName = user.FullName,
    //        username = user.UserName ?? string.Empty,
    //        email = user.Email ?? string.Empty,
    //        phone = user.PhoneNumber,
    //        isActive = user.IsActive,
    //        createdAt = user.CreationDate,
    //        roles = rolesLite.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList(),
    //        permissionsCount = permissionsLite2.Count,
    //        Permissions = permissionsLite2
    //    };

    //    return Result<UserListItemDto>.Success(dtoOut);
    //}

    public async Task<Result<UserListItemDto>> UpdateUserAsync(string id, UpdateUserPatchRequest dto)
    {
        // 0) الهدف
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Result<UserListItemDto>.Failure(new[] { "المستخدم غير موجود" }, null);

        // 1) الممثل (المنفّذ الحالي) ونطاقه
        var actorId = _currentUserService.UserId ?? string.Empty;

        // هل هو SuperAdmin (بالاعتماد على NormalizedName حتى يترجمها EF)؟
        var isSuperAdmin = await _context.UserRoleAssociations
            .Where(ura => ura.UserId == actorId)
            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .AnyAsync(n => n == "SUPERADMIN");

        // بيانات الممثل
        var actor = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorId) as User;

        if (actor is null)
            return Result<UserListItemDto>.Failure(new[] { "المستخدم الحالي غير موجود." }, null);

        long? actorBranchId = actor.BranchId;
        long? actorDepartmentId = actor.DepartmentId;

        // إذا ليس SuperAdmin ⇒ يجب أن يكون مدير فرع، والهدف ضمن نفس الفرع
        if (!isSuperAdmin)
        {
            // مدير الفرع فقط (BranchId موجود و DepartmentId فارغ)
            var actorIsBranchManager = actorBranchId.HasValue && !actorDepartmentId.HasValue;
            if (!actorIsBranchManager)
                return Result<UserListItemDto>.Failure(new[] { "غير مسموح بالتعديل إلا لمدير الفرع أو SuperAdmin." }, null);

            // تحديد فرع الهدف: إمّا BranchId مباشر أو عبر قسمه
            long? targetBranchId = user.BranchId;
            if (!targetBranchId.HasValue && user.DepartmentId.HasValue)
            {
                targetBranchId = await _context.Departments
                    .Where(d => d.DepartmentId == user.DepartmentId.Value)
                    .Select(d => (long?)d.BranchId)
                    .FirstOrDefaultAsync();
            }

            if (!targetBranchId.HasValue || targetBranchId.Value != actorBranchId.Value)
                return Result<UserListItemDto>.Failure(new[] { "لا يمكنك تعديل مستخدم خارج فرعك." }, null);
        }

        // 2) تحقّقات uniqueness عند تعديل username/email
        if (!string.IsNullOrWhiteSpace(dto.username) &&
            !string.Equals(dto.username, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var existsUserName = await _userManager.FindByNameAsync(dto.username);
            if (existsUserName is not null && existsUserName.Id != user.Id)
                return Result<UserListItemDto>.Failure(new[] { "اسم المستخدم موجود مسبقا." }, null);
        }

        if (!string.IsNullOrWhiteSpace(dto.email) &&
            !string.Equals(dto.email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existsEmail = await _userManager.FindByEmailAsync(dto.email);
            if (existsEmail is not null && existsEmail.Id != user.Id)
                return Result<UserListItemDto>.Failure(new[] { "الايميل موجود مسبقا." }, null);
        }

        // 3) تعديل الحقول (PATCH)
        if (dto.fullName is not null) user.FullName = dto.fullName;
        if (dto.username is not null) user.UserName = dto.username;
        if (dto.email is not null) user.Email = dto.email;
        if (dto.phone is not null) user.PhoneNumber = dto.phone;
        if (dto.isActive.HasValue) user.IsActive = dto.isActive.Value;

        // 4) تحديث المستخدم عبر Identity
        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            return Result<UserListItemDto>.Failure(updRes.Errors.Select(e => e.Description), null);

        // 5) الأدوار (null => لا تعديل، empty => إزالة كل الأدوار، otherwise => استبدال كامل)
        if (dto.roles != null)
        {
            // 🚫 منع مدير الفرع من إضافة/سحب SuperAdmin
            if (!isSuperAdmin)
            {
                var superAdminRoleId = await _context.Roles
                    .Where(r => r.NormalizedName == "SUPERADMIN")
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync();

                bool touchesSuperAdmin =
                    (dto.roles.Any(r => !string.IsNullOrWhiteSpace(r.RoleName) &&
                                        r.RoleName.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)))
                    ||
                    (dto.roles.Any(r => !string.IsNullOrWhiteSpace(r.RoleId) &&
                                        r.RoleId == superAdminRoleId));

                if (touchesSuperAdmin)
                    return Result<UserListItemDto>.Failure(new[] { "غير مسموح لمدير الفرع بمنح/سحب دور SuperAdmin." }, null);
            }

            // الأدوار الحالية
            var current = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == user.Id)
                .ToListAsync();

            // تحويل المدخلات إلى مجموعة RoleIds نهائية
            var requested = (dto.roles ?? new()).Where(r => r is not null).ToList();

            var incomingIds = requested
                .Where(r => !string.IsNullOrWhiteSpace(r.RoleId))
                .Select(r => r.RoleId!)
                .ToList();

            var incomingNames = requested
                .Where(r => string.IsNullOrWhiteSpace(r.RoleId) && !string.IsNullOrWhiteSpace(r.RoleName))
                .Select(r => r.RoleName!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rolesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (incomingNames.Any())
            {
                var foundByName = await _context.Roles
                    .Where(r => r.Name != null && incomingNames.Contains(r.Name))
                    .Select(r => new { r.Id, r.Name })
                    .ToListAsync();

                var validByName = foundByName.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
                rolesByName = validByName.ToDictionary(x => x.Name!, x => x.Id, StringComparer.OrdinalIgnoreCase);

                var missingByName = incomingNames.Where(n => !rolesByName.ContainsKey(n)).ToList();
                if (missingByName.Any())
                    return Result<UserListItemDto>.Failure(new[] { $"بعض الأدوار غير موجودة: {string.Join(", ", missingByName)}" }, null);
            }

            var finalIncomingIds = new HashSet<string>(incomingIds, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rolesByName) finalIncomingIds.Add(kv.Value);

            var currentIds = new HashSet<string>(current.Select(c => c.RoleId), StringComparer.OrdinalIgnoreCase);

            var toAdd = finalIncomingIds.Except(currentIds, StringComparer.OrdinalIgnoreCase).ToList();
            var toRemove = currentIds.Except(finalIncomingIds, StringComparer.OrdinalIgnoreCase).ToList();

            if (toRemove.Any())
            {
                var removeRows = current.Where(c => toRemove.Contains(c.RoleId, StringComparer.OrdinalIgnoreCase)).ToList();
                _context.UserRoleAssociations.RemoveRange(removeRows);
            }

            if (toAdd.Any())
            {
                var addRows = toAdd.Select(rid => new UserRoleAssociation
                {
                    UserId = user.Id,
                    RoleId = rid
                });
                await _context.UserRoleAssociations.AddRangeAsync(addRows);
            }

            await _context.SaveChangesAsync();
        }

        // 6) إعداد الخرج: roles + permissions (كما هو لديك)
        var rolesLite = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == user.Id)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty
            ))
            .ToListAsync();

        var roleIds = rolesLite.Select(r => r.RoleId).ToList();

        var rolePerms = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission != null && rp.Permission.IsActive)
            .Select(rp => new { rp.PermissionId, rp.Permission!.Name })
            .ToListAsync();

        var directPerms = await _context.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == user.Id && up.Permission != null && up.Permission.IsActive)
            .Select(up => new { up.PermissionId, up.Permission!.Name })
            .ToListAsync();

        var permissionPairs2 = directPerms
            .Where(p => p.PermissionId.HasValue && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new { Id = p.PermissionId!.Value, Name = p.Name! })
            .Concat(
                rolePerms
                    .Where(p => p.PermissionId != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new { Id = p.PermissionId, Name = p.Name! })
            );

        var permissionMap2 = permissionPairs2
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().Name, EqualityComparer<Guid>.Default);

        var permissionsLite2 = permissionMap2
            .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new PermissionLiteDto(kvp.Key.ToString(), kvp.Value))
            .ToList();

        var dtoOut = new UserListItemDto
        {
            userId = user.Id,
            fullName = user.FullName,
            username = user.UserName ?? string.Empty,
            email = user.Email ?? string.Empty,
            phone = user.PhoneNumber,
            isActive = user.IsActive,
            createdAt = user.CreationDate,
            roles = rolesLite.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList(),
            rolesCount = rolesLite.Count,
            permissionsCount = permissionsLite2.Count,
            Permissions = permissionsLite2
        };

        return Result<UserListItemDto>.Success(dtoOut);
    }

    #endregion

    #region          تعديل حالة المستخدم فقط
    public async Task<Result<UpdateUserStatusRequest>> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return Result<UpdateUserStatusRequest>.Failure(new[] { "المتسخدم غير موجود." }, null);

        user.IsActive = request.isActive;

        var updRes = await _userManager.UpdateAsync(user);
        if (!updRes.Succeeded)
            return Result<UpdateUserStatusRequest>.Failure(updRes.Errors.Select(e => e.Description), null);

        var dtoOut = new UpdateUserStatusRequest
        {
            isActive = user.IsActive,
        };
        return Result<UpdateUserStatusRequest>.Success(dtoOut);
    }
    #endregion

    #region         تغيير كلمة المرور (المستخدم نفسه)
    public async Task<Result<string>> ChangeOwnPasswordAsync(ChangePasswordRequest request)
    {
        var errorList = new List<string>();
        var userId = _currentUserService.UserId;

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            errorList.Add("المستخدم غير موجود");
            return Result<string>.Failure(errorList, null);
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            errorList.AddRange(result.Errors.Select(e => e.Description));
            return Result<string>.Failure(errorList, null);
        }

        return Result<string>.Success("تم تحديث كلمة المرور بنجاح.");

    }
    #endregion

    #region         إعادة تعيين كلمة المرور (الإداري)
    public async Task<Result<string>> ResetUserPasswordAsync(AdminChangePasswordRequest request)
    {
        var errorList = new List<string>();

        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null)
        {
            errorList.Add("المستخدم غير موجود");
            return Result<string>.Failure(errorList, null);
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);

        if (!resetResult.Succeeded)
        {
            errorList.AddRange(resetResult.Errors.Select(e => e.Description));
            return Result<string>.Failure(errorList, null);
        }

        return Result<string>.Success("تم إعادة تعيين كلمة المرور بنجاح.");
    }
    #endregion

    #region         اسناد ادوار الى مستخدم 
    //public async Task<Result<string>> ToggleRolesForUserAsync(AssignRolesDto dto)
    //{
    //    var errors = new List<string>();

    //    // 0) المستخدم الهدف
    //    var user = await _userManager.FindByIdAsync(dto.UserId);
    //    if (user == null)
    //        return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

    //    // 1) الممثل (المنفّذ الحالي) وصلاحيته
    //    var actorId = _currentUserService.UserId ?? string.Empty;

    //    var isSuperAdmin = await _context.UserRoleAssociations
    //        .Where(ura => ura.UserId == actorId)
    //        .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
    //        .AnyAsync(n => n == "SUPERADMIN");

    //    var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
    //    if (actor is null)
    //        return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." }, null);

    //    long? actorBranchId = actor.BranchId;
    //    long? actorDeptId = actor.DepartmentId;

    //    if (!isSuperAdmin)
    //    {
    //        // مدير الفرع فقط (BranchId موجود و DepartmentId فارغ)
    //        var actorIsBranchManager = actorBranchId.HasValue && !actorDeptId.HasValue;
    //        if (!actorIsBranchManager)
    //            return Result<string>.Failure(new[] { "غير مسموح بتعديل الأدوار إلا لمدير الفرع أو SuperAdmin." }, null);

    //        // نطاق فرع الهدف
    //        long? targetBranchId = user.BranchId;
    //        if (!targetBranchId.HasValue && user.DepartmentId.HasValue)
    //        {
    //            targetBranchId = await _context.Departments
    //                .Where(d => d.DepartmentId == user.DepartmentId.Value)
    //                .Select(d => (long?)d.BranchId)
    //                .FirstOrDefaultAsync();
    //        }

    //        if (!targetBranchId.HasValue || targetBranchId.Value != actorBranchId.Value)
    //            return Result<string>.Failure(new[] { "لا يمكنك تعديل أدوار مستخدم خارج فرعك." }, null);
    //    }

    //    // 2) حلّ قائمة الأدوار المطلوبة (ID/Name) إلى قائمة RoleIds نهائية
    //    var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    //    if (dto.RoleIds != null && dto.RoleIds.Count > 0)
    //        foreach (var rid in dto.RoleIds.Where(x => !string.IsNullOrWhiteSpace(x)))
    //            incomingIds.Add(rid);

    //    if (dto.RoleNames != null && dto.RoleNames.Count > 0)
    //    {
    //        var names = dto.RoleNames.Where(n => !string.IsNullOrWhiteSpace(n))
    //                                 .Select(n => n.Trim())
    //                                 .Distinct(StringComparer.OrdinalIgnoreCase)
    //                                 .ToList();

    //        if (names.Count > 0)
    //        {
    //            var found = await _context.Roles
    //                .Where(r => r.Name != null && names.Contains(r.Name))
    //                .Select(r => new { r.Id, r.Name, r.IsActive })
    //                .ToListAsync();

    //            var missing = names
    //                .Where(n => !found.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
    //                .ToList();

    //            if (missing.Any())
    //                return Result<string>.Failure(new[] { $"أدوار غير موجودة: {string.Join(", ", missing)}" }, null);

    //            foreach (var f in found)
    //                incomingIds.Add(f.Id);
    //        }
    //    }

    //    if (incomingIds.Count == 0)
    //        return Result<string>.Failure(new[] { "لم يتم تمرير أي دور." }, null);

    //    // 3) التحقق من أن جميع الأدوار المطلوبة "نشطة"
    //    var activeRoleIds = await _context.Roles
    //        .Where(r => incomingIds.Contains(r.Id) && r.IsActive)
    //        .Select(r => r.Id)
    //        .ToListAsync();

    //    var inactiveOrMissing = incomingIds.Except(activeRoleIds, StringComparer.OrdinalIgnoreCase).ToList();
    //    if (inactiveOrMissing.Any())
    //        return Result<string>.Failure(new[] { $"أدوار غير موجودة أو غير نشطة: {string.Join(", ", inactiveOrMissing)}" }, null);

    //    // 4) منع مدير الفرع من لمس SuperAdmin
    //    if (!isSuperAdmin)
    //    {
    //        var superAdminRoleId = await _context.Roles
    //            .Where(r => r.NormalizedName == "SUPERADMIN")
    //            .Select(r => r.Id)
    //            .FirstOrDefaultAsync();

    //        if (!string.IsNullOrWhiteSpace(superAdminRoleId))
    //        {
    //            if (incomingIds.Contains(superAdminRoleId))
    //                return Result<string>.Failure(new[] { "غير مسموح لمدير الفرع بمنح/سحب دور SuperAdmin." }, null);
    //        }
    //    }

    //    // 5) الأدوار الحالية للمستخدم
    //    var currentRoleIds = await _context.UserRoleAssociations
    //        .Where(ura => ura.UserId == user.Id)
    //        .Select(ura => ura.RoleId)
    //        .ToListAsync();

    //    var currentSet = new HashSet<string>(currentRoleIds, StringComparer.OrdinalIgnoreCase);

    //    // 6) احسب Toggling: الموجود يُحذف، غير الموجود يُضاف
    //    var toAdd = new List<string>();
    //    var toRemove = new List<string>();

    //    foreach (var rid in activeRoleIds)
    //    {
    //        if (currentSet.Contains(rid))
    //            toRemove.Add(rid);
    //        else
    //            toAdd.Add(rid);
    //    }

    //    if (toAdd.Count == 0 && toRemove.Count == 0)
    //        return Result<string>.Success("لا توجد تغييرات (كل الأدوار المطلوبة كانت في نفس حالتها).");

    //    // 7) Transaction: أدوار + صلاحيات + صلاحيات الحقول
    //    using var tx = await _context.Database.BeginTransactionAsync();
    //    try
    //    {
    //        // 7.1) إزالة الأدوار المطلوب إزالتها
    //        if (toRemove.Any())
    //        {
    //            var removeRows = await _context.UserRoleAssociations
    //                .Where(ura => ura.UserId == user.Id && toRemove.Contains(ura.RoleId))
    //                .ToListAsync();

    //            _context.UserRoleAssociations.RemoveRange(removeRows);
    //            await _context.SaveChangesAsync();
    //        }

    //        // 7.2) إضافة الأدوار المطلوب إضافتها
    //        if (toAdd.Any())
    //        {
    //            var addRows = toAdd.Select(rid => new UserRoleAssociation
    //            {
    //                Id = Guid.NewGuid(),
    //                UserId = user.Id,
    //                RoleId = rid
    //            });
    //            await _context.UserRoleAssociations.AddRangeAsync(addRows);
    //            await _context.SaveChangesAsync();
    //        }

    //        // 7.3) تحديث "صلاحيات المستخدم" وفق الحالة الجديدة

    //        // بعد التبديل، جمّع كل أدوار المستخدم الحالية
    //        var finalRoleIds = await _context.UserRoleAssociations
    //            .Where(ura => ura.UserId == user.Id)
    //            .Select(ura => ura.RoleId)
    //            .ToListAsync();

    //        // (أ) الصلاحيات الناتجة عن الأدوار النهائية — فقط النشطة
    //        var finalRolePermissionIds = await _context.RolePermissions
    //            .Where(rp => finalRoleIds.Contains(rp.RoleId))
    //            .Include(rp => rp.Permission)
    //            .Where(rp => rp.Permission != null && rp.Permission.IsActive)
    //            .Select(rp => rp.PermissionId)
    //            .Distinct()
    //            .ToListAsync();

    //        // (ب) الصلاحيات الموجودة مباشرة للمستخدم الآن
    //        var existingUserPermissionIds = await _context.UserPermissions
    //            .Where(up => up.UserId == user.Id && up.PermissionId != null)
    //            .Select(up => up.PermissionId!.Value)
    //            .ToListAsync();

    //        // (ج) الصلاحيات المطلوبة إضافتها (من الأدوار الجديدة) غير الموجودة مباشرة للمستخدم
    //        var toAddPerms = finalRolePermissionIds
    //            .Except(existingUserPermissionIds)
    //            .Select(pid => new UserPermission { UserId = user.Id, PermissionId = pid })
    //            .ToList();

    //        if (toAddPerms.Any())
    //            await _context.UserPermissions.AddRangeAsync(toAddPerms);

    //        // (د) الصلاحيات التي يجب إزالتها: أي صلاحية موجودة مباشرة للمستخدم لكنها لم تعد مطلوبة من أي دور
    //        //     ملاحظة: لو عندك "صلاحيات مباشرة" تُسند يدويًا خارج الأدوار وتريد إبقائها، لازم تميّزها بعَلم/فلَغ.
    //        var stillNeededPerms = new HashSet<Guid>(finalRolePermissionIds);
    //        var userPermsToRemove = await _context.UserPermissions
    //            .Where(up => up.UserId == user.Id && up.PermissionId != null && !stillNeededPerms.Contains(up.PermissionId!.Value))
    //            .ToListAsync();

    //        if (userPermsToRemove.Any())
    //            _context.UserPermissions.RemoveRange(userPermsToRemove);

    //        await _context.SaveChangesAsync();

    //        // 7.4) تحديث "صلاحيات الحقول" بنفس أسلوب الأذونات
    //        var finalFieldPermIds = await _context.RoleFieldPermissions
    //            .Where(rfp => finalRoleIds.Contains(rfp.RoleId))
    //            .Select(rfp => rfp.FieldPermissionId)
    //            .Distinct()
    //            .ToListAsync();

    //        var existingUserFieldPermIds = await _context.UserFieldPermissions
    //            .Where(ufp => ufp.UserId == user.Id)
    //            .Select(ufp => ufp.FieldPermissionId)
    //            .ToListAsync();

    //        var toAddFieldPerms = finalFieldPermIds
    //            .Except(existingUserFieldPermIds)
    //            .Select(fid => new UserFieldPermission { UserId = user.Id, FieldPermissionId = fid })
    //            .ToList();

    //        if (toAddFieldPerms.Any())
    //            await _context.UserFieldPermissions.AddRangeAsync(toAddFieldPerms);

    //        var stillNeededFieldPerms = new HashSet<Guid>(finalFieldPermIds);
    //        var userFieldPermsToRemove = await _context.UserFieldPermissions
    //            .Where(ufp => ufp.UserId == user.Id && !stillNeededFieldPerms.Contains(ufp.FieldPermissionId))
    //            .ToListAsync();

    //        if (userFieldPermsToRemove.Any())
    //            _context.UserFieldPermissions.RemoveRange(userFieldPermsToRemove);

    //        await _context.SaveChangesAsync();

    //        // 7.5) تحديث SecurityStamp لإجبار إعادة تسجيل الدخول وتطبيق التغييرات
    //        await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id);

    //        await tx.CommitAsync();

    //        var msg = $"تم تبديل الأدوار بنجاح. (إضافة: {toAdd.Count} | إزالة: {toRemove.Count})";
    //        return Result<string>.Success(msg);
    //    }
    //    catch
    //    {
    //        await tx.RollbackAsync();
    //        throw;
    //    }
    //}

    public async Task<Result<string>> ToggleRolesForUserAsync(AssignRolesDto dto)
    {
        // 0) الهدف
        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود" }, null);

        // 1) المنفّذ وصلاحيته
        var actorId = _currentUserService.UserId ?? string.Empty;

        var isSuperAdmin = await _context.UserRoleAssociations
            .Where(ura => ura.UserId == actorId)
            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .AnyAsync(n => n == "SUPERADMIN");

        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
        if (actor is null)
            return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." }, null);

        long? actorBranchId = actor.BranchId;
        long? actorDeptId = actor.DepartmentId;

        if (!isSuperAdmin)
        {
            var actorIsBranchManager = actorBranchId.HasValue && !actorDeptId.HasValue;
            if (!actorIsBranchManager)
                return Result<string>.Failure(new[] { "غير مسموح بتعديل الأدوار إلا لمدير الفرع أو SuperAdmin." }, null);

            long? targetBranchId = user.BranchId;
            if (!targetBranchId.HasValue && user.DepartmentId.HasValue)
            {
                targetBranchId = await _context.Departments
                    .Where(d => d.DepartmentId == user.DepartmentId.Value)
                    .Select(d => (long?)d.BranchId)
                    .FirstOrDefaultAsync();
            }
            if (!targetBranchId.HasValue || targetBranchId.Value != actorBranchId.Value)
                return Result<string>.Failure(new[] { "لا يمكنك تعديل أدوار مستخدم خارج فرعك." }, null);
        }

        // 2) حلّ القائمة المرسلة إلى RoleIds نهائية
        var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (dto.RoleIds is { Count: > 0 })
            foreach (var rid in dto.RoleIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                incomingIds.Add(rid);

        if (dto.RoleNames is { Count: > 0 })
        {
            var names = dto.RoleNames!
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (names.Count > 0)
            {
                var found = await _context.Roles
                    .Where(r => r.Name != null && names.Contains(r.Name))
                    .Select(r => new { r.Id, r.Name, r.IsActive })
                    .ToListAsync();

                var missing = names.Where(n => !found.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
                if (missing.Any())
                    return Result<string>.Failure(new[] { $"أدوار غير موجودة: {string.Join(", ", missing)}" }, null);

                foreach (var f in found) incomingIds.Add(f.Id);
            }
        }

        // إذا ما انبعت أي دور، نعتبرها قائمة نهائية فاضية (يعني إزالة كل الأدوار المسموح إزالتها)
        // بس رح نتقيّد بقيود SuperAdmin أدناه.

        // 3) نشِّط فقط الأدوار الفعّالة
        var activeIncomingIds = await _context.Roles
            .Where(r => incomingIds.Contains(r.Id) && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync();

        // 4) حماية SuperAdmin لغير السوبر (لا إضافة ولا حذف)
        string? superAdminRoleId = await _context.Roles
            .Where(r => r.NormalizedName == "SUPERADMIN")
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
        {
            // امنع تضمين SuperAdmin ضمن الحالة النهائية لمدير فرع
            activeIncomingIds = activeIncomingIds.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 5) الأدوار الحالية
        var currentRoleIds = await _context.UserRoleAssociations
            .Where(ura => ura.UserId == user.Id)
            .Select(ura => ura.RoleId)
            .ToListAsync();

        var currentSet = new HashSet<string>(currentRoleIds, StringComparer.OrdinalIgnoreCase);
        var finalSet = new HashSet<string>(activeIncomingIds, StringComparer.OrdinalIgnoreCase); // الحالة النهائية القادمة من الواجهة

        // 6) احسب الفرق: ما يجب إضافته/إزالته
        var toAdd = finalSet.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentSet.Except(finalSet, StringComparer.OrdinalIgnoreCase).ToList();

        // حماية إضافية: لو المستخدم عنده SuperAdmin حاليًا، لا تسمح لغير السوبر بحذفه
        if (!isSuperAdmin && !string.IsNullOrWhiteSpace(superAdminRoleId))
            toRemove = toRemove.Where(id => !string.Equals(id, superAdminRoleId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return Result<string>.Success("لا توجد تغييرات.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // 7) إزالة المطلوب فقط
            if (toRemove.Any())
            {
                var removeRows = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == user.Id && toRemove.Contains(ura.RoleId))
                    .ToListAsync();

                _context.UserRoleAssociations.RemoveRange(removeRows);
                await _context.SaveChangesAsync();
            }

            // 8) إضافة المطلوب فقط
            if (toAdd.Any())
            {
                var addRows = toAdd.Select(rid => new UserRoleAssociation
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    RoleId = rid
                });
                await _context.UserRoleAssociations.AddRangeAsync(addRows);
                await _context.SaveChangesAsync();
            }

            // 9) تحديث الصلاحيات المشتقة من الحالة النهائية
            var finalRoleIds = await _context.UserRoleAssociations
                .Where(ura => ura.UserId == user.Id)
                .Select(ura => ura.RoleId)
                .ToListAsync();

            var finalRolePermissionIds = await _context.RolePermissions
                .Where(rp => finalRoleIds.Contains(rp.RoleId))
                .Include(rp => rp.Permission)
                .Where(rp => rp.Permission != null && rp.Permission.IsActive)
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync();

            var existingUserPermissionIds = await _context.UserPermissions
                .Where(up => up.UserId == user.Id && up.PermissionId != null)
                .Select(up => up.PermissionId!.Value)
                .ToListAsync();

            // أ) أضف ما ينقص
            var toAddPerms = finalRolePermissionIds
                .Except(existingUserPermissionIds)
                .Select(pid => new UserPermission { UserId = user.Id, PermissionId = pid })
                .ToList();
            if (toAddPerms.Any())
                await _context.UserPermissions.AddRangeAsync(toAddPerms);

            // ب) أزل أي إذن مباشر لم يعد مطلوبًا بعد إزالة الأدوار
            var stillNeededPerms = new HashSet<Guid>(finalRolePermissionIds);
            var userPermsToRemove = await _context.UserPermissions
                .Where(up => up.UserId == user.Id && up.PermissionId != null && !stillNeededPerms.Contains(up.PermissionId!.Value))
                .ToListAsync();
            if (userPermsToRemove.Any())
                _context.UserPermissions.RemoveRange(userPermsToRemove);

            await _context.SaveChangesAsync();

            // 10) صلاحيات الحقول (نفس المنطق)
            var finalFieldPermIds = await _context.RoleFieldPermissions
                .Where(rfp => finalRoleIds.Contains(rfp.RoleId))
                .Select(rfp => rfp.FieldPermissionId)
                .Distinct()
                .ToListAsync();

            var existingUserFieldPermIds = await _context.UserFieldPermissions
                .Where(ufp => ufp.UserId == user.Id)
                .Select(ufp => ufp.FieldPermissionId)
                .ToListAsync();

            var toAddFieldPerms = finalFieldPermIds
                .Except(existingUserFieldPermIds)
                .Select(fid => new UserFieldPermission { UserId = user.Id, FieldPermissionId = fid })
                .ToList();
            if (toAddFieldPerms.Any())
                await _context.UserFieldPermissions.AddRangeAsync(toAddFieldPerms);

            var stillNeededFieldPerms = new HashSet<Guid>(finalFieldPermIds);
            var userFieldPermsToRemove = await _context.UserFieldPermissions
                .Where(ufp => ufp.UserId == user.Id && !stillNeededFieldPerms.Contains(ufp.FieldPermissionId))
                .ToListAsync();
            if (userFieldPermsToRemove.Any())
                _context.UserFieldPermissions.RemoveRange(userFieldPermsToRemove);

            await _context.SaveChangesAsync();

            // 11) تطبيق التغييرات الأمنية
            await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id);

            await tx.CommitAsync();

            return Result<string>.Success($"تم تعديل الأدوار بنجاح. (إضافة: {toAdd.Count} | إزالة: {toRemove.Count})");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    #endregion

    #region         حذف دور من مستخدم
    //public async Task<Result<string>> RemoveRoleFromUserAsync(RemoveRoleDto dto)
    //{
    //    var errors = new List<string>();

    //    var userRole = await _context.UserRoleAssociations
    //        .Include(ura => ura.Role)
    //        .FirstOrDefaultAsync(ura => ura.UserId == dto.UserId && ura.RoleId == dto.RoleId);

    //    if (userRole == null)
    //    {
    //        errors.Add("لم يتم العثور على هذا الدور للمستخدم.");
    //        return Result<string>.Failure(errors, null);
    //    }

    //    using var transaction = await _context.Database.BeginTransactionAsync();
    //    try
    //    {
    //        _context.UserRoleAssociations.Remove(userRole);
    //        await _context.SaveChangesAsync();

    //        // صلاحيات الدور
    //        var permissionIds = await _context.RolePermissions
    //            .Where(rp => rp.RoleId == dto.RoleId)
    //            .Select(rp => rp.PermissionId)
    //            .ToListAsync();

    //        if (permissionIds.Any())
    //        {
    //            // تحقق إن كان المستخدم ما زال يملك الدور من مسار آخر
    //            var remainingRoleIds = await _context.UserRoleAssociations
    //                .Where(ura => ura.UserId == dto.UserId)
    //                .Select(ura => ura.RoleId)
    //                .ToListAsync();

    //            var rolePermissionsStillNeeded = await _context.RolePermissions
    //                .Where(rp => remainingRoleIds.Contains(rp.RoleId))
    //                .Select(rp => rp.PermissionId)
    //                .Distinct()
    //                .ToListAsync();

    //            var permsToRemove = permissionIds
    //                .Except(rolePermissionsStillNeeded)
    //                .ToList();

    //            if (permsToRemove.Any())
    //            {
    //                var userPerms = await _context.UserPermissions
    //                    .Where(up => up.UserId == dto.UserId && up.PermissionId != null && permsToRemove.Contains(up.PermissionId!.Value))
    //                    .ToListAsync();

    //                _context.UserPermissions.RemoveRange(userPerms);
    //            }
    //        }

    //        // صلاحيات الحقول
    //        var fieldPermissionIds = await _context.RoleFieldPermissions
    //            .Where(rfp => rfp.RoleId == dto.RoleId)
    //            .Select(rfp => rfp.FieldPermissionId)
    //            .ToListAsync();

    //        if (fieldPermissionIds.Any())
    //        {
    //            var remainingRoleIds = await _context.UserRoleAssociations
    //                .Where(ura => ura.UserId == dto.UserId)
    //                .Select(ura => ura.RoleId)
    //                .Distinct()
    //                .ToListAsync();

    //            var fieldPermsStillNeeded = await _context.RoleFieldPermissions
    //                .Where(rfp => remainingRoleIds.Contains(rfp.RoleId))
    //                .Select(rfp => rfp.FieldPermissionId)
    //                .Distinct()
    //                .ToListAsync();

    //            var fieldPermsToRemove = fieldPermissionIds
    //                .Except(fieldPermsStillNeeded)
    //                .ToList();

    //            if (fieldPermsToRemove.Any())
    //            {
    //                var userFieldPerms = await _context.UserFieldPermissions
    //                    .Where(ufp => ufp.UserId == dto.UserId && fieldPermsToRemove.Contains(ufp.FieldPermissionId))
    //                    .ToListAsync();

    //                _context.UserFieldPermissions.RemoveRange(userFieldPerms);
    //            }
    //        }

    //        await _context.SaveChangesAsync();

    //        // تحديث SecurityStamp لإجبار تسجيل الخروج وتطبيق التغييرات
    //        await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, dto.UserId);

    //        await transaction.CommitAsync();

    //        return Result<string>.Success("تم إزالة الدور والصلاحيات التابعة له بنجاح.");
    //    }
    //    catch (Exception)
    //    {
    //        await transaction.RollbackAsync();
    //        throw;
    //    }
    //}
    ////#endregion

    //#region         إزالة صلاحيات مباشرة من مستخدم
    //public async Task<Result<string>> RemovePermissionsFromUserAsync(RemovePermissionsDto dto)
    //{
    //    var errors = new List<string>();

    //    if (dto.PermissionIds == null || dto.PermissionIds.Count == 0)
    //    {
    //        errors.Add("لم يتم تحديد أي صلاحيات.");
    //        return Result<string>.Failure(errors, null);
    //    }

    //    var user = await _userManager.FindByIdAsync(dto.UserId);
    //    if (user == null)
    //    {
    //        errors.Add("المستخدم غير موجود.");
    //        return Result<string>.Failure(errors, null);
    //    }

    //    var userPermissions = await _context.UserPermissions
    //        .Where(up => up.UserId == dto.UserId &&
    //                     up.PermissionId != null &&
    //                     dto.PermissionIds.Contains(up.PermissionId!.Value))
    //        .ToListAsync();

    //    if (!userPermissions.Any())
    //        return Result<string>.Success("لا توجد صلاحيات مطابقة لإزالتها.");

    //    _context.UserPermissions.RemoveRange(userPermissions);
    //    await _context.SaveChangesAsync();

    //    // تحديث SecurityStamp لإجبار تسجيل الخروج وتطبيق التغييرات
    //    await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, dto.UserId);

    //    return Result<string>.Success("تم إزالة الصلاحيات المحددة من المستخدم.");
    //}
    #endregion

    #region  اسناد صلاحيات الى مستخدم
    //public async Task<Result<string>> TogglePermissionsForUserAsync(AssignPermissionToUserDto dto)
    //{
    //    var errors = new List<string>();

    //    // 0) تحقق إدخال
    //    if (dto.PermissionId == null || dto.PermissionId.Count == 0)
    //        return Result<string>.Failure(new[] { "لم يتم تحديد أي صلاحيات." }, null);

    //    // 1) المستخدم الهدف
    //    var user = await _userManager.FindByIdAsync(dto.UserId);
    //    if (user == null)
    //        return Result<string>.Failure(new[] { "المستخدم غير موجود." }, null);

    //    // 2) الممثل (المنفّذ الحالي) وتحقق الصلاحية/نطاق الفرع
    //    var actorId = _currentUserService.UserId ?? string.Empty;

    //    var isSuperAdmin = await _context.UserRoleAssociations
    //        .Where(ura => ura.UserId == actorId)
    //        .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
    //        .AnyAsync(n => n == "SUPERADMIN");

    //    var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
    //    if (actor is null)
    //        return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." }, null);

    //    if (!isSuperAdmin)
    //    {
    //        // مدير فرع فقط: BranchId موجود و DepartmentId فارغ
    //        var actorIsBranchManager = actor.BranchId.HasValue && !actor.DepartmentId.HasValue;
    //        if (!actorIsBranchManager)
    //            return Result<string>.Failure(new[] { "غير مسموح بتعديل الصلاحيات إلا لمدير الفرع أو SuperAdmin." }, null);

    //        // نطاق فرع المستخدم الهدف (BranchId مباشر أو عبر قسمه)
    //        long? targetBranchId = user.BranchId;
    //        if (!targetBranchId.HasValue && user.DepartmentId.HasValue)
    //        {
    //            targetBranchId = await _context.Departments
    //                .Where(d => d.DepartmentId == user.DepartmentId.Value)
    //                .Select(d => (long?)d.BranchId)
    //                .FirstOrDefaultAsync();
    //        }

    //        if (!targetBranchId.HasValue || targetBranchId.Value != actor.BranchId!.Value)
    //            return Result<string>.Failure(new[] { "لا يمكنك تعديل صلاحيات مستخدم خارج فرعك." }, null);
    //    }

    //    // 3) التحقق من وجود/نشاط الصلاحيات المطلوبة
    //    var activePermIds = await _context.Permissions
    //        .Where(p => dto.PermissionId.Contains(p.Id) && p.IsActive)
    //        .Select(p => p.Id)
    //        .ToListAsync();

    //    var missingOrInactive = dto.PermissionId.Except(activePermIds).ToList();
    //    if (missingOrInactive.Any())
    //        return Result<string>.Failure(new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

    //    // 4) الصلاحيات المباشرة الحالية للمستخدم (فقط المباشرة)
    //    var currentDirectIds = await _context.UserPermissions
    //        .Where(up => up.UserId == user.Id && up.PermissionId != null)
    //        .Select(up => up.PermissionId!.Value)
    //        .ToListAsync();

    //    // 5) احسب الـ Toggle
    //    var toRemove = activePermIds.Intersect(currentDirectIds).ToList();                 // موجودة ⇒ احذفها
    //    var toAdd = activePermIds.Except(currentDirectIds).ToList();                    // غير موجودة ⇒ أضفها

    //    if (toAdd.Count == 0 && toRemove.Count == 0)
    //        return Result<string>.Success("لا توجد تغييرات (كل الصلاحيات المطلوبة كانت في نفس حالتها).");

    //    using var tx = await _context.Database.BeginTransactionAsync();
    //    try
    //    {
    //        // 6) إزالة الصلاحيات المباشرة المحددة (لا نمس الصلاحيات الموروثة من الأدوار)
    //        if (toRemove.Any())
    //        {
    //            var rows = await _context.UserPermissions
    //                .Where(up => up.UserId == user.Id &&
    //                             up.PermissionId != null &&
    //                             toRemove.Contains(up.PermissionId!.Value))
    //                .ToListAsync();

    //            _context.UserPermissions.RemoveRange(rows);
    //            await _context.SaveChangesAsync();
    //        }

    //        // 7) إضافة الصلاحيات المباشرة الجديدة
    //        if (toAdd.Any())
    //        {
    //            var addRows = toAdd.Select(pid => new UserPermission
    //            {
    //                UserId = user.Id,
    //                PermissionId = pid
    //            });
    //            await _context.UserPermissions.AddRangeAsync(addRows);
    //            await _context.SaveChangesAsync();
    //        }

    //        // 8) تحديث SecurityStamp ليفرض إعادة تسجيل الدخول وتطبيق التغييرات فورًا
    //        await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id);

    //        await tx.CommitAsync();

    //        return Result<string>.Success($"تم تبديل الصلاحيات بنجاح. (إضافة: {toAdd.Count} | إزالة: {toRemove.Count})");
    //    }
    //    catch
    //    {
    //        await tx.RollbackAsync();
    //        throw;
    //    }
    //}
    public async Task<Result<string>> TogglePermissionsForUserAsync(AssignPermissionToUserDto dto)
    {
        // 0) تحقق إدخال
        if (dto.PermissionId == null || dto.PermissionId.Count == 0)
            return Result<string>.Failure(new[] { "لم يتم تحديد أي صلاحيات." }, null);

        // 1) المستخدم الهدف
        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
            return Result<string>.Failure(new[] { "المستخدم غير موجود." }, null);

        // 2) الممثل (المنفّذ الحالي) وتحقق الصلاحية/نطاق الفرع
        var actorId = _currentUserService.UserId ?? string.Empty;

        var isSuperAdmin = await _context.UserRoleAssociations
            .Where(ura => ura.UserId == actorId)
            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .AnyAsync(n => n == "SUPERADMIN");

        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
        if (actor is null)
            return Result<string>.Failure(new[] { "المستخدم الحالي غير موجود." }, null);

        if (!isSuperAdmin)
        {
            // مدير فرع فقط: BranchId موجود و DepartmentId فارغ
            var actorIsBranchManager = actor.BranchId.HasValue && !actor.DepartmentId.HasValue;
            if (!actorIsBranchManager)
                return Result<string>.Failure(new[] { "غير مسموح بتعديل الصلاحيات إلا لمدير الفرع أو SuperAdmin." }, null);

            // نطاق فرع المستخدم الهدف (BranchId مباشر أو عبر قسمه)
            long? targetBranchId = user.BranchId;
            if (!targetBranchId.HasValue && user.DepartmentId.HasValue)
            {
                targetBranchId = await _context.Departments
                    .Where(d => d.DepartmentId == user.DepartmentId.Value)
                    .Select(d => (long?)d.BranchId)
                    .FirstOrDefaultAsync();
            }

            if (!targetBranchId.HasValue || targetBranchId.Value != actor.BranchId!.Value)
                return Result<string>.Failure(new[] { "لا يمكنك تعديل صلاحيات مستخدم خارج فرعك." }, null);
        }

        // 3) فعالية الصلاحيات المرسلة
        var incoming = new HashSet<Guid>(dto.PermissionId);
        var activePermIds = await _context.Permissions
            .Where(p => incoming.Contains(p.Id) && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();

        var missingOrInactive = incoming.Except(activePermIds).ToList();
        if (missingOrInactive.Any())
            return Result<string>.Failure(new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

        // 4) الصلاحيات المباشرة الحالية للمستخدم (فقط المباشرة)
        var currentDirectIds = await _context.UserPermissions
            .Where(up => up.UserId == user.Id && up.PermissionId != null)
            .Select(up => up.PermissionId!.Value)
            .ToListAsync();

        var currentSet = new HashSet<Guid>(currentDirectIds);
        var finalSet = new HashSet<Guid>(activePermIds); // الحالة النهائية المرغوبة

        // 5) احسب الفرق وفق "حالة نهائية"
        var toAdd = finalSet.Except(currentSet).ToList(); //新增 فقط
        var toRemove = currentSet.Except(finalSet).ToList(); //احذف فقط اللي مو موجود بالقائمة الجديدة

        if (toAdd.Count == 0 && toRemove.Count == 0)
            return Result<string>.Success("لا توجد تغييرات.");

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // إزالة الصلاحيات المباشرة غير المطلوبة
            if (toRemove.Any())
            {
                var rows = await _context.UserPermissions
                    .Where(up => up.UserId == user.Id &&
                                 up.PermissionId != null &&
                                 toRemove.Contains(up.PermissionId!.Value))
                    .ToListAsync();

                _context.UserPermissions.RemoveRange(rows);
                await _context.SaveChangesAsync();
            }

            // إضافة الصلاحيات المباشرة الناقصة
            if (toAdd.Any())
            {
                var addRows = toAdd.Select(pid => new UserPermission
                {
                    UserId = user.Id,
                    PermissionId = pid
                });
                await _context.UserPermissions.AddRangeAsync(addRows);
                await _context.SaveChangesAsync();
            }

            // تحديث SecurityStamp لتطبيق التغييرات فورًا
            await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, user.Id);

            await tx.CommitAsync();

            return Result<string>.Success($"تم تعديل الصلاحيات بنجاح. (إضافة: {toAdd.Count} | إزالة: {toRemove.Count})");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
    #endregion

    #region         إزالة صلاحيات الحقول من مستخدم
    public async Task<Result<string>> RemoveFieldPermissionsFromUserAsync(RemoveFieldPermissionsDto dto)
    {
        var errors = new List<string>();

        if (dto.FieldPermissionIds == null || dto.FieldPermissionIds.Count == 0)
        {
            errors.Add("لم يتم تحديد أي صلاحيات حقول.");
            return Result<string>.Failure(errors, null);
        }

        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user == null)
        {
            errors.Add("المستخدم غير موجود.");
            return Result<string>.Failure(errors, null);
        }

        var userFieldPermissions = await _context.UserFieldPermissions
            .Where(ufp => ufp.UserId == dto.UserId &&
                          dto.FieldPermissionIds.Contains(ufp.FieldPermissionId))
            .ToListAsync();

        if (!userFieldPermissions.Any())
            return Result<string>.Success("لا توجد صلاحيات حقول مطابقة لإزالتها.");

        _context.UserFieldPermissions.RemoveRange(userFieldPermissions);
        await _context.SaveChangesAsync();

        // تحديث SecurityStamp لإجبار تسجيل الخروج وتطبيق التغييرات
        await SecurityStampHelper.InvalidateUserTokensAsync(_context, _userManager, dto.UserId);

        return Result<string>.Success("تم إزالة صلاحيات الحقول المحددة من المستخدم.");
    }
    #endregion

    #region تعديل مستخدم وأدواره
    public async Task<Result<string>> UpdateUserWithRolesAsync(string userId, UpdateUserWithRolesDto dto)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // تحقق من صحة الـ UserId
        if (string.IsNullOrWhiteSpace(userId))
        {
            errors.Add("معرف المستخدم غير صالح.");
            return Result<string>.Failure(errors, null);
        }

        // جلب المستخدم
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            errors.Add("المستخدم غير موجود.");
            return Result<string>.Failure(errors, null);
        }

        // ====================== بدء الترانزاكشن ======================
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // ====================== تحديث بيانات المستخدم ======================
            user.UserName = dto.UserName;
            user.NormalizedUserName = dto.UserName.ToUpperInvariant();
            user.Email = dto.Email;
            user.NormalizedEmail = dto.Email.ToUpperInvariant();

            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
            {
                errors.AddRange(updateRes.Errors.Select(e => e.Description));
                return Result<string>.Failure(errors, null);
            }

            // ====================== معالجة الأدوار ======================
            if (dto.RoleIds?.Any() == true)
            {
                // الأدوار الموجودة في النظام - فقط الأدوار النشطة
                var roles = await _context.Roles
                    .Where(r => dto.RoleIds.Contains(r.Id) && r.IsActive) // فقط الأدوار النشطة
                    .ToListAsync();

                // التحقق من الأدوار غير الموجودة أو غير النشطة
                var foundRoleIds = roles.Select(r => r.Id).ToList();
                var notFound = dto.RoleIds.Except(foundRoleIds).ToList();

                if (notFound.Any())
                {
                    errors.Add($"الأدوار التالية غير موجودة: {string.Join(", ", notFound)}");
                    return Result<string>.Failure(errors, null);
                }

                // الأدوار الحالية للمستخدم
                var currentRoleIds = await _context.UserRoleAssociations
                    .Where(ura => ura.UserId == userId)
                    .Select(ura => ura.RoleId)
                    .ToListAsync();

                // الجديدة فقط
                var rolesToAdd = foundRoleIds.Except(currentRoleIds).ToList();

                // المحذوفة فقط
                var rolesToRemove = currentRoleIds.Except(foundRoleIds).ToList();

                // إضافة الأدوار الجديدة
                if (rolesToAdd.Any())
                {
                    var newAssociations = rolesToAdd.Select(rid => new UserRoleAssociation
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        RoleId = rid
                    }).ToList();

                    _context.UserRoleAssociations.AddRange(newAssociations);
                }

                // حذف الأدوار غير المطلوبة
                if (rolesToRemove.Any())
                {
                    var toRemove = await _context.UserRoleAssociations
                        .Where(ura => ura.UserId == userId && rolesToRemove.Contains(ura.RoleId))
                        .ToListAsync();

                    _context.UserRoleAssociations.RemoveRange(toRemove);
                }

                // Warnings
                if (!rolesToAdd.Any() && !rolesToRemove.Any())
                {
                    warnings.Add("لم يتم إجراء أي تعديل على الأدوار. جميع الأدوار المدخلة مطابقة للبيانات الحالية.");
                }
            }

            // ====================== حفظ التغييرات ======================
            var affected = await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var message = $"تم تحديث المستخدم بنجاح. عدد العمليات: {affected}.";
            if (warnings.Any())
                message += $" تحذيرات: {string.Join(" | ", warnings)}";

            return Result<string>.Success(message);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            errors.Add($"حدث خطأ أثناء التحديث: {ex.Message}");
            return Result<string>.Failure(errors, null);
        }
    }
    #endregion

    #region جلب المستخدمين للقائمة المنسدلة
    public async Task<Result<List<DropdownUserDto>>> GetDropDownUsersAsync()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive) // فقط المستخدمين النشطين
            .OrderBy(u => u.FullName ?? u.UserName ?? string.Empty)
            .Select(u => new DropdownUserDto
            {
                Id = u.Id,
                Name = !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : (u.UserName ?? string.Empty)
            })
            .ToListAsync();

        return Result<List<DropdownUserDto>>.Success(users);
    }
    #endregion

    #region  دوال مساعدة خاصة بالمستخدم الحالي
    // ✅ فحص السوبر أدمن (قابل للترجمة SQL)
    private Task<bool> IsSuperAdminAsync(string userId) =>
        _context.UserRoleAssociations
            .Where(ura => ura.UserId == userId)
            .Join(_context.Roles, ura => ura.RoleId, r => r.Id, (ura, r) => r.NormalizedName)
            .AnyAsync(n => n == "SUPERADMIN");

    // ✅ إحضار منفّذ الطلب + فرعه
    private async Task<(User actor, long? actorBranchId, bool isSuperAdmin)> GetActorAsync()
    {
        var actorId = _currentUserService.UserId ?? string.Empty;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId) as User;
        if (actor is null) throw new InvalidOperationException("المستخدم الحالي غير موجود.");

        var isSuper = await IsSuperAdminAsync(actorId);
        return (actor, actor.BranchId, isSuper);
    }

    // ✅ شرط نطاق الفرع للمستخدمين (يعمل للي عنده BranchId أو Department)
    private static Expression<Func<User, bool>> InBranch(long branchId) =>
        u => u.BranchId == branchId || (u.DepartmentId != null && u.Department.BranchId == branchId);
    #endregion
    private async Task<UserListItemDto> BuildUserListItemDtoAsync(string userId, string? fullNameFromRequest)
    {
        var u = await _context.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id,
                x.UserName,
                x.Email,
                x.PhoneNumber,
                x.IsActive,
                x.CreationDate
            })
            .FirstAsync();

        // Roles
        var roles = await _context.UserRoleAssociations
            .AsNoTracking()
            .Where(ura => ura.UserId == userId)
            .Select(ura => new RoleLiteDto(
                ura.RoleId,
                (ura.Role != null && !string.IsNullOrWhiteSpace(ura.Role.Name)) ? ura.Role.Name : string.Empty
            ))
            .ToListAsync();

        var roleIds = roles.Select(r => r.RoleId).Distinct().ToList();

        // Role permissions
        var rolePerms = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Permission.Name)
            .ToListAsync();

        // Direct user permissions
        var directPerms = await _context.UserPermissions
            .AsNoTracking()
            .Where(up => up.UserId == userId)
            .Select(up => up.Permission.Name)
            .ToListAsync();

        var allPermNames = directPerms
            .Concat(rolePerms)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissionsLite = allPermNames
            .Select(p => new PermissionLiteDto(p, p))
            .ToList();

        // FullName: نحاول استخدام claim إن وجد، وإلا نستخدم UserName
        string? fullName = fullNameFromRequest;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            // لو أردت القراءة من Claims store بوقت لاحق يمكنك إضافتها هنا
            fullName = u.UserName;
        }

        return new UserListItemDto
        {
            userId = u.Id,
            fullName = fullName,
            username = u.UserName ?? string.Empty,
            email = u.Email ?? string.Empty,
            phone = u.PhoneNumber,
            isActive = u.IsActive,
            createdAt = u.CreationDate,
            roles = roles.OrderBy(r => r.RoleName, StringComparer.OrdinalIgnoreCase).ToList(),
            permissionsCount = permissionsLite.Count,
            Permissions = permissionsLite
        };
    }
}

