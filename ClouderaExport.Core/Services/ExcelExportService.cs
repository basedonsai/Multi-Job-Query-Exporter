using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClouderaExport.Core.Models;
using NLog;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace ClouderaExport.Core.Services;

public class ExcelExportService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void Validate(ExportJob job, ConnectionProfile? connection)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));

        if (!job.Enabled)
            throw new InvalidOperationException($"Job '{job.Name}' is currently disabled. Please enable it before running.");

        if (connection == null)
            throw new InvalidOperationException($"Job '{job.Name}' has no connection profile assigned. Please select a valid connection.");

        if (connection.Type == ConnectionType.Dsn)
        {
            if (string.IsNullOrWhiteSpace(connection.DsnName))
                throw new InvalidOperationException($"Connection '{connection.Name}' is in DSN mode, but no DSN Name is specified.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(connection.RawConnectionString))
                throw new InvalidOperationException($"Connection '{connection.Name}' is in Connection String mode, but the connection string is empty.");
        }

        try
        {
            connection.BuildOdbcConnectionString(maskPassword: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Connection '{connection.Name}' configuration error: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(job.OutputFolder))
            throw new InvalidOperationException($"Job '{job.Name}' does not specify an output folder.");

        string folder = job.OutputFolder.Trim();
        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidOperationException($"Job '{job.Name}' output folder path '{folder}' contains invalid path characters.");

        try
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Test folder write permissions
            string testProbeFile = Path.Combine(folder, $".write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testProbeFile, "write_test");
            File.Delete(testProbeFile);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Output folder '{folder}' is not writable. Please check folder permissions.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Output folder '{folder}' cannot be accessed or created: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(job.BaseFileName))
            throw new InvalidOperationException($"Job '{job.Name}' does not specify a base file name.");

        string fileName = job.BaseFileName.Trim();
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"Base file name '{fileName}' contains invalid characters. (Invalid: \\ / : * ? \" < > |)");

        if (job.SplitRows <= 0)
            throw new InvalidOperationException($"Split rows must be greater than 0 (currently configured: {job.SplitRows}).");

        if (string.IsNullOrWhiteSpace(job.QueryText))
            throw new InvalidOperationException($"Job '{job.Name}' has an empty SQL / ODBC query text.");
    }

    public async Task<ExportResult> ExecuteJobAsync(
        ExportJob job,
        ConnectionProfile connection,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ExportResult { JobName = job.Name };

        Validate(job, connection);

        string outputFolder = job.OutputFolder.Trim();
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
            logCallback?.Invoke($"Created output directory: {outputFolder}");
            Logger.Info("Created output directory: {0}", outputFolder);
        }

        string trimmedQuery = job.QueryText.TrimEnd();

        string connectionString = connection.BuildOdbcConnectionString(maskPassword: false);
        string maskedConnectionString = connection.BuildOdbcConnectionString(maskPassword: true);

        logCallback?.Invoke($"Connecting to ODBC data source for job '{job.Name}' ({maskedConnectionString})...");
        Logger.Info("Job '{0}': Connecting using profile '{1}' ({2})", job.Name, connection.Name, maskedConnectionString);

        using var odbcConn = new OdbcConnection(connectionString);
        odbcConn.ConnectionTimeout = 30;
        await odbcConn.OpenAsync(cancellationToken);

        using var command = new OdbcCommand(trimmedQuery, odbcConn);
        command.CommandTimeout = 0; // Infinite timeout for long-running export queries

        logCallback?.Invoke($"Executing query: {trimmedQuery}");
        Logger.Info("Job '{0}': Executing query", job.Name);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        int columnCount = reader.FieldCount;
        if (columnCount == 0)
        {
            throw new InvalidOperationException("The query returned no columns.");
        }

        string[] columnNames = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        int partNumber = 1;
        long totalRows = 0;
        int currentChunkRows = 0;

        XSSFWorkbook? currentWb = null;
        ISheet? currentSheet = null;
        ICellStyle? headerStyle = null;

        void StartNewWorkbook()
        {
            currentWb = new XSSFWorkbook();
            currentSheet = currentWb.CreateSheet("ExportData");

            var font = currentWb.CreateFont();
            font.IsBold = true;
            headerStyle = currentWb.CreateCellStyle();
            headerStyle.SetFont(font);

            var headerRow = currentSheet.CreateRow(0);
            for (int col = 0; col < columnCount; col++)
            {
                var cell = headerRow.CreateCell(col);
                cell.SetCellValue(columnNames[col]);
                cell.CellStyle = headerStyle;
            }

            // Freeze pane first row
            currentSheet.CreateFreezePane(0, 1);
            currentChunkRows = 0;
        }

        void SaveCurrentWorkbook()
        {
            if (currentWb == null || currentSheet == null) return;

            // Apply AutoFilter on header row
            if (columnCount > 0)
            {
                int filterEndRow = Math.Max(currentChunkRows, 1);
                currentSheet.SetAutoFilter(new CellRangeAddress(0, filterEndRow, 0, columnCount - 1));
            }

            string fileName = $"{job.BaseFileName.Trim()}_part{partNumber:D3}.xlsx";
            string filePath = Path.Combine(outputFolder, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                currentWb.Write(fs);
            }

            currentWb.Close();
            currentWb = null;
            currentSheet = null;

            result.GeneratedFiles.Add(filePath);
            string partMsg = $"Generated part {partNumber:D3}: {filePath} ({currentChunkRows:N0} rows)";
            logCallback?.Invoke(partMsg);
            Logger.Info(partMsg);

            partNumber++;
        }

        try
        {
            StartNewWorkbook();

            while (await reader.ReadAsync(cancellationToken))
            {
                currentChunkRows++;
                totalRows++;

                var row = currentSheet!.CreateRow(currentChunkRows);
                for (int col = 0; col < columnCount; col++)
                {
                    if (reader.IsDBNull(col)) continue;

                    var cell = row.CreateCell(col);
                    object val = reader.GetValue(col);

                    switch (val)
                    {
                        case bool b:
                            cell.SetCellValue(b);
                            break;
                        case sbyte or byte or short or ushort or int or uint or long or ulong:
                            cell.SetCellValue(Convert.ToDouble(val));
                            break;
                        case float or double or decimal:
                            cell.SetCellValue(Convert.ToDouble(val));
                            break;
                        case DateTime dt:
                            cell.SetCellValue(dt.ToString("yyyy-MM-dd HH:mm:ss"));
                            break;
                        case DateOnly d:
                            cell.SetCellValue(d.ToString("yyyy-MM-dd"));
                            break;
                        case TimeOnly t:
                            cell.SetCellValue(t.ToString("HH:mm:ss"));
                            break;
                        default:
                            cell.SetCellValue(val.ToString());
                            break;
                    }
                }

                if (currentChunkRows >= job.SplitRows)
                {
                    SaveCurrentWorkbook();
                    StartNewWorkbook();
                }
            }

            // Save final workbook if it contains rows, or if total rows was 0 (part001 with header)
            if (currentChunkRows > 0 || (totalRows == 0 && partNumber == 1))
            {
                SaveCurrentWorkbook();
            }
            else if (currentWb != null)
            {
                currentWb.Close();
                currentWb = null;
            }

            stopwatch.Stop();
            result.Success = true;
            result.TotalRows = totalRows;
            result.Elapsed = stopwatch.Elapsed;

            string summary = $"Export completed successfully for '{job.Name}': {totalRows:N0} total rows across {result.GeneratedFiles.Count} file(s) in {stopwatch.Elapsed.TotalSeconds:F2}s.";
            logCallback?.Invoke(summary);
            Logger.Info(summary);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.TotalRows = totalRows;
            result.Elapsed = stopwatch.Elapsed;
            result.ErrorMessage = ex.Message;

            if (currentWb != null)
            {
                try { currentWb.Close(); } catch { }
            }

            string err = $"Export FAILED for '{job.Name}': {ex.Message}";
            logCallback?.Invoke(err);
            Logger.Error(ex, err);
            throw;
        }
    }
}
