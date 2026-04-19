using CommunityToolkit.Mvvm.ComponentModel;
using Igres.Core.Services;

namespace Igres.Desktop.Services;

public sealed partial class ConfirmationDialogViewModel : ObservableObject
{
    public ConfirmationRequest Request { get; }
    public bool RequiresTypedConfirmation => !string.IsNullOrEmpty(Request.TypedConfirmationText);

    [ObservableProperty]
    private string _typedValue = string.Empty;

    public ConfirmationDialogViewModel(ConfirmationRequest request)
    {
        Request = request;
    }

    public bool CanConfirm =>
        !RequiresTypedConfirmation ||
        string.Equals(TypedValue?.Trim(), Request.TypedConfirmationText, StringComparison.Ordinal);

    partial void OnTypedValueChanged(string value) => OnPropertyChanged(nameof(CanConfirm));
}
