using System;

namespace Backend.Services.Crypto;

public interface IJoinPasswordCryptoService
{
    string Encrypt(string plainText);
    string Decrypt(string encryptedText);
}
