using Application.Common.Results;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Application.Services.ReferenceData;

internal static class ReferenceDataSupport
{
    public static string NormalizeCatalog(string catalog)
    {
        var normalized = (catalog ?? string.Empty).Trim().ToLowerInvariant();
        if (!ReferenceCatalogs.All.Contains(normalized))
            throw new InvalidReferenceDataException("كتالوج مرجعي غير معروف.");
        return normalized;
    }

    public static void NormalizePaging(ref int pageNumber, ref int pageSize)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 500) pageSize = 500;
    }

    public static void ValidateCommon(ReferenceDataWriteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            throw new InvalidReferenceDataException("الرمز مطلوب.");
        if (string.IsNullOrWhiteSpace(dto.NameAr))
            throw new InvalidReferenceDataException("الاسم بالعربية مطلوب.");
    }

    public static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static async Task<PaginatedList<ReferenceDataItemDto>> ToPageAsync<T>(
        IQueryable<T> query,
        int page,
        int size,
        Func<T, ReferenceDataItemDto> map,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PaginatedList<ReferenceDataItemDto>(rows.Select(map).ToList(), total, page, size);
    }

    public static IQueryable<T> ApplyActiveFilter<T>(IQueryable<T> query, bool? isActive, Expression<Func<T, bool>> predicate)
        => isActive.HasValue ? query.Where(predicate) : query;

    public static IQueryable<T> ApplySearch<T>(IQueryable<T> query, string? search, Expression<Func<T, bool>> predicate)
        => string.IsNullOrWhiteSpace(search) ? query : query.Where(predicate);

    public static Guid ParseGuidId(string id)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ReferenceDataNotFoundException();
        return guid;
    }

    public static short ParseShortId(string id)
    {
        if (!short.TryParse(id, out var key))
            throw new ReferenceDataNotFoundException();
        return key;
    }

    public static Expression<Func<T, bool>> CodeEquals<T>(string code)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var prop = Expression.Property(param, "Code");
        var toLower = Expression.Call(prop, nameof(string.ToLower), Type.EmptyTypes);
        var equal = Expression.Equal(toLower, Expression.Constant(code.ToLower()));
        return Expression.Lambda<Func<T, bool>>(equal, param);
    }

    public static Expression<Func<T, bool>> NameEquals<T>(string name)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var prop = Expression.Property(param, "NameAr");
        var equal = Expression.Equal(prop, Expression.Constant(name));
        return Expression.Lambda<Func<T, bool>>(equal, param);
    }
}
