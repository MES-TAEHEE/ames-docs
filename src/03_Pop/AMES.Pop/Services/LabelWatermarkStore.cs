namespace AMES.Pop.Services;

/// <summary>
/// 라인별 라벨 워터마크를 오프라인 저장소(DPAPI 암호화 파일)에 둔다 — 로그아웃·재시작을 넘어 살아남는다.
/// 파일이 없거나 읽지 못하면 워터마크가 없는 것으로 본다(첫 로그인과 같다).
/// </summary>
internal sealed class LabelWatermarkStore : ILabelWatermarkStore
{
    private const string FileName = "label-watermark.bin";
    private readonly object _gate = new();

    public int? Load(string lineId)
    {
        lock (_gate)
        {
            var all = OfflineStore.Load<Dictionary<string, int>>(FileName);
            return all is not null && all.TryGetValue(lineId, out var w) ? w : null;
        }
    }

    public void Save(string lineId, int watermark)
    {
        lock (_gate)
        {
            var all = OfflineStore.Load<Dictionary<string, int>>(FileName) ?? new Dictionary<string, int>();
            all[lineId] = watermark;
            OfflineStore.Save(FileName, all);
        }
    }
}
