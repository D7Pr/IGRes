namespace Igres.Core.Models;

/// <summary>
/// Headers lifted from a captured mobile request. Stored only in the user's OS keychain
/// and never logged. The app replays these on outbound requests against Instagram's
/// private API.
/// </summary>
public sealed record CapturedHeaders(
    string Authorization,
    string UserAgent,
    string XIgAppId,
    string XIgDeviceId,
    string XIgFamilyDeviceId,
    string XMid,
    string IgUDsUserId,
    string IgIntendedUserId,
    string IgURur,
    string XBloksVersionId,
    string AcceptLanguage,
    string XIgAppLocale,
    string XIgDeviceLocale,
    string XIgMappedLocale,
    DateTimeOffset CapturedAt)
{
    /// <summary>Short masked summary safe to display in UI. Never includes the bearer.</summary>
    public string DisplaySummary
    {
        get
        {
            var user = IgUDsUserId.Length > 6 ? IgUDsUserId[..6] + "***" : "***";
            return $"ds_user_id: {user} | device: {ShortDevice(XIgDeviceId)} | captured: {CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    private static string ShortDevice(string d) => d.Length >= 8 ? d[..8] : d;
}
