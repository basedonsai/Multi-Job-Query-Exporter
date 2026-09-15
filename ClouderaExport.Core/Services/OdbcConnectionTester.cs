using System;
using System.Data.Odbc;
using System.Text.RegularExpressions;
using ClouderaExport.Core.Models;
using NLog;

namespace ClouderaExport.Core.Services;

public static class OdbcConnectionTester
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static (bool Success, string Message) TestConnection(ConnectionProfile profile)
    {
        if (profile == null)
        {
            return (false, "Connection profile cannot be null.");
        }

        string actualConnStr;
        string maskedConnStr;

        try
        {
            actualConnStr = profile.BuildOdbcConnectionString(maskPassword: false);
            maskedConnStr = profile.BuildOdbcConnectionString(maskPassword: true);
        }
        catch (Exception ex)
        {
            string validationMsg = $"Configuration Error: {ex.Message}";
            Logger.Warn(validationMsg);
            return (false, validationMsg);
        }

        return TestConnectionInternal(actualConnStr, maskedConnStr);
    }

    public static (bool Success, string Message) TestConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return (false, "Connection string cannot be empty.");
        }

        string masked = Regex.Replace(connectionString, @"(?i)(PWD\s*=\s*)([^;]*)", "$1******");
        return TestConnectionInternal(connectionString, masked);
    }

    private static (bool Success, string Message) TestConnectionInternal(string connectionString, string maskedConnectionString)
    {
        try
        {
            Logger.Info("Testing ODBC connection: {0}", maskedConnectionString);
            using var connection = new OdbcConnection(connectionString);
            connection.ConnectionTimeout = 10;
            connection.Open();

            string serverVersion = "Unknown";
            try
            {
                serverVersion = connection.ServerVersion;
            }
            catch
            {
                // Some ODBC drivers do not implement the ServerVersion property
            }

            connection.Close();
            Logger.Info("ODBC connection successful (Server Version: {0})", serverVersion);
            return (true, $"Success: Connected successfully. Server Version: {serverVersion}");
        }
        catch (OdbcException ex)
        {
            string detail = $"ODBC Error [{ex.ErrorCode}]: {ex.Message}";
            Logger.Error(ex, "ODBC test failed for {0}: {1}", maskedConnectionString, detail);
            return (false, detail);
        }
        catch (Exception ex)
        {
            string detail = $"Connection Failed: {ex.Message}";
            Logger.Error(ex, "Connection test failed for {0}: {1}", maskedConnectionString, detail);
            return (false, detail);
        }
    }
}
