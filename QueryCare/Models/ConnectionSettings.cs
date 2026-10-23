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
}

