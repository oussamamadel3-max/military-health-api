# قائمة المهام (Tasks Checklist)

- [x] **المهمة 1: تحسين هيكلية ملف الـ Result و الـ Generic Result**
  - **التفاصيل**: دراسة ملف الـ Result & Generic Result وحسم الهيكلية الجديدة للمشروع.

- [.5] **المهمة 2: دراسة ملف تحديد معدل الطلبات وبنية مجلد Infrastructure** *(للفهم والدراسة فقط)*
  - **التفاصيل**: API: دراسة وفهم ملف الـ Rate Limiting ومكونات مجلد Infrastructure (دراسة وتحليل فقط، بدون تطبيق كود).

- [ ] **المهمة 3: فلاتر عامة عبر الكيانات (Generic Filters Across Entities)**
  - **التفاصيل**: إنشاء فلاتر عامة عبر جميع الكيانات (مثلاً الفلترة عبر عمود مشترك مثل "Status" على Documents و Attachments في مكان واحد).

- [x] **المهمة 4: فحص استخدام سياق قاعدة البيانات (DbContext) في طبقة التطبيق والخدمات**
  - **التفاصيل**: التحقق من طبقة التطبيق/الخدمات والتأكد من عدم استخدام `DbContext` بشكل مباشر في أي ملف.

- [x] **المهمة 5: فحص إجراءات الأمان (CSRF، JWT و XSS)** *(للفهم والتدقيق فقط)*
  - **التفاصيل**: دراسة وفهم آليات الأمان المطبقة في النظام (CSRF, JWT & XSS) (دراسة وتحليل فني فقط، بدون تطبيق كود).

- [x] **المهمة 6: تقسيم ملفات تعريف الخرائط (Mapping Profiles)**
  - **التفاصيل**: تقسيم ملف `mapProfile.cs` إلى ملفات تعريف خرائط منفصلة ومستقلة (على سبيل المثال: `RoleProfile.cs` و `documentsProfile.cs`).

- [x] **المهمة 7: فحص عمليات التحويل اليدوي (Manual Mapping)**
  - **التفاصيل**: التأكد من عدم وجود عمليات تحويل يدوية بين النماذج (التحويل يدوياً دون استخدام ملفات الـ Mapping).

- [x] **المهمة 8: فصل تعليقات الكيانات (Data Annotations) إلى ملفات مستقلة**
  - **التفاصيل**: فصل إعدادات قاعدة البيانات و Data Annotations الخاصة بالكيانات إلى ملفات تكوين مستقلة.

- [x] **المهمة 9: ترقية المشروع بالكامل إلى .NET 10 (Phase 1)**
  - **التفاصيل**: ترقية تقنية فقط، بدون تغيير أي سلوك وظيفي:
    - `API` و `Application` و `Domain` و `Infrastructure`: `TargetFramework` من `net8.0` إلى `net10.0`.
    - إضافة `global.json` لتثبيت الحل على `.NET 10 SDK 10.0.300` مع `rollForward: latestPatch` (البقاء داخل `10.0.3xx`).
    - عائلة `Microsoft.EntityFrameworkCore` (`.Abstractions`, `.Design`, `.Relational`, `.SqlServer`, `.Tools`) موحّدة على `10.0.12`.
    - `Microsoft.AspNetCore.Authentication.JwtBearer` و `Microsoft.AspNetCore.Identity.EntityFrameworkCore` و `Microsoft.Extensions.Identity.Core` من `8.0.21`/`9.0.10` إلى `10.0.12`.
    - `Npgsql.EntityFrameworkCore.PostgreSQL` من `9.0.1` إلى `10.0.3` (يتوافق مع EF Core `>= 10.0.4 && < 11.0.0`).
    - `System.IdentityModel.Tokens.Jwt` من `8.14.0` إلى `8.19.2` لتوحيد عائلة IdentityModel مع ما يطلبه JwtBearer 10.
    - `Swashbuckle.AspNetCore` بقي على `9.0.6` بدون تغيير، وكود Swagger و Rate Limiting لم يُلمس.
    - لم تُنشأ أي migration جديدة: `dotnet ef migrations has-pending-model-changes` أرجع "No changes".
    - حذف `Application.Tests/` (كان يحتوي `bin` و `obj` فقط، وهي stale artifacts غير متتبعة في Git).
  - **نتائج التحقق**:
    - `dotnet restore` و `dotnet build` (rebuild كامل) ناجحان بـ 0 أخطاء، وتحذيرات الـ C# مطابقة تمامًا لما قبل الترقية (27 تحذيرًا في الحالتين).
    - `dotnet ef migrations has-pending-model-changes` أرجع "No changes"، وعند التشغيل: "No migrations were applied. The database is already up to date."
    - Smoke Test ناجح بالكامل على .NET 10: الإقلاع و seeding، واتصال PostgreSQL 18، و Swagger UI و `swagger.json`، وتسجيل الدخول مع كوكيز `access_token`/`refresh_token`/`csrf_sig` بخصائص HttpOnly و Secure، و `GET /api/Auth/me` أرجع `superAdmin` بدور SuperAdmin و 22 صلاحية، و 401 بدون توكن، وقراءة `Documents` و `Dashboard`، وكتابة كاملة (إنشاء دور ثم قراءته ثم حذفه ثم التأكد من 404).
    - Rate Limiting لم يتغير سلوكه: سياسة `login` توقفت عند 5 محاولات بالضبط، و middleware الـ TokenBucket المخصص أعطى 40 طلبًا ناجحًا ثم 20 طلبًا بـ 429 مع `Retry-After: 10` و `X-RateLimit-Error-Message`، وهو مطابق تمامًا لـ `TokenLimit = 40`.
  - **ملاحظات مؤجّلة (خارج نطاق Phase 1، للتوثيق فقط)**:
    - سياسة `login` كانت تُرجع `503` (قيمة افتراضية لـ `RejectionStatusCode`) — **عُولج في المهمة 12 (Phase 2)** وأصبح `429` مع `ApiResponse`.
    - `API/Dockerfile` قديم ومكسور: يبني `ArchiveProject/ArchiveProject.csproj` وهو مشروع غير موجود في هذا الحل، وصوره على `aspnet:8.0`/`sdk:8.0`. لم يُصلح لأنه غير مستخدم فعليًا في build/run.
    - لا يوجد `docker-compose` ولا `Directory.Build.props` ولا `Directory.Packages.props`، و `.github/workflows/` فارغ (لا CI). لم يُنشأ أي منها.
    - `app.UseForwardedHeaders(...)` كان مُسجَّلًا مرتين — **عُولج في المهمة 12 (Phase 2)** (تسجيل واحد آمن عبر `UseAppForwardedHeaders`).
    - تحذير `NU1510` جديد على `Microsoft.AspNetCore.Http.Abstractions 2.2.0` في `Application`: ميزة package pruning في .NET 10 تعتبر الحزمة غير ضرورية لأن الإطار المشترك يوفّرها. تحذير إرشادي فقط ولا يؤثر على build أو runtime.
    - تحذير `NU1903` على `AutoMapper 12.0.1` كان مؤجّلًا هنا؛ عُالج لاحقًا في **المهمة 10** باستبدال AutoMapper بـ Mapster.
    - حزم غير مستخدمة بقيت كما هي: `Microsoft.AspNetCore.Http.Features 5.0.17` و `Npgsql.EntityFrameworkCore.PostgreSQL` في `Domain`، وحزم SqlServer/`Microsoft.Data.SqlClient` مع أن التطبيق يعمل على `UseNpgsql`.
    - ترقية `Swashbuckle` إلى `10.x` تتطلب migration إلى `Microsoft.OpenApi 2.x` وتعديل `SwaggerExtensions.cs` → مهمة مستقلة.
    - أداة `dotnet-ef` العامة على `10.0.7` وهي أقدم من runtime `10.0.12` (تحذير إرشادي فقط).

- [x] **المهمة 10: استبدال AutoMapper بـ Mapster**
  - **التفاصيل**: إزالة `AutoMapper` بالكامل واستبداله بـ `Mapster` (MIT) بسبب ثغرة `CVE-2026-32933` / `GHSA-rvv3-g6hj-g44x` في `AutoMapper 12.0.1`، ولأن الإصدارات المُصلَحة (`15.1.1` / `16.1.1`) تتطلب ترخيصًا تجاريًا:
    - حذف `AutoMapper.Extensions.Microsoft.DependencyInjection 12.0.1` من `Application.csproj`.
    - إضافة `Mapster 10.0.12` و `Mapster.DependencyInjection 10.0.12`.
    - تحويل ملفات الـ Profiles الأربعة (`AuthProfile`, `DocumentProfile`, `AuditLogProfile`, `AttachmentProfile`) من `Profile` إلى `IRegister` مع `TypeAdapterConfig.NewConfig`.
    - نقل كل قواعد التحويل حرفيًا: `CreateMap` → `NewConfig`، `ForMember/MapFrom` → `Map`، `Ignore()` → `Ignore()`، `ReverseMap()` → `TwoWays()`.
    - الخدمات الخمس (`RoleService`, `UserService`, `DocumentService`, `AttachmentService`, `AuditLogService`): تبديل `using AutoMapper` إلى `using MapsterMapper` فقط؛ مواضع `_mapper.Map(...)` بقيت كما هي بفضل توافق واجهة `IMapper`.
    - التسجيل في `ApplicationServicesRegiseration`: `TypeAdapterConfig` + `Scan` + `Compile()` عند الإقلاع + `AddScoped<IMapper, ServiceMapper>()`.
  - **نتائج التحقق**:
    - `dotnet restore` و `dotnet build` ناجحان بـ 0 أخطاء، ولا يوجد `NU1903` بعد الإزالة.
    - `dotnet list package --vulnerable --include-transitive` على المشاريع الأربعة: لا حزم مصابة.
    - لا يبقى أي ذكر لـ `AutoMapper` في ملفات `.cs` أو `.csproj`.
    - `config.Compile()` عند الإقلاع مرّ بلا استثناء (كل الخرائط العشرين صالحة بنيويًا).
    - Smoke Test على الموضعين الخطرين (تحويل بمعاملين على كيان متتبَّع من EF):
      - تحديث دور بعد منحه صلاحية: `description` تغيّر، و`permissionsCount` بقي `1` (لم تُمسَح `RolePermissions`)، و`isActive` و`createdAt` و`assignedUsersCount` محفوظة، و`updatedAt` حُدِّث من الخدمة.
      - تحديث مستند: `title` و`description` تغيّرا، و`documentNumber` و`userId` و`createdAt` و`createdBy` محفوظة.
    - مسارات قراءة معتمدة على التحويل نجحت (`Documents`, `Roles`, `Permissions`) بـ HTTP 200 و`success=true`.
  - **ملاحظات مؤجّلة (للتوثيق فقط)**:
    - تحقّق الطرف-إلى-الطرف لمسار `AuditLog/GetAllFiltered` لم يُكمَل في مهمة الاستبدال (المسار `POST`). عُالج مسار التحويل لاحقًا في المهمة 11 عبر Mapster؛ يُفضَّل تغطية HTTP في اختبار تكامل لاحق.
    - تحذيرات nullable القديمة في `AuthProfile` (CS8602/CS8604 على `RolePermissions`) بقيت كما كانت قبل الاستبدال؛ ليست انحدارًا من Mapster.
    - ملاحظة سابقة عن `RoleService.GetById` اليدوي وعن عدم استخدام خرائط `PermissionWithRoles*`: عُولجت في **المهمة 11**.

- [x] **المهمة 11: تدقيق شامل وإزالة الـ Manual Mapping (Entity ↔ DTO عبر Mapster فقط)**
  - **التفاصيل**: فحص كامل `backend-template` (Controllers / Services / Repositories / Helpers) وتحويل كل نسخ خصائص Entity↔DTO اليدوي إلى Mapster، دون الاكتفاء بالـ Profiles السابقة:
    - توسيع `AuthProfile` و`DocumentProfile` و`AuditLogProfile` و`AttachmentProfile`، وإضافة `FieldPermissionProfile`.
    - إصلاح المواضع اليدوية، ومنها: `RoleService.GetById`، و`PermissionService` بالكامل، و`ReportsService`، و`DocumentRepository` dropdown (نُقل التحويل إلى الخدمة)، و`DocumentService`/`AttachmentLinkDto`، و`UserService`/`UserListItemDto`/`DropdownUserDto`/`RoleLiteDto`/`PermissionLiteDto`، و`AuthService`/`MeResponseDto`، و`FieldPermissionService`، و`DashboardService`/`RecentActivityDto`.
    - الإثراء بعد التحويل مسموح (مثل تعبئة `FileUrl`، و`DocumentTitle`، وعدّادات الأدوار/الصلاحيات) طالما نسخ الحقول الأساسية يمر عبر Mapster.
  - **نتائج التحقق**:
    - `dotnet build` ناجح بـ 0 أخطاء.
    - بحث ثانٍ بأسماء محددة: **0** مواضع Entity↔DTO يدوية.
    - بحث عام أخير (أشمل من قائمة الأسماء):
      1. `new\s+\w*Dto` عبر `API` / `Application` / `Infrastructure` / `Domain`
      2. `\.Select(...=> new \w*Dto`
      3. تعيينات `dto.*` / `entity.*` الشائعة
    - مراجعة يدوية لكل النتائج: لا يوجد نسخ خصائص Entity↔DTO خارج Mapster.
    - النتيجة الرسمية:
      ```text
      Manual Entity ↔ DTO mappings found: 0

      All direct Entity ↔ DTO mappings are handled through Mapster.

      Remaining explicit DTO construction is limited to:
      - aggregations
      - result/token wrappers
      - query filters
      - metadata
      - computed/enriched fields
      - DB relationship entities
      ```
    - تحسّن وظيفي جانبي: `RoleService.GetById` أصبح يُرجع `Description` و`IsActive` عبر Mapster بدل فقدانهما في البناء اليدوي السابق.
  - **استثناءات موثّقة (ليست Entity↔DTO) — نتائج البحث العام**:
    - **أغلفة توكن/نتيجة**: `LoginResponseDto` / `CsrfResponseDto` (`AuthController`)، `TokenResponseDto` / `TokenGenerationResultDto` (`TokenService`)، `ExportFileDto` (`ExportService`)، `CreateFieldPermissionsResultDto`.
    - **تجميعات**: `AuditLogSummaryDto`، `ActiveUserStatsDto`، `DocumentGrowthPointDto`، `StorageUsageDto`، `DashboardStatsDto`.
    - **فلاتر استعلام**: `FieldPermissionFilterDto` (`FieldPermissionHelper` / `FieldPermissionsController`).
    - **Metadata / enum**: `EntityInfoDto`، `FieldInfoDto`، `FieldAccessTypeDto`.
    - **سياق تشغيل**: `CurrentUserDto` في `AppDbContext` من `ICurrentUserService`.
    - **حقول محسوبة/إثراء بعد Mapster** (وليست نسخ Entity→DTO): تعبئة `Roles`/`Users` على `FieldPermissionDto`، و`roles`/`Permissions`/العدادات على `UserListItemDto`، و`FileUrl`/`FileSizeFormatted` على مرفقات، و`Name` العربي/`CreatedByName` على تفاصيل الدور، وتعقيم حقول الـ DTO قبل `_mapper.Map`، وتعديل حقول الكيان لأغراض عمل (مثل `DocumentNumber` أو `Description` المحسوبة).
    - **كيانات ربط DB**: `UserRoleAssociation`، `RolePermission`، `UserPermission`، `UserFieldPermission` (ليست DTOs).

- [x] **المهمة 12: توحيد Rate Limiting على Microsoft.AspNetCore.RateLimiting (Phase 2)**
  - **التفاصيل**: استبدال النظام المزدوج (FixedWindow لتسجيل الدخول + `RateLimitingMiddleware` TokenBucket مخصص) بنظام واحد فقط على .NET 10:
    - مجلد `API/RateLimiting/`: سياسات `login` / `reports` / `public-api`، إعدادات مع `ValidateOnStart`، مفتاح تقسيم `user:{id}` أو `ip:{…}` (بدون `user:unknown`)، `ForwardedHeaders` آمن، `OnRejected` يوحّد `429` + `ApiResponse` (`rate_limit_exceeded`) و`X-RateLimit-Error-Message` و`Retry-After` عند التوفر.
    - Global TokenBucket (مستخدم/IP)، Login SlidingWindow (IP)، Reports: Concurrency ثم TokenBucket عبر `SequentialRateLimiter`، وتسجيل سياسة Public دون endpoints وهمية.
    - خط الأنابيب: Authentication ثم RateLimiter؛ `MapHealthChecks("/health").DisableRateLimiting()`؛ حذف الـ middleware المخصص وازدواجية ForwardedHeaders.
    - `[EnableRateLimiting]` على Login وثلاث عمليات تصدير في `ReportsController`.
    - مشروع `API.Tests` (TestServer، `Environment=Testing`، بدون PostgreSQL) يغطي العزل Global، Login 429≠503، Reports concurrency، Health bypass، ForwardedHeaders trusted/untrusted، وسقوط auth بدون userId إلى IP.
  - **نتائج التحقق**:
    - `dotnet build Backend.sln` ناجح (0 أخطاء).
    - `dotnet test API.Tests`: **9/9 Passed**.
    - `dotnet ef migrations has-pending-model-changes`: No changes.
    - لا بقايا لـ `RateLimitingMiddleware` أو `AddMemoryCache` المرتبط به.
    - Smoke على API حقيقي (`https://localhost:7265`):
      - Login بمحاولات خاطئة: 1–5 → `401`؛ السادسة → **`429`** مع `rate_limit_exceeded` (وليس 503).
      - `/health` → `200 Healthy` حتى بعد استنفاد Login.
      - تسجيل دخول `superAdmin` → `200`؛ `/api/Auth/me` → `200`؛ تصدير Dashboard Excel → `200` (~8KB).
