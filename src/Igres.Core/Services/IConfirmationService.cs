namespace Igres.Core.Services;

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel = "Cancel",
    bool IsDestructive = true,
    string? TypedConfirmationText = null,
    int AffectedCount = 0);

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default);
}
