using System.Security.Cryptography;
using System.Text.Json;
using AMES.Contracts.Dto;

namespace AMES.InjAgent.Core;

/// <summary>
/// 오프라인 상태 — LOT 번호 구간, 대기 작업, 금형·조건 마스터 캐시. DB 가 끊겨도 샷을 잃지 않게 디스크에 남긴다.
/// 번호 구간은 DB 가 온라인일 때 미리 확보한 것이라 POP 의 구간과 겹치지 않는다(같은 SYS_LotSeq 에서 빠져 나온다).
/// </summary>
public sealed class OfflineState
{
    public List<LotBlock>                        Blocks  { get; set; } = new();
    public List<QueuedOp>                        Ops     { get; set; } = new();
    public Dictionary<string, List<MoldItemDto>> Molds   { get; set; } = new();
    public Dictionary<string, List<InjCondItemDto>> Conds { get; set; } = new();
    public Dictionary<int, int>                  TempMap { get; set; } = new();   // 임시 LotID(음수) → 실제 LotID
    public int                                   TempSeq { get; set; }
}

/// <summary>하루치 LOT 번호 구간 — Next 부터 Last 까지 아직 안 쓴 번호.</summary>
public sealed class LotBlock
{
    public string   LineId { get; set; } = string.Empty;
    public DateTime Day    { get; set; }
    public string   Header { get; set; } = string.Empty;   // 년월일+라인코드 5자
    public int      Next   { get; set; }
    public int      Last   { get; set; }
}

/// <summary>
/// 복구 뒤 DB 에 다시 넣을 작업 한 건. Kind: LOT(샷 LOT 생성) · INSP(검사 저장) · NG(NG 차단) · COND(조건 이력).
/// LotId 가 음수면 오프라인에서 받은 임시 번호다 — 복구 때 <see cref="OfflineState.TempMap"/> 으로 실제 번호에 잇는다.
/// </summary>
public sealed class QueuedOp
{
    public string      Kind        { get; set; } = string.Empty;
    public string      LineId      { get; set; } = string.Empty;
    public string      EquipId     { get; set; } = string.Empty;
    public int         LotId       { get; set; }
    public string      LotCode     { get; set; } = string.Empty;
    public MoldItemDto? Map        { get; set; }
    public long        Shot        { get; set; }
    public DateTime    OccurredAt  { get; set; }
    public string      CavityPos   { get; set; } = string.Empty;
    public string      ShortMold   { get; set; } = string.Empty;
    public string      WeldLine    { get; set; } = string.Empty;
    public string      Gas         { get; set; } = string.Empty;
    public string      Weight      { get; set; } = string.Empty;
    public bool        OverallNg   { get; set; }
    /// <summary>INSP: 저장을 시도하기 전 같은 LOT·캐비티의 검사 행 수(-1 = 알 수 없음). 재생 때 이보다 많으면 이미 커밋된 것이다.</summary>
    public int         Before      { get; set; } = -1;
    public string      ItemCode    { get; set; } = string.Empty;
    public decimal?    SetValue    { get; set; }
    public decimal?    ActualValue { get; set; }
}

/// <summary>오프라인 상태 파일 — 현재 Windows 사용자 DPAPI 로 암호화한 JSON.</summary>
public sealed class OfflineStateFile
{
    static readonly byte[] Entropy = "AMES.InjAgent.Offline"u8.ToArray();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    readonly string _path;

    public OfflineStateFile(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMES", "InjAgent", "offline.bin");

    /// <summary>파일이 없으면 빈 상태. 읽기에 실패하면 예외 — 조용히 비우면 대기 중인 샷이 사라진다.</summary>
    public OfflineState Load()
    {
        if (!File.Exists(_path)) return new OfflineState();
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<OfflineState>(plain, Json) ?? new OfflineState();
    }

    public void Save(OfflineState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var enc = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state, Json), Entropy, DataProtectionScope.CurrentUser);
        var tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, enc);
        File.Move(tmp, _path, overwrite: true);   // 쓰는 도중 끊겨도 이전 파일이 남는다
    }
}
