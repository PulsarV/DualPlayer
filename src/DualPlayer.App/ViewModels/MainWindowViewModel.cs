using CommunityToolkit.Mvvm.ComponentModel;

namespace DualPlayer.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "DualPlayer";
}
