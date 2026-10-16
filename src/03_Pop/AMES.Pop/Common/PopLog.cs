namespace AMES.Pop.Common;

/// <summary>
/// 무인 동작(라벨 발행 · 스캐너 · 유휴 로그아웃)의 실패 기록. 이 경로들은 토스트로 알릴
/// 사람이 화면 앞에 없고 Debug.WriteLine 은 Release 에서 사라진다 — 사후 추적에는 파일이 필요하다.
/// 라벨 .zpl 과 같은 폴더에 남긴다: 현장에서 한 곳만 보면 된다.
/// </summary>
internal static class PopLog
{
    public static void Append(string file, string tag, string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[{tag}] {msg}");
        try
        {
            var dir = AppConfig.Current.PrinterOutputDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"{file}-{DateTime.Now:yyyyMMdd}.log"),
                               $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { /* 로깅 실패가 발행·수신·로그아웃을 막아서는 안 된다 */ }
    }
}
