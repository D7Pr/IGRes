using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.Auth;
using Igres.Core.Models;

namespace Igres.Desktop.ViewModels.Auth;

public sealed partial class VerificationCodeViewModel : ViewModelBase
{
    private readonly IAuthCoordinator _auth;
    private VerificationChallenge? _challenge;

    public event EventHandler<AccountSession>? SignedIn;
    public event EventHandler? Restarted;

    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string? _destinationHint;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _canResend;
    [ObservableProperty] private bool _canRestart;

    public VerificationCodeViewModel(IAuthCoordinator auth)
    {
        _auth = auth;
    }

    public void LoadChallenge(VerificationChallenge challenge)
    {
        _challenge = challenge;
        Code = string.Empty;
        DestinationHint = challenge.DestinationHint;
        ErrorMessage = challenge.ErrorMessage;
        CanResend = challenge.CanResend;
        CanRestart = challenge.CanRestart;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (_challenge is null) return;
        if (string.IsNullOrWhiteSpace(Code))
        {
            await RunOnUiThreadAsync(() => ErrorMessage = "Enter the verification code to continue.");
            return;
        }
        try
        {
            await RunOnUiThreadAsync(() =>
            {
                IsSubmitting = true;
                ErrorMessage = null;
            });

            var result = await _auth.SubmitVerificationCodeAsync(_challenge.ChallengeId, Code.Trim(), CancellationToken.None);

            await RunOnUiThreadAsync(() =>
            {
                switch (result.Outcome)
                {
                    case AuthOutcome.SignedIn when result.Session is not null:
                        SignedIn?.Invoke(this, result.Session);
                        break;
                    case AuthOutcome.ChallengeRequired when result.Challenge is not null:
                        LoadChallenge(result.Challenge);
                        break;
                    default:
                        ErrorMessage = result.ErrorMessage ?? "Verification failed.";
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() => ErrorMessage = ex.Message);
        }
        finally
        {
            await RunOnUiThreadAsync(() => IsSubmitting = false);
        }
    }

    [RelayCommand]
    private void Restart() => Restarted?.Invoke(this, EventArgs.Empty);
}
