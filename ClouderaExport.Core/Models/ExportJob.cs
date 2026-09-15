using System;

namespace ClouderaExport.Core.Models;

public class ExportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Job";
    public bool Enabled { get; set; } = true;
    public Guid? ConnectionId { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public string BaseFileName { get; set; } = "Export";
    public ExportFormat Format { get; set; } = ExportFormat.Xlsx;
    public int SplitRows { get; set; } = 2000;
    public string QueryText { get; set; } = string.Empty;

    public ExportJob Clone()
    {
        return new ExportJob
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? "New Job (Copy)" : $"{Name} (Copy)",
            Enabled = Enabled,
            ConnectionId = ConnectionId,
            OutputFolder = OutputFolder,
            BaseFileName = BaseFileName,
            Format = Format,
            SplitRows = SplitRows,
            QueryText = QueryText
        };
    }
}
