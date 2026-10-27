using AMES.InjAgent.Plc;

namespace AMES.InjAgent.Core;

/// <summary>호기 1대의 폴링 상태 스냅샷 (UI 표시용).</summary>
public sealed record MachineStatus(
    string EquipId, bool MachineConnected, bool RobotConnected,
    long? ShotCount, string MoldRaw, string? LotLh, string? LotRh, string LastInspection,
    int MissingLots = 0, int RobotWriteFailures = 0);

/// <summary>
/// 사출기 1호기 폴링 파이프라인. 원본 Main.cs tmrRunModbus 의
/// 샷 감지 → LOT 생성 → 로봇 송신 → 검사 수신 → 조건 수집 흐름을
/// 주입된 인터페이스 위에서 재구성한 것. PollOnce 단위로 단위테스트 가능.
/// </summary>
public sealed class MachinePoller : IDisposable
{
    private readonly MachineConfig     _cfg;
    private readonly IInjectionMachine _machine;
    private readonly IRobotLink        _robot;
    private readonly IInjAgentStore    _store;
    private readonly Action<string>    _log;

    private long?  _lastShotCount;
    private string _moldRaw = string.Empty;
    private readonly CavityLot?[] _slots = new CavityLot?[2];      // [0]=CavityNo1(LH/1st), [1]=CavityNo2(RH/2nd)
    private readonly bool[]    _inspectionArmed  = new bool[2];    // 샷 후 클리어(all-null) 관측 전까지 이전 사이클 잔존 판정 무시
    private readonly string?[] _lastInspectionKey = new string?[2];
    private readonly string[]  _inspectionText   = { "—", "—" };
    private volatile MachineStatus? _statusSnapshot;               // UI는 불변 스냅샷만 읽음 (C1)
    private int _missingLots;                                      // 재시도까지 실패해 LOT 이 없는 샷 캐비티 수
    private int _robotWriteFailures;                               // 로봇 쓰기 연속 실패 횟수(성공하면 0)
    private readonly PendingInspection?[] _pending = new PendingInspection?[2];   // 저장·차단·응답 중 아직 끝나지 않은 판정
    private const int LotCreateAttempts = 3;
    private const int LotRetryDelayMs = 200;

    private sealed record CavityLot(int LotId, string LotCode, string ItemNo, string CavityPos, string MoldRaw);

    public MachinePoller(MachineConfig cfg, IInjectionMachine machine, IRobotLink robot,
                         IInjAgentStore store, Action<string> log)
    {
        _cfg = cfg;
        _machine = machine;
        _robot = robot;
        _store = store;
        _log = msg => log($"[{cfg.EquipId}] {msg}");
    }

    public MachineStatus Status => _statusSnapshot
        ?? new MachineStatus(_cfg.EquipId, false, false, null, string.Empty, null, null, "—");

    public void PollOnce()
    {
        try
        {
            bool machineOk = _machine.EnsureConnected();
            bool robotOk   = _robot.EnsureConnected() && _robot.RefreshBlock();
            if (!machineOk) return;

            long shot; string moldRaw;
            try
            {
                shot    = _machine.ReadShotCount();
                moldRaw = _machine.ReadMoldCode();
            }
            catch (Exception ex)
            {
                _log($"Machine read failed: {ex.Message}");
                return;
            }

            if (_lastShotCount is null)
            {
                _lastShotCount = shot;
                _moldRaw = moldRaw;
                _log($"baseline — shot count {shot}, mold '{moldRaw}' (no LOT created on start)");
                return;
            }

            // 원본의 3중 OR 중 취출신호(D5001) 조건은 의도적으로 제외 — 샷카운트 불변 상태에서
            // 반복 감지되어 중복 LOT 를 만들 위험이 있고, 시뮬레이터도 해당 비트를 쓰지 않는다.
            // LOT 은 샷이 실제로 늘었을 때만 만든다. 카운터가 줄었으면(리셋) 기준선만 다시 잡고,
            // 샷 없이 금형 코드만 바뀌었으면 기록만 한다 — 둘 다 LOT 을 만들지 않는다.
            if (shot > _lastShotCount)                              HandleShot(shot, moldRaw, robotOk);
            else if (shot < _lastShotCount)                         Rebaseline(shot, moldRaw);
            else if (moldRaw != _moldRaw && moldRaw.Length > 0)     ChangeMoldOnly(moldRaw);
            else if (robotOk)                                       ResendToRobot();

            if (robotOk) HandleInspection();
        }
        finally
        {
            PublishSnapshot();
        }
    }

    /// <summary>카운터가 줄었다(리셋·재기동). LOT 은 만들지 않고 기준선만 다시 잡는다.</summary>
    void Rebaseline(long shot, string moldRaw)
    {
        _log($"Counter reset {_lastShotCount} → {shot} — 기준선만 다시 잡음, LOT 미생성");
        _lastShotCount = shot;
        if (moldRaw.Length > 0) _moldRaw = moldRaw;
        ClearLotState();
    }

    /// <summary>
    /// 샷 없이 금형 코드만 바뀌었다. LOT 은 만들지 않는다 — 다음 실제 샷부터 새 금형으로 찍힌다.
    /// 진행 중인 LOT 과 검사 대기는 그대로 둔다(LOT 이 자기 금형 코드를 가지므로 송신 값도 바뀌지 않는다).
    /// </summary>
    void ChangeMoldOnly(string moldRaw)
    {
        _log($"Mold code changed '{_moldRaw}' → '{moldRaw}' (no shot) — LOT 미생성, 진행 중 LOT 유지");
        _moldRaw = moldRaw;
    }

    /// <summary>현재 LOT·검사 상태를 비운다(샷이 아닌 이유로 기준이 바뀔 때).</summary>
    void ClearLotState()
    {
        Array.Clear(_slots);
        _lastInspectionKey[0] = _lastInspectionKey[1] = null;
        _inspectionArmed[0] = _inspectionArmed[1] = false;
        _pending[0] = _pending[1] = null;
    }

    /// <summary>STOP→START 대비 상태 초기화. 다음 PollOnce 는 baseline 만 잡는다 (LOT 미생성).</summary>
    public void ResetForRestart()
    {
        _lastShotCount = null;
        _moldRaw = string.Empty;
        Array.Clear(_slots);
        _lastInspectionKey[0] = _lastInspectionKey[1] = null;
        _pending[0] = _pending[1] = null;
        _inspectionArmed[0] = _inspectionArmed[1] = false;
        _inspectionText[0] = _inspectionText[1] = "—";
        PublishSnapshot();
    }

    /// <summary>STOP 시 소켓 해제 (재시작 가능 — Dispose 아님).</summary>
    public void DisconnectClients()
    {
        _machine.Disconnect();
        _robot.Disconnect();
        PublishSnapshot();
    }

    void PublishSnapshot()
        => _statusSnapshot = new MachineStatus(
            _cfg.EquipId, _machine.Connected, _robot.Connected,
            _lastShotCount, _moldRaw,
            _slots[0]?.LotCode, _slots[1]?.LotCode,
            $"{_inspectionText[0]} / {_inspectionText[1]}", _missingLots, _robotWriteFailures);

    void HandleShot(long shot, string moldRaw, bool robotOk)
    {
        var prev = _lastShotCount ?? shot;
        _lastShotCount = shot;
        if (moldRaw.Length > 0) _moldRaw = moldRaw;
        _log($"Shot detected — count {shot}, mold '{_moldRaw}'");

        Array.Clear(_slots);
        _lastInspectionKey[0] = _lastInspectionKey[1] = null;
        _inspectionArmed[0] = _inspectionArmed[1] = false;
        _pending[0] = _pending[1] = null;

        // 조건수집은 LOT 생성과 독립 — 기준정보 미등록 호기도 조건 이력은 남긴다.
        CollectConditions(shot);

        var (mold, color) = PlcCodec.SplitMoldColor(_moldRaw);
        if (mold.Length == 0) { _log("No mold code — skipping LOT creation"); return; }

        List<Contracts.Dto.MoldItemDto> map;
        try { map = _store.GetMoldItems(mold, color); }
        catch (Exception ex) { _log($"Mold mapping lookup failed: {ex.Message} — LOT for this shot is lost (same policy as original, manual entry required)"); return; }
        if (map.Count == 0) { _log($"Mold code not found in master data: {mold}+{color} — no LOT created"); return; }

        // 폴링 사이에 건너뛴 샷(prev+1 … shot)마다 LOT 세트를 만든다.
        // 금형만 바뀌었거나 카운터가 줄었으면(prev ≥ shot) 현재 샷 한 세트만 만든다.
        long first = shot > prev ? prev + 1 : shot;
        for (long s = first; s <= shot; s++)
        {
            Array.Clear(_slots);
            CreateCavityLots(map, s);
        }

        if (robotOk) ResendToRobot();
    }

    /// <summary>한 샷분 캐비티 LOT 을 만든다. 마지막 샷의 결과만 슬롯에 남아 로봇 송신에 쓰인다.</summary>
    void CreateCavityLots(List<Contracts.Dto.MoldItemDto> map, long shot)
    {
        foreach (var m in map.OrderBy(x => x.CavityNo))
        {
            int slot = m.CavityNo - 1;
            if (slot is < 0 or > 1) { _log($"CavityNo {m.CavityNo} out of range — ignored"); continue; }
            // DB 연결 장애는 저장소가 큐에 쌓아 여기까지 오지 않는다 — 여기 실패는 번호 소진·데이터 오류 같은 것이다.
            // 짧게 몇 번 다시 시도하고, 그래도 안 되면 LOT 없는 제품으로 표시해 사람이 확인하게 한다.
            Exception? last = null;
            for (int attempt = 1; attempt <= LotCreateAttempts; attempt++)
            {
                try
                {
                    var (lotId, lotCode) = _store.CreateRawLot(_cfg.LineId, _cfg.EquipId, m, shot);
                    _slots[slot] = new CavityLot(lotId, lotCode, m.ItemNo, m.CavityPos, _moldRaw);
                    _log(attempt == 1
                        ? $"{m.CavityPos} LOT created: {lotCode} (shot {shot})"
                        : $"{m.CavityPos} LOT created: {lotCode} (shot {shot}, attempt {attempt})");
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    _log($"{m.CavityPos} LOT creation failed (shot {shot}, attempt {attempt}/{LotCreateAttempts}): {ex.Message}");
                    if (attempt < LotCreateAttempts) Thread.Sleep(LotRetryDelayMs);
                }
            }
            if (last is not null)
            {
                // 슬롯을 비워두므로 로봇 송신·검사 매핑이 다른 캐비티와 교차 오염되지 않는다.
                _missingLots++;
                _log($"!!! {m.CavityPos} LOT MISSING (shot {shot}) — 수동 확인 필요: {last.Message}");
            }
        }
    }

    /// <summary>
    /// 로봇 미확인(D5002.0==0) 동안 금형/LOT/품번을 반복 송신 (원본 625-670행).
    /// 쓰기가 하나라도 실패하면 비트 32 를 켜지 않는다 — 로봇은 LOT 을 받을 준비가 안 된 것으로 남고, 다음 주기에 같은 값을 다시 쓴다.
    /// </summary>
    void ResendToRobot()
    {
        if (_robot.ReadBit(5002, 0) != 0) return;
        // LOT 이 없으면 비트 32 를 켜지 않는다 — 로봇이 LOT 준비로 오해하지 않게.
        var mold = (_slots[0] ?? _slots[1])?.MoldRaw;
        if (string.IsNullOrEmpty(mold)) return;
        // &= 는 앞에서 실패해도 뒤의 쓰기를 모두 시도한다 — 한 값이 빠져도 나머지는 로봇에 보낸다.
        bool ok = _robot.WriteString(5700, mold);
        if (_slots[0] is { } first)
        {
            ok &= _robot.WriteString(5100, first.LotCode);
            ok &= _robot.WriteString(5800, first.ItemNo);
        }
        if (_slots[1] is { } second)
        {
            ok &= _robot.WriteString(5200, second.LotCode);
            ok &= _robot.WriteString(5900, second.ItemNo);
        }
        if (ok) ok = _robot.WriteBit(32, true);

        if (ok) { _robotWriteFailures = 0; return; }
        _robotWriteFailures++;
        _log($"로봇 LOT 송신 실패(연속 {_robotWriteFailures}회) — 비트 32 를 켜지 않고 다음 주기에 다시 보냅니다");
    }

    /// <summary>D5007(LH)/D5008(RH) 검사 4항목이 모두 도착하면 1회 저장. NG 는 스캔 차단.</summary>
    void HandleInspection()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            var lot = _slots[slot];
            if (lot is null) continue;

            if (_pending[slot] is null)
            {
                int word = slot == 0 ? 5007 : 5008;
                var results = ReadJudgements(word);
                bool allNull = results.All(r => r is null);

                // 샷 직후 이전 사이클 판정이 아직 유지 중일 수 있다 — 클리어(all-null)를 한 번
                // 관측한 뒤에 도착하는 판정만 이 LOT의 것으로 수용한다.
                if (!_inspectionArmed[slot])
                {
                    if (allNull) _inspectionArmed[slot] = true;
                    continue;
                }
                if (results.Any(r => r is null)) continue;

                string key = string.Join(',', results);
                if (_lastInspectionKey[slot] == key) continue;   // 같은 판정 유지 중 재저장 방지 (판정 변경 시 재검사로 보고 추가 저장 허용)

                _pending[slot] = new PendingInspection(results, key, results.Contains("NG"), CountSafely(lot));
            }

            ProcessPending(slot, lot);
        }
    }

    /// <summary>저장 → NG 차단 → 응답 순서로 한 판정을 끝낸다. 실패한 단계만 다음 주기에 다시 하고, 끝난 단계는 다시 하지 않는다.</summary>
    void ProcessPending(int slot, CavityLot lot)
    {
        var p = _pending[slot]!;

        if (!p.Saved)
        {
            try
            {
                // 이전 저장이 예외로 끝났다면 커밋됐을 수도 있다 — 행이 늘었으면 이미 들어간 것으로 본다.
                bool alreadySaved = p.Uncertain && p.Before >= 0
                                 && _store.CountInspections(lot.LotId, lot.CavityPos) > p.Before;
                if (!alreadySaved)
                    _store.SaveInspection(lot.LotId, _cfg.EquipId, lot.CavityPos,
                                          p.Results[0]!, p.Results[1]!, p.Results[2]!, p.Results[3]!, p.Ng);
                p.Saved = true;
                _log(p.Ng ? $"{lot.LotCode} inspection NG ({p.Key})" : $"{lot.LotCode} inspection done: {p.Key}");
            }
            catch (Exception ex)
            {
                // 저장 실패 시 응답을 보내지 않는다 → 로봇이 판정을 유지하는 동안 다음 폴에서 재시도.
                p.Uncertain = true;
                _log($"Inspection save failed ({lot.LotCode}): {ex.Message} — will retry");
                return;
            }
        }

        if (p.Ng && !p.Marked)
        {
            try
            {
                _store.MarkNgBlocked(lot.LotId);
                p.Marked = true;
                _log($"{lot.LotCode} NG → scan blocked");
            }
            catch (Exception ex)
            {
                // 저장은 이미 끝났다 — 저장하지 않고 차단만 다시 한다.
                _log($"{lot.LotCode} NG 차단 실패: {ex.Message} — will retry");
                return;
            }
        }

        if (!AcknowledgeInspection(lot)) return;

        _lastInspectionKey[slot] = p.Key;
        _inspectionText[slot] = $"{lot.CavityPos}: {p.Key}";
        _pending[slot] = null;
    }

    /// <summary>저장 전에 같은 LOT·캐비티의 검사 행 수를 본다(예외 뒤 재시도의 중복 확인용). 알 수 없으면 -1.</summary>
    int CountSafely(CavityLot lot)
    {
        try { return _store.CountInspections(lot.LotId, lot.CavityPos); }
        catch (Exception ex) { _log($"검사 이력 확인 실패(중복 확인 생략): {ex.Message}"); return -1; }
    }

    /// <summary>검사 응답 비트(33)를 쓴다. 실패하면 false — 다음 주기에 응답만 다시 쓴다.</summary>
    bool AcknowledgeInspection(CavityLot lot)
    {
        if (_robot.WriteBit(33, true)) { _robotWriteFailures = 0; return true; }
        _robotWriteFailures++;
        _log($"{lot.LotCode} 검사 응답 쓰기 실패(연속 {_robotWriteFailures}회) — 다음 주기에 응답만 다시 씁니다");
        return false;
    }

    /// <summary>처리 중인 검사 판정 한 건 — 단계별 완료 여부를 기억해 같은 단계를 두 번 하지 않는다.</summary>
    private sealed class PendingInspection
    {
        public PendingInspection(string?[] results, string key, bool ng, int before)
        {
            Results = results;
            Key = key;
            Ng = ng;
            Before = before;
        }

        public string?[] Results { get; }
        public string Key { get; }
        public bool Ng { get; }
        public int Before { get; }            // 저장 전 검사 행 수(-1 = 알 수 없음)
        public bool Uncertain { get; set; }   // 저장이 예외로 끝나 커밋 여부를 모른다
        public bool Saved { get; set; }
        public bool Marked { get; set; }
    }

    /// <summary>항목 4종(미성형/웰드라인/가스/중량) × 3비트(OK/NG/PASS). 미도착 항목은 null.</summary>
    string?[] ReadJudgements(int word)
    {
        var results = new string?[4];
        for (int item = 0; item < 4; item++)
        {
            if      (_robot.ReadBit(word, item * 3)     == 1) results[item] = "OK";
            else if (_robot.ReadBit(word, item * 3 + 1) == 1) results[item] = "NG";
            else if (_robot.ReadBit(word, item * 3 + 2) == 1) results[item] = "PASS";
        }
        return results;
    }

    /// <summary>샷 시점 사출조건(세팅/실제) 수집 → PR_InjCondLog. 항목별 실패는 개별 로그.</summary>
    void CollectConditions(long shot)
    {
        List<Contracts.Dto.InjCondItemDto> items;
        try { items = _store.GetCondItems(_cfg.LineId); }
        catch (Exception ex) { _log($"Condition master lookup failed: {ex.Message}"); return; }

        foreach (var item in items)
        {
            try
            {
                decimal? setV = ReadCond(item.DataType, item.SetAddress);
                decimal? actV = ReadCond(item.DataType, item.ActualAddress);
                if (setV is null && actV is null) continue;
                _store.InsertCondLog(_cfg.LineId, item.ItemCode, shot, setV, actV);
            }
            catch (Exception ex)
            {
                _log($"Condition collection failed ({item.ItemCode}): {ex.Message}");
            }
        }
    }

    decimal? ReadCond(string dataType, int? address)
    {
        if (address is null) return null;
        return dataType == "FLOAT"
            ? (decimal)_machine.ReadFloat(address.Value)
            : _machine.ReadLong(address.Value);
    }

    public void Dispose()
    {
        (_machine as IDisposable)?.Dispose();
        (_robot as IDisposable)?.Dispose();
    }
}
