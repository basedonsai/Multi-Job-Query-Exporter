using System;
using ClouderaExport.Core.Models;

namespace ClouderaExport.Ui.ViewModels;

public class JobItemViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private Guid _id;
    private string _name = "New Job";
    private bool _enabled = true;
    private Guid? _connectionId;
    private string _outputFolder = string.Empty;
    private string _baseFileName = "Export";
    private ExportFormat _format = ExportFormat.Xlsx;
    private int _splitRows = 2000;
    private string _queryText = string.Empty;

    public JobItemViewModel(ExportJob job, Action onChanged)
    {
        _onChanged = onChanged;
        _id = job.Id;
        _name = job.Name;
        _enabled = job.Enabled;
        _connectionId = job.ConnectionId;
        _outputFolder = job.OutputFolder;
        _baseFileName = job.BaseFileName;
        _format = job.Format;
        _splitRows = job.SplitRows <= 0 ? 2000 : job.SplitRows;
        _queryText = job.QueryText;
    }

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public Guid? ConnectionId
    {
        get => _connectionId;
        set
        {
            if (SetField(ref _connectionId, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (SetField(ref _outputFolder, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public string BaseFileName
    {
        get => _baseFileName;
        set
        {
            if (SetField(ref _baseFileName, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public ExportFormat Format
    {
        get => _format;
        set
        {
            if (SetField(ref _format, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public int SplitRows
    {
        get => _splitRows;
        set
        {
            if (SetField(ref _splitRows, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetField(ref _queryText, value))
            {
                _onChanged?.Invoke();
            }
        }
    }

    public ExportJob ToModel()
    {
        return new ExportJob
        {
            Id = Id,
            Name = Name,
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
