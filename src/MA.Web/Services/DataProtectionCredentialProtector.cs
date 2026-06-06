using MA.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace MA.Web.Services;

public class DataProtectionCredentialProtector : ICredentialProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionCredentialProtector(IDataProtectionProvider provider)
    {
        // Purpose string is part of the key derivation - changing it invalidates existing ciphertexts.
        _protector = provider.CreateProtector("MA.MarketplaceCredentials.v1");
    }

    public string Encrypt(string plaintext) =>
        string.IsNullOrEmpty(plaintext) ? "" : _protector.Protect(plaintext);

    public string Decrypt(string ciphertext) =>
        string.IsNullOrEmpty(ciphertext) ? "" : _protector.Unprotect(ciphertext);
}
