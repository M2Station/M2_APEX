using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Listly.Models;

/// <summary>
/// Loads a JSON list bundled as an embedded resource (the shared support-list files under <c>Assets/</c>).
/// The bundled file is the single source of truth: on any failure an empty list is returned rather than
/// duplicating the data as a hard-coded fallback, so editing the JSON is the only change ever needed.
/// </summary>
internal static class EmbeddedCatalog
{
    /// <summary>Deserializes the embedded resource whose name ends with <paramref name="fileName"/>.</summary>
    public static List<T> Load<T>(string fileName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

            if (resource is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    var list = JsonSerializer.Deserialize<List<T>>(stream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (list is not null)
                        return list;
                }
            }
        }
        catch
        {
            // The file ships embedded, so this should never happen; the JSON is the single source of truth.
        }

        return new List<T>();
    }
}
