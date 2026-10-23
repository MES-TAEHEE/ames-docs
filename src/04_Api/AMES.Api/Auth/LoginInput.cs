using System.Text;

namespace AMES.Api.Auth;

public static class LoginInput
{
    static readonly Encoding Encoding = LoadEncoding();
    static Encoding LoadEncoding()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    public static bool Valid(string? value, int bytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) return false;
        try { return Encoding.GetByteCount(value) <= bytes && !value.Any(char.IsControl); }
        catch (EncoderFallbackException) { return false; }
    }

    public static bool ValidContext(string? terminal, string? line, string? shift)
        => Valid(terminal, 20)
           && (string.IsNullOrEmpty(line) || Valid(line, 20))
           && (string.IsNullOrEmpty(shift) || Valid(shift, 10));
}
