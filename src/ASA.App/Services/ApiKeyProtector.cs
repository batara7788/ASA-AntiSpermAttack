using System.Security.Cryptography;
using System.Text;

namespace Asa.App.Services;

public static class ApiKeyProtector
{
    public static string Protect(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            return string.Empty;
        }

        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainKey.Trim()), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string protectedKey)
    {
        if (string.IsNullOrEmpty(protectedKey))
        {
            return string.Empty;
        }

        try
        {
            byte[] decrypted = ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
