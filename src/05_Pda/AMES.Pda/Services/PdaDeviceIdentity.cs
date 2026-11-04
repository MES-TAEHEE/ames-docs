using System.Security.Cryptography;
using System.Text;

namespace AMES.Pda.Services;

public static class PdaDeviceIdentity
{
    public static string Resolve(string? androidId, string identityDirectory)
    {
        var value = androidId?.Trim().ToUpperInvariant();
        if (value is not { Length: 16 } || !value.All(Uri.IsHexDigit)
            || value == "0000000000000000" || value == "9774D56D682E549C")
        {
            Directory.CreateDirectory(identityDirectory);
            var path = Path.Combine(identityDirectory, "pda-installation-id");
            var stored = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            if (!Guid.TryParseExact(stored, "N", out var installationId))
            {
                installationId = Guid.NewGuid();
                File.WriteAllText(path, installationId.ToString("N"));
            }
            value = installationId.ToString("N");
        }

        // Keep the existing API/DB TerminalID limit of 20 ASCII bytes.
        return "PDA-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    }
}
