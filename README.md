# ArchiveProject - نظام المصادقة والصلاحيات

## ⚠️ ملاحظات مهمة - اقرأها أولاً!

### **🔴 كيان الموظف (Employee) للتجريب فقط**

**جميع الأمثلة والكود المتعلق بـ `Employee` هي للتجريب فقط. يمكنك الاستفادة منها كدليل ثم حذفها.**

**الملفات المتعلقة:**
- `ArchiveProject.Domain/Entities/Employee.cs`
- `ArchiveProject.Application/Dtos/EmployeeDtos/`
- `ArchiveProject.Infrastructure/Services/EmployeeService.cs`
- `ArchiveProject/Controllers/EmployeesController.cs`

---

### 1. EntityHelper - Namespace مهم ⚠️

عند إضافة كيانات في namespace مختلف عن `ArchiveProject.Domain.Entities`، يجب تحديث:

**الملف**: `ArchiveProject.Infrastructure/Persistence/EntityHelper.cs` (السطر 34)

```csharp
// مثال: إضافة namespace جديد
if (propertyType.Namespace?.StartsWith("ArchiveProject.Domain.Entities") == true ||
    propertyType.Namespace?.StartsWith("YourProject.Domain.Entities") == true)
    return true;
```

**لماذا؟** `EntityHelper` يحدد Navigation Properties. بدون تحديث namespace، قد تُعامل الكيانات الجديدة كحقول عادية.

---

### 2. حقول التدقيق (BaseEntity)

**لا حاجة لأي كود إضافي** - فقط ارث من `BaseEntity`:

```csharp
public class Product : BaseEntity
{
    [Key]
    public long Id { get; set; }
    // ...
}
```

**النتيجة**: الحقول التالية تُعبأ تلقائياً:
- `CreatedAt`, `CreatedBy` (عند الإنشاء)
- `UpdatedAt`, `UpdatedBy` (عند التعديل)

---

### 3. صلاحيات الحقول

- ✅ إضافة `[FieldPermissionEntity]` على الكيان
- ✅ إضافة `[FieldPermission]` على الحقول في DTO
- ✅ تطبيق الصلاحيات في Service باستخدام `FieldPermissionHelper`

---

## 📋 نظرة عامة

نظام مصادقة وصول مركزي مبني على ASP.NET Core 8.0 يوفر:
- 🔐 JWT Authentication مع Refresh Tokens
- 👥 إدارة المستخدمين والأدوار
- 🔑 صلاحيات على مستوى الحقول
- 📝 سجل تدقيق تلقائي (AuditLog)
- 🛡️ Rate Limiting, Error Handling, CORS
- 🏗️ Clean Architecture

---

## 🏗️ البنية المعمارية

```
ArchiveProject/
├── Domain/          # Entities, Enums, Attributes
├── Application/     # DTOs, Interfaces, Results
├── Infrastructure/  # DbContext, Services, Migrations
└── API/            # Controllers, Middleware
```

---

## 📝 سجل التدقيق (AuditLog)

### التفعيل

اجعل الكيان يرث من `IAuditableEntity`:

```csharp
using ArchiveProject.Domain.Interfaces;
using ArchiveProject.Domain.Common;

public class Product : BaseEntity, IAuditableEntity
{
    [Key]
    public long Id { get; set; }
    // ...
    
    public long GetPrimaryKey() => Id; // مطلوب!
}
```

**النتيجة**: يتم تسجيل جميع عمليات Create/Update/Delete تلقائياً.

### ما يتم تسجيله؟

- `UserId`, `UserName`, `IPAddress`
- `ActionType` (Added/Modified/Deleted)
- `TableName`, `RecordId`
- `OldValue`, `NewValue` (JSON)
- `ChangedFields`, `ChangeSummary`
- `Timestamp`

**الحقول المستثناة**: `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`

### جلب السجلات

```http
GET /api/AuditLog/GetAllFiltered?pageNumber=1&pageSize=10
GET /api/AuditLog/GetById/{id}
GET /api/AuditLog/GetAuditEntities
```

**الصلاحية المطلوبة**: `AuditLog.Read`

---

## 🔐 نظام الصلاحيات

### 1. صلاحيات على مستوى الكيان

#### الاستخدام في Controller

```csharp
[HttpGet("[action]")]
[AuthorizePermission("ProductManagement.Read")]
public async Task<IActionResult> GetAll()
{
    // ...
}
```

#### إضافة صلاحية جديدة

في `DbInitializer.cs` (السطر ~84):

```csharp
var MyPermissions = new[]
{
    // ...
    new { Name = "ProductManagement.Read", Desc = "قراءة المنتجات" },
    // ======== هنا يجب اضافة الصلاحيات الثابتة الخاصة بك ========
};
```

**نمط التسمية**: `<EntityName>.<Action>` (مثال: `UserManagement.Create`)

---

### 2. صلاحيات على مستوى الحقول

#### أنواع الوصول

```csharp
public enum FieldAccessType
{
    None = 0,        // مخفي تماماً
    Read = 1,        // قراءة فقط
    Write = 2,       // كتابة فقط
    ReadWrite = 3    // قراءة وكتابة
}
```

#### التطبيق

**الخطوة 1**: إضافة Attribute للكيان
```csharp
[FieldPermissionEntity(DisplayName = "المنتج")]
public class Product : BaseEntity { }
```

**الخطوة 2**: إضافة Attributes في DTO
```csharp
public class ProductDto
{
    [FieldPermission("Price", DisplayName = "السعر")]
    public decimal? Price { get; set; }
}
```

**الخطوة 3**: تطبيق في Service
```csharp
var userId = _userContextService.UserId ?? string.Empty;
var roleIds = _userContextService.RoleIds?.ToList() ?? new List<string>();
var jwtFieldPermissions = _userContextService.FieldPermissions;

dto = await _fieldPermissionHelper.ApplyFieldPermissionsAsync(
    dto, userId, roleIds, "Product", jwtFieldPermissions);
```

#### إدارة الصلاحيات

```http
POST /api/FieldPermissions/Create
GET /api/FieldPermissions/GetFieldPermissions?pageNumber=1&pageSize=10&entityName=Product
GET /api/FieldPermissions/GetFields/Product
```

---

## 🔑 نظام المصادقة (JWT)

### الحصول على Token

```http
POST /api/Auth/RequestToken
{
  "userName": "admin",
  "password": "password123"
}
```

### Refresh Token

```http
POST /api/Auth/RefreshToken
{
  "refreshToken": "<token>"
}
```

### استخدام Token

```http
GET /api/Products/GetAll
Authorization: Bearer <token>
```

---

## ⏳ Rate Limiting

### التعديل

**الملف**: `ArchiveProject/Middleware/RateLimitingMiddleware.cs` (السطور 22-30)

```csharp
TokenLimit = 40,                    // 40 طلب دفعة واحدة
TokensPerPeriod = 10,               // 10 طلبات
ReplenishmentPeriod = TimeSpan.FromSeconds(10), // كل 10 ثوانٍ
```

**الافتراضي**: ≈ 60 طلب/دقيقة

**عند تجاوز الحد**: HTTP 429 Too Many Requests

---

## 🛠️ الأدوات المساعدة

### Result Pattern

```csharp
var result = await _service.GetByIdAsync(id);
if (result.Succeeded)
    var data = result.Value;
else
    var errors = result.Errors;
```

### PaginatedList

```csharp
var result = await _service.GetAllAsync(page: 1, size: 10);
var items = result.Value.Items;
var totalCount = result.Value.TotalCount;
```

---

## 🚀 التثبيت والتشغيل

### المتطلبات

- .NET 8.0 SDK
- SQL Server 2019+

### الخطوات

1. **استنساخ المشروع**
   ```bash
   git clone <repository-url>
   cd ArchiveProject
   ```

2. **تحديث Connection String** في `appsettings.json`
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=YOUR_SERVER;Database=ArchiveProjectDb;..."
     }
   }
   ```

3. **تطبيق Migrations**
   ```bash
   dotnet ef database update --project ArchiveProject.Infrastructure --startup-project ArchiveProject
   ```

4. **تشغيل المشروع**
   ```bash
   dotnet run --project ArchiveProject
   ```

5. **الوصول إلى Swagger**: `https://localhost:7265/swagger`

---

## 🔧 الإعدادات

### JWT Settings (`appsettings.json`)

```json
{
  "JwtSettings": {
    "Key": "your-secret-key",
    "Issuer": "https://localhost:4200/defdev",
    "Audience": "your-audience-id",
    "ExpirationInMinutes": 30,
    "RefreshTokenExpirationInDays": 30
  }
}
```

### CORS

تعديل إعدادات CORS في `CorsExtensions.cs`

---

## 📦 البنية التحتية

### Migrations

**إنشاء Migration:**
```bash
dotnet ef migrations add MigrationName --project ArchiveProject.Infrastructure --startup-project ArchiveProject
```

**تطبيق Migrations:**
```bash
dotnet ef database update --project ArchiveProject.Infrastructure --startup-project ArchiveProject
```

### Seed Data

يتم تنفيذ Seed Data تلقائياً عند بدء التطبيق. التعديل في `DbInitializer.cs`

---

## 🔒 إجبار تسجيل الخروج عند تغيير الصلاحيات

عند تعديل صلاحيات/أدوار/صلاحيات حقول لمستخدم أو دور، يتم **إجبار تسجيل الخروج تلقائياً** عبر تحديث `SecurityStamp`.

**يعمل تلقائياً** - لا حاجة لأي كود إضافي! ✅

---

