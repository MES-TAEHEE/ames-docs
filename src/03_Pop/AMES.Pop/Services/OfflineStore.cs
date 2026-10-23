using System.Security.Cryptography;
using System.Text.Json;

namespace AMES.Pop.Services;

/// <summary>
/// 오프라인 로그인 캐시·저널을 담는 암호화 파일. Windows DPAPI(현재 사용자 범위)로 묶어서
/// 다른 PC 로 복사하거나 다른 Windows 계정으로 열어서는 읽을 수 없다 — PIN 해시가 들어 있기 때문이다.
/// </summary>
internal static class OfflineStore
{
    private static readonly byte[] Entropy = "AMES.Pop.Offline.v1"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private static string? _dir;

    // 설정 PopTerminal:Offline:Dir(환경변수 %VAR% 가능, 상대 경로는 실행 폴더 기준) — 비어 있거나 설정 파일을 못 읽으면(테스트) 기본 폴더.
    public static string Dir
    {
        get => _dir ??= FromConfig();
        set => _dir = value;
    }

    private static string FromConfig()
    {
        try
        {
            var configured = AMES.Pop.Common.AppConfig.Current.OfflineDir?.Trim();
            if (!string.IsNullOrEmpty(configured))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured), AppContext.BaseDirectory);
        }
        catch { /* 기본 폴더로 */ }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMES", "Pop");
    }

    public static string PathOf(string name) => Path.Combine(Dir, name);

    /// <summary>파일이 없거나 읽을 수 없으면(손상·다른 사용자 암호화) null — 호출자가 빈 상태로 시작한다.</summary>
    public static T? Load<T>(string name) where T : class
    {
        try
        {
            var path = PathOf(name);
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain, Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            PopLogSafe($"load {name} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>임시 파일에 쓰고 바꿔치기한다 — 쓰는 도중 꺼져도 이전 파일이 남는다.</summary>
    public static void Save<T>(string name, T value) where T : class
    {
        Directory.CreateDirectory(Dir);
        var path = PathOf(name);
        var tmp  = path + ".tmp";
        var enc  = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value, Json), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(tmp, enc);
        File.Move(tmp, path, overwrite: true);
    }

    private static void PopLogSafe(string msg)
    {
        try { AMES.Pop.Common.PopLog.Append("offline", "OfflineStore", msg); } catch { }
    }
}
