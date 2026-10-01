using System.Reflection;

namespace Application.Services.Export;

public static class ExportEmbeddedResources
{
    private static readonly Assembly Assembly = typeof(ExportEmbeddedResources).Assembly;
    private static readonly string[] ResourceNames = Assembly.GetManifestResourceNames();

    public static Stream? Open(string fileName)
    {
        var id = ResourceNames.FirstOrDefault(n =>
            n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase) ||
            n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (id is null)
            return null;

        var stream = Assembly.GetManifestResourceStream(id);
        if (stream is null)
            return null;

        if (stream.CanSeek)
            stream.Position = 0;

        return stream;
    }

    public static byte[]? ReadBytes(string fileName)
    {
        using var stream = Open(fileName);
        if (stream is null)
            return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static string? ReadText(string fileName)
    {
        var bytes = ReadBytes(fileName);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}
