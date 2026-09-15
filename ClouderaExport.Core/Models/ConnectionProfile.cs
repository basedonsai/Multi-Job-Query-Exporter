using System;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClouderaExport.Core.Models;

public class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ConnectionType Type { get; set; } = ConnectionType.ConnectionString;
    public string? DsnName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    [JsonPropertyName("ConnectionString")]
    public string? RawConnectionString { get; set; }

    [JsonIgnore]
    public string ConnectionString
    {
        get => RawConnectionString ?? string.Empty;
        set => RawConnectionString = value;
    }

    public string BuildOdbcConnectionString(bool maskPassword = false)
    {
        if (Type == ConnectionType.Dsn)
        {
            if (string.IsNullOrWhiteSpace(DsnName))
            {
                throw new InvalidOperationException($"Connection profile '{Name}' is missing DSN Name.");
            }

            var builder = new StringBuilder();
            builder.Append($"DSN={DsnName.Trim()};");

            if (!string.IsNullOrWhiteSpace(Username))
            {
                builder.Append($"UID={Username.Trim()};");
            }

            if (!string.IsNullOrWhiteSpace(Password))
            {
                string pwd = maskPassword ? "******" : Password;
                builder.Append($"PWD={pwd};");
            }

            return builder.ToString();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(RawConnectionString))
            {
                throw new InvalidOperationException($"Connection profile '{Name}' is missing Connection String.");
            }

            string raw = RawConnectionString.Trim();
            if (maskPassword)
            {
                raw = Regex.Replace(raw, @"(?i)(PWD\s*=\s*)([^;]*)", "$1******");
            }

            return raw;
        }
    }

    public ConnectionProfile Clone()
    {
        return new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? "New Connection (Copy)" : $"{Name} (Copy)",
            Type = Type,
            DsnName = DsnName,
            Username = Username,
            Password = Password,
            RawConnectionString = RawConnectionString
        };
    }
}
