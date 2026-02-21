using System.Security.Cryptography;
using System.Text;

namespace Vigma.TimbradoGateway.Utils;

public static class ApiKeyGenerator
{
    public static string GenerateLiveKey()
    {
        Span<byte> b = stackalloc byte[32];
        RandomNumberGenerator.Fill(b);
        var hex = Convert.ToHexString(b).ToLowerInvariant();
        return $"tg_live_{hex}";
    }

    public static string GenerateTestKey()
    {
        Span<byte> b = stackalloc byte[32];
        RandomNumberGenerator.Fill(b);
        var hex = Convert.ToHexString(b).ToLowerInvariant();
        return $"tg_test_{hex}";
    }

    // Hash SHA256 en hex (64 chars) para guardar en BD
    public static string Hash(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "";
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Last4(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        return key.Length <= 4 ? key : key[^4..];
    }

    public static string Mask(string key)
    {
        var last4 = Last4(key);
        // Mantén el prefijo tg_live_ aunque sea test si quieres, o mejora:
        var prefix = key.StartsWith("tg_test_") ? "tg_test_" : "tg_live_";
        return $"{prefix}************************{last4}";
    }
}
