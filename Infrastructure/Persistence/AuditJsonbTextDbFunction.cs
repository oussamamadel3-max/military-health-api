using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence;

/// <summary>
/// يسجّل ترجمة EF لـ <see cref="AuditJsonbText.Contains"/> إلى
/// <c>(jsonb)::text LIKE '%' || substring || '%'</c> لتفادي PostgreSQL 42883.
/// </summary>
internal static class AuditJsonbTextDbFunction
{
    public static void Register(ModelBuilder modelBuilder)
    {
        var method = typeof(AuditJsonbText).GetMethod(
            nameof(AuditJsonbText.Contains),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(string)],
            modifiers: null)
            ?? throw new InvalidOperationException("AuditJsonbText.Contains not found.");

        modelBuilder.HasDbFunction(method).HasTranslation(args =>
        {
            var jsonArg = args[0];
            var substringArg = args[1];
            var textMapping = new StringTypeMapping("text", DbType.String);

            // (jsonb)::text
            var asText = new SqlUnaryExpression(
                ExpressionType.Convert,
                jsonArg,
                typeof(string),
                textMapping);

            // '%' || substring || '%'
            var percent = new SqlConstantExpression("%", textMapping);
            var pattern = new SqlBinaryExpression(
                ExpressionType.Add,
                new SqlBinaryExpression(
                    ExpressionType.Add,
                    percent,
                    substringArg,
                    typeof(string),
                    textMapping),
                percent,
                typeof(string),
                textMapping);

            return new LikeExpression(
                asText,
                pattern,
                escapeChar: null,
                typeMapping: null);
        });
    }
}
