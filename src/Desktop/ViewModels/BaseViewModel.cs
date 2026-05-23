using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Desktop.ViewModels;

public partial class BaseViewModel : ObservableValidator
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string? successMessage;

    protected void SetError(string message)
    {
        ErrorMessage = message;
        HasError = !string.IsNullOrEmpty(message);
    }

    protected void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    [RelayCommand]
    private void ClearErrorDisplay()
    {
        ClearError();
    }
}