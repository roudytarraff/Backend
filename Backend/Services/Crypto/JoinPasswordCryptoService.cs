using System;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Services.Crypto;

public sealed class JoinPasswordCryptoService : IJoinPasswordCryptoService
{
    private readonly string _keyString;

    public JoinPasswordCryptoService(IConfiguration configuration)
    {
        _keyString = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is missing.");
    }

    public string Encrypt(string plainText)
    {
        var key = Convert.FromBase64String(_keyString);
        if (key.Length != 32)
            throw new InvalidOperationException("Encryption:Key must be a base64 32-byte key.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var payload = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string encryptedText)
    {
        var key = Convert.FromBase64String(_keyString);
        if (key.Length != 32)
            throw new InvalidOperationException("Encryption:Key must be a base64 32-byte key.");

        var payload = Convert.FromBase64String(encryptedText);

        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[16];
        var cipherBytes = new byte[payload.Length - 16];

        Buffer.BlockCopy(payload, 0, iv, 0, 16);
        Buffer.BlockCopy(payload, 16, cipherBytes, 0, cipherBytes.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}