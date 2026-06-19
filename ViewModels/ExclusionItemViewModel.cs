using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarpManager.Models;

namespace WarpManager.ViewModels;

public partial class ExclusionItemViewModel : ObservableObject
{
    public string        Value  { get; }
    public ExclusionType Type   { get; }

    private readonly MainViewModel _parent;

    public ExclusionItemViewModel(string value, ExclusionType type, MainViewModel parent)
    {
        Value   = value;
        Type    = type;
        _parent = parent;
    }

    [RelayCommand]
    Task RemoveAsync() => _parent.RemoveItemAsync(this);
}
