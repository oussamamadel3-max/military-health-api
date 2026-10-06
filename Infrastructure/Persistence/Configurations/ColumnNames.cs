using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal static class ColumnNames
{
    public static void UseCamelCaseColumns<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        // Property(string) creates or promotes shadow properties (EF temporary *TempId keys).
        // Rename only real CLR columns, through the property metadata that already exists.
        foreach (var property in builder.Metadata.GetProperties().ToList())
        {
            if (property.IsShadowProperty())
                continue;

            var name = property.Name;
            if (string.IsNullOrEmpty(name))
                continue;

            property.SetColumnName(char.ToLowerInvariant(name[0]) + name[1..]);
        }
    }
}
