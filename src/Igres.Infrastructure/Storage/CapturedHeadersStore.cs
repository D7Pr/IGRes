using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Igres.Core.Models;
using Igres.Core.Storage;

namespace Igres.Infrastructure.Storage;

public sealed class CapturedHeadersStore : ICapturedHeadersStore
{
    private readonly string _filePath;
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes("Igres.CapturedHeadersStore.v1");

    public CapturedHeadersStore()
    {
        _filePath = AppDataPaths.CapturedHeadersFile;
    }

    public async Task SaveAsync(CapturedHeaders headers, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(headers);
        var payload = Protect(json);
        await File.WriteAllBytesAsync(_filePath, payload, cancellationToken);
    }

    public async Task<CapturedHeaders?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            var payload = await File.ReadAllBytesAsync(_filePath, cancellationToken);
            var json = Unprotect(payload);
            return JsonSerializer.Deserialize<CapturedHeaders>(json);
        }
        catch
        {
            return null;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_filePath))
        {
            try { File.Delete(_filePath); } catch { /* ignored */ }
        }
        return Task.CompletedTask;
    }

    private byte[] Protect(byte[] plain)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);
        return FallbackProtect(plain);
    }

    private byte[] Unprotect(byte[] payload)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(payload, _entropy, DataProtectionScope.CurrentUser);
        return FallbackUnprotect(payload);
    }

    private byte[] FallbackProtect(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var ct = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        var result = new byte[aes.IV.Length + ct.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ct, 0, result, aes.IV.Length, ct.Length);
        return result;
    }

    private byte[] FallbackUnprotect(byte[] payload)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        var iv = new byte[16];
        Buffer.BlockCopy(payload, 0, iv, 0, iv.Length);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(payload, iv.Length, payload.Length - iv.Length);
    }

    private byte[] DeriveKey()
    {
        var user = Environment.UserName ?? "igres";
        var machine = Environment.MachineName ?? "host";
        var material = Encoding.UTF8.GetBytes($"{user}|{machine}|igres-captured-key-v1");
        return Rfc2898DeriveBytes.Pbkdf2(material, _entropy, 100_000, HashAlgorithmName.SHA256, 32);
    }
}
