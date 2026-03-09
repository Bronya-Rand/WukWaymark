using System;
using System.Text;
using System.Text.Json;

namespace WukWaymark.Helpers;

/// <summary>
/// Helper class for obfuscating and deobfuscating data using a simple XOR cipher.
/// </summary>
public static class ObfuscationHelper
{
    // Simple XOR key for obfuscation (not meant to be secure, just obscure)
    private static readonly byte[] XorKey = "WukWaymark2024PrivacyKey"u8.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = true
    };

    /// <summary>
    /// Obfuscates an object by serializing it to JSON, applying XOR cipher, and encoding to Base64.
    /// </summary>
    /// <typeparam name="T">The type of object to obfuscate</typeparam>
    /// <param name="data">The object to obfuscate</param>
    /// <returns>Base64-encoded obfuscated string</returns>
    public static string Obfuscate<T>(T data)
    {
        // Serialize to JSON
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Apply XOR cipher using Span<T> to reduce allocations
        var keySpan = XorKey.AsSpan();
        var keyLength = keySpan.Length;

        // Use stackalloc for small payloads, heap for large ones
        var obfuscated = jsonBytes.Length <= 2048
            ? stackalloc byte[jsonBytes.Length]
            : new byte[jsonBytes.Length];

        for (var i = 0; i < jsonBytes.Length; i++)
        {
            obfuscated[i] = (byte)(jsonBytes[i] ^ keySpan[i % keyLength]);
        }

        // Encode to Base64
        return Convert.ToBase64String(obfuscated);
    }

    /// <summary>
    /// Deobfuscates a Base64-encoded string back to the original object.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize to</typeparam>
    /// <param name="base64Data">The Base64-encoded obfuscated string</param>
    /// <returns>The deobfuscated object</returns>
    /// <exception cref="JsonException">Thrown if deserialization fails</exception>
    public static T? Deobfuscate<T>(string base64Data)
    {
        // Decode from Base64
        var obfuscated = Convert.FromBase64String(base64Data);

        // Reverse XOR cipher
        var keySpan = XorKey.AsSpan();
        var keyLength = keySpan.Length;

        var jsonBytes = obfuscated.Length <= 2048
            ? stackalloc byte[obfuscated.Length]
            : new byte[obfuscated.Length];

        for (var i = 0; i < obfuscated.Length; i++)
        {
            jsonBytes[i] = (byte)(obfuscated[i] ^ keySpan[i % keyLength]);
        }

        // Deserialize from JSON
        var json = Encoding.UTF8.GetString(jsonBytes);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
