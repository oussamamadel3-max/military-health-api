using System.Security.Cryptography;
using System.Text;

namespace Domain.Common;

public static class AuditPrimaryKey
{
    /// <summary>
    /// يحوّل Guid إلى long مستقر لفهرسة AuditLog.RecordId.
    /// </summary>
    public static long FromGuid(Guid id)
    {
        var bytes = id.ToByteArray();
        return BitConverter.ToInt64(bytes, 0);
    }

    /// <summary>
    /// يحوّل مفتاح Identity النصي (غالباً Guid) إلى long مستقر.
    /// </summary>
    public static long FromString(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return 0;

        if (Guid.TryParse(id, out var guid))
            return FromGuid(guid);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return BitConverter.ToInt64(hash, 0);
    }
}
