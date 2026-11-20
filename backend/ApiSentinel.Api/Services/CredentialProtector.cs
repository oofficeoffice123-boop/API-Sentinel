using Microsoft.AspNetCore.DataProtection;

namespace ApiSentinel.Api.Services;

public sealed class CredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("ApiSentinel.ApiCredentials.v1");
    public string Protect(string credential) => _protector.Protect(credential);
    public string Unprotect(string credential) => _protector.Unprotect(credential);
}