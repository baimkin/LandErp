using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.ParserSpike.ServerIntegration;

namespace LandErp.ParserSpike.Desktop;

/// <summary>The key is protected by Windows for the current user, never stored as plain text.</summary>
internal static class ServerConnectionSettings
{
    private sealed record StoredConnection(string Address, Guid AgentId, string ProtectedKey);
    internal static ServerConnection? Load(string path)
    {
        if (!File.Exists(path))
        {
            try { return ServerConnection.FromEnvironment(); }
            catch (InvalidOperationException) { return null; }
        }
        try
        {
            StoredConnection stored = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(path))
                ?? throw new InvalidOperationException();
            Uri address = new(stored.Address);
            if (address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length != 0 || address.AbsolutePath != "/"
                || address.Query.Length != 0 || address.Fragment.Length != 0 || stored.AgentId == Guid.Empty)
                return null;
            byte[] clear = ProtectedData.Unprotect(Convert.FromBase64String(stored.ProtectedKey), null, DataProtectionScope.CurrentUser);
            try
            {
                string key = Encoding.UTF8.GetString(clear);
                return key.Length == 64 && key.All(char.IsAsciiHexDigit) ? new(address, stored.AgentId, key) : null;
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception) { return null; } // Damaged settings prompt re-entry; no secret/provider details are displayed.
    }
    internal static void Save(string path, ServerConnection connection)
    {
        byte[] clear = Encoding.UTF8.GetBytes(connection.Token);
        try
        {
            byte[] protectedKey = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            string json = JsonSerializer.Serialize(new StoredConnection(connection.Origin.AbsoluteUri, connection.AgentId, Convert.ToBase64String(protectedKey)));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}
