using System.Text.Json;
using Igres.Core.Models;
using Igres.Core.Services;

namespace Igres.Infrastructure.Storage;

public sealed class UserPreferenceService : IUserPreferenceService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private UserPreference _current = UserPreference.Default;

    public UserPreference Current => _current;
    public event Action<UserPreference>? PreferenceChanged;

    public UserPreferenceService()
    {
        _filePath = AppDataPaths.PreferencesFile;
    }

    public async Task<UserPreference> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            UpdateCurrent(UserPreference.Default);
            return _current;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var preference = await JsonSerializer.DeserializeAsync<UserPreference>(stream, cancellationToken: cancellationToken);
            UpdateCurrent(preference ?? UserPreference.Default);
            return _current;
        }
        catch
        {
            UpdateCurrent(UserPreference.Default);
            return _current;
        }
    }

    public async Task SaveAsync(UserPreference preference, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(preference, WriteOptions);
        await File.WriteAllBytesAsync(_filePath, json, cancellationToken);
        UpdateCurrent(preference);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                File.Delete(_filePath);
            }
            catch
            {
                // Best-effort local cleanup.
            }
        }

        UpdateCurrent(UserPreference.Default);
        return Task.CompletedTask;
    }

    private void UpdateCurrent(UserPreference preference)
    {
        _current = preference;
        PreferenceChanged?.Invoke(preference);
    }
}
