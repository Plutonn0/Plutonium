using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using XboxAuthNet.Game.Accounts.JsonStorage;

namespace PlutoniumLauncher;

internal sealed class DpapiJsonFileStorage(string filePath) : IJsonStorage
{
    public JsonNode? ReadAsJsonNode()
    {
        if (!File.Exists(filePath)) return null;
        var encrypted = File.ReadAllBytes(filePath);
        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonNode.Parse(json);
    }

    public void Write(JsonNode node, JsonSerializerOptions? serializerOptions)
    {
        using var buffer = new MemoryStream();
        JsonSerializer.Serialize(buffer, node, serializerOptions);
        var encrypted = ProtectedData.Protect(buffer.ToArray(), optionalEntropy: null, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                file.Write(encrypted);
                file.Flush(flushToDisk: true);
            }
            AtomicFile.Replace(temporary, filePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
