using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Igres.Core.Auth;
using Igres.Core.Models;

namespace Igres.Desktop.ViewModels.Auth;

public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly IAuthCoordinator _auth;

    public event EventHandler<VerificationChallenge>? ChallengeIssued;
    public event EventHandler<AccountSession>? SignedIn;

    [ObservableProperty] private string _identifier = string.Empty;
    [ObservableProperty] private string _secret = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSigningIn;

    public string HelperHint =>
        "Live Instagram sign-in. Your password is encrypted with Instagram's public key before it leaves the app, and the session bearer is stored only in your OS keychain. " +
        "If you have 2FA on, you'll be prompted for a code on the next screen. Using this on a new device can trigger a checkpoint — open the official app first if that happens.";

    public WelcomeViewModel(IAuthCoordinator auth)
    {
        _auth = auth;
    }

    public void Reset()
    {
        ErrorMessage = null;
        IsSigningIn = false;
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsSigningIn) return;
        if (string.IsNullOrWhiteSpace(Identifier))
        {
            await RunOnUiThreadAsync(() => ErrorMessage = "Enter an identifier to continue.");
            return;
        }
        try
        {
            await RunOnUiThreadAsync(() =>
            {
                IsSigningIn = true;
                ErrorMessage = null;
            });

            var result = await _auth.StartSignInAsync(new AuthStartRequest(Identifier.Trim(), Secret), CancellationToken.None);

            await RunOnUiThreadAsync(() =>
            {
                switch (result.Outcome)
                {
                    case AuthOutcome.SignedIn when result.Session is not null:
                        SignedIn?.Invoke(this, result.Session);
                        break;
                    case AuthOutcome.ChallengeRequired when result.Challenge is not null:
                        ChallengeIssued?.Invoke(this, result.Challenge);
                        break;
                    default:
                        ErrorMessage = result.ErrorMessage ?? "Sign-in could not be started.";
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
            await RunOnUiThreadAsync(() => IsSigningIn = false);
        }
    }
}
