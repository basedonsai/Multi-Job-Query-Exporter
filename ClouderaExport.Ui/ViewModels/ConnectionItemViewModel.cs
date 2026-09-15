using System;
using ClouderaExport.Core.Models;

namespace ClouderaExport.Ui.ViewModels;

public class ConnectionItemViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private Guid _id;
    private string _name = string.Empty;
    private ConnectionType _type = ConnectionType.ConnectionString;
    private string _dsnName = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _rawConnectionString = string.Empty;

    public ConnectionItemViewModel(ConnectionProfile profile, Action onChanged)
    {
        _onChanged = onChanged;
        _id = profile.Id;
        _name = profile.Name;
        _type = profile.Type;
        _dsnName = profile.DsnName ?? string.Empty;
        _username = profile.Username ?? string.Empty;
        _password = profile.Password ?? string.Empty;
        _rawConnectionString = profile.RawConnectionString ?? profile.ConnectionString ?? string.Empty;
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
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public ConnectionType Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value))
            {
                OnPropertyChanged(nameof(IsDsn));
                OnPropertyChanged(nameof(IsConnectionString));
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public bool IsDsn => Type == ConnectionType.Dsn;
    public bool IsConnectionString => Type == ConnectionType.ConnectionString;

    public string DsnName
    {
        get => _dsnName;
        set
        {
            if (SetField(ref _dsnName, value))
            {
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetField(ref _username, value))
            {
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value))
            {
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public string RawConnectionString
    {
        get => _rawConnectionString;
        set
        {
            if (SetField(ref _rawConnectionString, value))
            {
                OnPropertyChanged(nameof(ConnectionString));
                OnPropertyChanged(nameof(EffectiveConnectionString));
                _onChanged?.Invoke();
            }
        }
    }

    public string ConnectionString
    {
        get => RawConnectionString;
        set => RawConnectionString = value;
    }

    public string EffectiveConnectionString
    {
        get
        {
            try
            {
                var profile = ToModel();
                return profile.BuildOdbcConnectionString(maskPassword: true);
            }
            catch (Exception ex)
            {
                return $"[Invalid Configuration: {ex.Message}]";
            }
        }
    }

    public ConnectionProfile ToModel()
    {
        return new ConnectionProfile
        {
            Id = Id,
            Name = Name,
            Type = Type,
            DsnName = DsnName,
            Username = Username,
            Password = Password,
            RawConnectionString = RawConnectionString
        };
    }
}
