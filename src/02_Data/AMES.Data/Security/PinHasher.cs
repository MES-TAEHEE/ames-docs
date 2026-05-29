using System.Security.Cryptography;

namespace AMES.Data.Security;

/// <summary>
/// PBKDF2-HMACSHA256 password / PIN hasher that produces output bit-compatible
/// with ASP.NET Core Identity v3 (PasswordHasher&lt;TUser&gt; format marker 0x01),
/// so an operator hashed here can later sign in through any Identity-aware web
/// portal against the same AspNetUsers.PasswordHash without re-issuing.
///
/// Output layout (Base64-encoded as a single string):
///   [0]      0x01                  format marker
///   [1..4]   UInt32 BE             PRF (1 = HMACSHA256)
///   [5..8]   UInt32 BE             iteration count
///   [9..12]  UInt32 BE             salt length (bytes)
///   [13..]   salt                  (16 bytes)
///   [..]     subkey                (32 bytes)
/// </summary>
public static class PinHasher
{
    private const int PrfHmacSha256 = 1;
    private const int Iterations    = 10_000;
    private const int SaltBytes     = 16;
    private const int SubkeyBytes   = 32;

    public static string Hash(string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            password:       pin,
            salt:           salt,
            iterations:     Iterations,
            hashAlgorithm:  HashAlgorithmName.SHA256,
            outputLength:   SubkeyBytes);

        var output = new byte[13 + SaltBytes + SubkeyBytes];
        output[0] = 0x01;
        WriteUInt32BE(output, 1, PrfHmacSha256);
        WriteUInt32BE(output, 5, Iterations);
        WriteUInt32BE(output, 9, SaltBytes);
        Buffer.BlockCopy(salt,   0, output, 13,            SaltBytes);
        Buffer.BlockCopy(subkey, 0, output, 13 + SaltBytes, SubkeyBytes);

        return Convert.ToBase64String(output);
    }

    public static bool Verify(string pin, string? hashedB64)
    {
        if (string.IsNullOrEmpty(hashedB64) || string.IsNullOrEmpty(pin))
            return false;

        byte[] decoded;
        try { decoded = Convert.FromBase64String(hashedB64); }
        catch { return false; }

        if (decoded.Length < 13 || decoded[0] != 0x01) return false;

        var prf       = ReadUInt32BE(decoded, 1);
        var iters     = ReadUInt32BE(decoded, 5);
        var saltLen   = (int)ReadUInt32BE(decoded, 9);
        if (prf != PrfHmacSha256 || iters == 0 || saltLen <= 0) return false;
        if (decoded.Length < 13 + saltLen + 1) return false;

        var salt          = new byte[saltLen];
        Buffer.BlockCopy(decoded, 13, salt, 0, saltLen);
        var expectedKey   = new byte[decoded.Length - 13 - saltLen];
        Buffer.BlockCopy(decoded, 13 + saltLen, expectedKey, 0, expectedKey.Length);

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            password:       pin,
            salt:           salt,
            iterations:     (int)iters,
            hashAlgorithm:  HashAlgorithmName.SHA256,
            outputLength:   expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    private static void WriteUInt32BE(byte[] buf, int offset, uint value)
    {
        buf[offset    ] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)(value);
    }

    private static uint ReadUInt32BE(byte[] buf, int offset) =>
        ((uint)buf[offset]     << 24) |
        ((uint)buf[offset + 1] << 16) |
        ((uint)buf[offset + 2] << 8)  |
         (uint)buf[offset + 3];
}
