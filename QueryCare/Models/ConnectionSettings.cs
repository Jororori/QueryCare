using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace QueryCare.Models;

public sealed class ConnectionSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Server { get; set; } = "localhost";
    public string Database { get; set; } = "";
    public bool UseWindowsAuth { get; set; } = true;
    public string User { get; set; } = "";
    public bool TrustServerCertificate { get; set; } = true;

    public string? ProtectedPassword { get; set; }

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedPassword)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => ProtectedPassword = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
    public bool IsSameTarget(ConnectionSettings other) =>
        string.Equals(Server, other.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Database, other.Database, StringComparison.OrdinalIgnoreCase);

    public string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            IntegratedSecurity = UseWindowsAuth,
            TrustServerCertificate = TrustServerCertificate,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ApplicationName = "QueryCare (solo lectura)",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 10,
            Pooling = true
        };
        if (!UseWindowsAuth)
        {
            b.UserID = User;
            b.Password = Password;
        }
        return b.ConnectionString;
    }
}

public static class ConnectionStore
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QueryCare");
    private static string FilePath => Path.Combine(Folder, "connections.json");
    private static string LegacyFilePath => Path.Combine(Folder, "connection.json");

    public static List<ConnectionSettings> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<ConnectionSettings>>(File.ReadAllText(FilePath)) ?? new();
            if (File.Exists(LegacyFilePath) && JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(LegacyFilePath)) is { } single
                && !string.IsNullOrEmpty(single.Database))
                return new() { single };
        }
        catch { }
        return new();
    }

    public static void Save(IEnumerable<ConnectionSettings> connections)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(connections.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

