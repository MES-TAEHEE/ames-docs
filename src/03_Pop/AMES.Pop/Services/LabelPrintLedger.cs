namespace AMES.Pop.Services;

/// <summary>
/// 이 PC 의 발행 원장 — LOT 코드 → 출력 시각. 오프라인 저장소(DPAPI 암호화 파일)에 두므로 DB 가 끊겨도 남는다.
/// 30일 지난 기록은 버린다. 파일 쓰기 실패는 출력을 막지 않는다(기록만 잃는다).
/// </summary>
internal sealed class LabelPrintLedger : ILabelLedger
{
    private const string FileName = "label-ledger.bin";
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    private readonly object _gate = new();
    private Dictionary<string, DateTime>? _printed;

    public bool Contains(string lotCode)
    {
        lock (_gate) return Load().ContainsKey(lotCode);
    }

    public void Record(string lotCode)
    {
        lock (_gate)
        {
            var printed = Load();
            var cutoff  = DateTime.Now - Keep;
            foreach (var old in printed.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList()) printed.Remove(old);
            printed[lotCode] = DateTime.Now;
            try { OfflineStore.Save(FileName, printed); }
            catch (Exception ex) { AMES.Pop.Common.PopLog.Append("print", "LabelLedger", $"save failed ({lotCode}): {ex.Message}"); }
        }
    }

    private Dictionary<string, DateTime> Load()
        => _printed ??= OfflineStore.Load<Dictionary<string, DateTime>>(FileName) ?? new Dictionary<string, DateTime>();
}
