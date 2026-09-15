using System;
using System.Collections.Generic;

namespace ClouderaExport.Core.Models;

public class ExportResult
{
    public bool Success { get; set; }
    public string JobName { get; set; } = string.Empty;
    public long TotalRows { get; set; }
    public List<string> GeneratedFiles { get; set; } = new();
    public TimeSpan Elapsed { get; set; }
    public string? ErrorMessage { get; set; }
}
