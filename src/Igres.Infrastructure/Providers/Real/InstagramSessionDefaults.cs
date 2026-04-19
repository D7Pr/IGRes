namespace Igres.Infrastructure.Providers.Real;

internal static class InstagramSessionDefaults
{
    internal const string FallbackBloksVersionId =
        "c372d954187eba9fc31a971f5970403065184575585fcc820a91d7d4fddcba5c";

    internal static string ResolveBloksVersionId(string? capturedValue) =>
        string.IsNullOrWhiteSpace(capturedValue) ? FallbackBloksVersionId : capturedValue;
}
