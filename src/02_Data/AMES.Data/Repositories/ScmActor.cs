using System.Security.Cryptography;
using System.Text;

namespace AMES.Data.Repositories;

public sealed partial class ScmRepository
{
    // Same stable portal actor as the ames:actor claim, never a truncated email.
    static string PortalActor(string userId)
        => "P" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.Trim().ToLowerInvariant())))[..16];
}
