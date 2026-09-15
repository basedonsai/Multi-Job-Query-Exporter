using System.Collections.Generic;

namespace ClouderaExport.Core.Models;

public class AppState
{
    public UiSettings Settings { get; set; } = new();
    public List<ConnectionProfile> Connections { get; set; } = new();
    public List<ExportJob> Jobs { get; set; } = new();
}
