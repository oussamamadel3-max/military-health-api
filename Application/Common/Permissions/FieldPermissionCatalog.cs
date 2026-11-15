using Application.Attributes;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using System.Reflection;
using FieldPermissionEntityAttribute = Domain.Attributes.FieldPermissionEntityAttribute;
using UserDto = Application.Dtos.AuthDtos.UserDto;

namespace Application.Common.Permissions;

/// <summary>
/// اكتشاف كتالوج صلاحيات الحقول من Domain types ذات
/// <see cref="FieldPermissionEntityAttribute"/> وحقول DTO ذات
/// <see cref="FieldPermissionAttribute"/> المرتبطة باسم الكيان فقط.
/// </summary>
public sealed class FieldPermissionCatalog : IFieldPermissionCatalog
{
    private static readonly Assembly DomainAssembly = typeof(User).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(UserDto).Assembly;

    private readonly Lazy<CatalogSnapshot> _snapshot;

    public FieldPermissionCatalog()
    {
        _snapshot = new Lazy<CatalogSnapshot>(BuildSnapshot);
    }

    /// <summary>يبني كتالوجاً من تجميعات مخصّصة (للاختبارات).</summary>
    public FieldPermissionCatalog(Assembly domainAssembly, Assembly applicationAssembly)
    {
        _snapshot = new Lazy<CatalogSnapshot>(() => BuildSnapshot(domainAssembly, applicationAssembly));
    }

    public IReadOnlyList<EntityInfoDto> GetEntities()
    {
        return _snapshot.Value.Entities
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FieldInfoDto> GetFields(string entityName)
    {
        if (!TryGetEntry(entityName, out var entry))
            throw new RoleNotFoundException($"الكيان '{entityName}' غير موجود في كتالوج صلاحيات الحقول.");

        return entry.Fields
            .OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FieldInfoDto { Name = f.Name, DisplayName = f.DisplayName })
            .ToList();
    }

    public bool IsEntityRegistered(string entityName)
        => TryGetEntry(entityName, out _);

    public bool IsFieldRegistered(string entityName, string fieldName)
    {
        if (!TryGetEntry(entityName, out var entry))
            return false;

        return entry.Fields.Any(f => f.Name.Equals(fieldName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public string GetEntityDisplayName(string entityName)
        => TryGetEntry(entityName, out var entry) ? entry.DisplayName : entityName;

    public IReadOnlyDictionary<string, string> GetFieldDisplayMap(string entityName)
    {
        if (!TryGetEntry(entityName, out var entry))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return entry.Fields.ToDictionary(
            f => f.Name,
            f => f.DisplayName,
            StringComparer.OrdinalIgnoreCase);
    }

    private bool TryGetEntry(string entityName, out EntityEntry entry)
    {
        entry = default!;
        if (string.IsNullOrWhiteSpace(entityName))
            return false;

        return _snapshot.Value.ByName.TryGetValue(entityName.Trim(), out entry!);
    }

    private static CatalogSnapshot BuildSnapshot()
        => BuildSnapshot(DomainAssembly, ApplicationAssembly);

    private static CatalogSnapshot BuildSnapshot(Assembly domainAssembly, Assembly applicationAssembly)
    {
        var entityTypes = domainAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<FieldPermissionEntityAttribute>() != null)
            .ToList();

        var dtoFieldIndex = IndexDtoFields(applicationAssembly);
        var byName = new Dictionary<string, EntityEntry>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<EntityInfoDto>();

        foreach (var type in entityTypes)
        {
            var attr = type.GetCustomAttribute<FieldPermissionEntityAttribute>();
            var displayName = string.IsNullOrWhiteSpace(attr?.DisplayName) ? type.Name : attr!.DisplayName!;
            var fields = ResolveFieldsForEntity(type.Name, dtoFieldIndex);

            byName[type.Name] = new EntityEntry(type.Name, displayName, fields);
            entities.Add(new EntityInfoDto { Name = type.Name, DisplayName = displayName });
        }

        return new CatalogSnapshot(entities, byName);
    }

    private static List<FieldEntry> ResolveFieldsForEntity(
        string entityName,
        IReadOnlyList<DtoFieldRecord> dtoFieldIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in dtoFieldIndex)
        {
            if (!record.DtoTypeName.Contains(entityName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!map.ContainsKey(record.FieldName) || map[record.FieldName] == record.FieldName)
                map[record.FieldName] = record.DisplayName;
        }

        return map
            .Select(kvp => new FieldEntry(kvp.Key, kvp.Value))
            .ToList();
    }

    private static List<DtoFieldRecord> IndexDtoFields(Assembly applicationAssembly)
    {
        var result = new List<DtoFieldRecord>();
        try
        {
            foreach (var type in applicationAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                foreach (var property in type.GetProperties())
                {
                    var attr = property.GetCustomAttribute<FieldPermissionAttribute>();
                    if (attr == null)
                        continue;

                    var fieldName = string.IsNullOrWhiteSpace(attr.FieldName) ? property.Name : attr.FieldName;
                    var displayName = string.IsNullOrWhiteSpace(attr.DisplayName) ? fieldName : attr.DisplayName!;
                    result.Add(new DtoFieldRecord(type.Name, fieldName, displayName));
                }
            }
        }
        catch (ReflectionTypeLoadException)
        {
            // تجاهل أخطاء تحميل الأنواع الجزئية
        }

        return result;
    }

    private sealed record CatalogSnapshot(
        IReadOnlyList<EntityInfoDto> Entities,
        IReadOnlyDictionary<string, EntityEntry> ByName);

    private sealed record EntityEntry(string Name, string DisplayName, IReadOnlyList<FieldEntry> Fields);

    private sealed record FieldEntry(string Name, string DisplayName);

    private sealed record DtoFieldRecord(string DtoTypeName, string FieldName, string DisplayName);
}
