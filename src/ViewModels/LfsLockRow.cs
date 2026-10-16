using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sextant.ViewModels;

public partial class LfsLockRow : ObservableObject
{
    public bool IsHeader { get; init; }

    public bool IsLock => !IsHeader;

    public bool IsMine { get; init; }

    public string Id { get; init; } = "";

    public string Path { get; init; } = "";

    public string Owner { get; init; } = "";

    public string Label { get; init; } = "";

    public bool ShowCheck { get; init; }

    public bool ShowUnlock { get; init; }

    public bool ShowForceUnlock { get; init; }

    public IRelayCommand? ToggleChecksCommand { get; init; }

    public IAsyncRelayCommand? UnlockCommand { get; init; }

    public IAsyncRelayCommand? UnlockAllCommand { get; init; }

    public Action? CheckChanged { get; set; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool? HeaderChecked { get; set; }

    [ObservableProperty]
    public partial bool ShowUnlockAll { get; set; }

    partial void OnIsCheckedChanged(bool value) => CheckChanged?.Invoke();
}
