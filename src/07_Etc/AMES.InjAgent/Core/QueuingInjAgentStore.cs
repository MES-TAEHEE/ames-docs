using AMES.Contracts.Dto;
using AMES.Data.Services;

namespace AMES.InjAgent.Core;

/// <summary>
/// DB 가 끊기면 샷 LOT·검사·조건 이력을 디스크 큐에 쌓고, 복구되면 샷 순서대로 DB 에 다시 넣는 저장소 어댑터.
/// 번호는 온라인일 때 확보한 구간(<see cref="LotBlock"/>)에서 받는다 — POP 오프라인 구간과 겹치지 않는다.
/// 모든 공개 메서드는 한 잠금 아래에서 돈다(폴러 여러 대가 같은 큐를 쓴다).
/// </summary>
public sealed class QueuingInjAgentStore : IInjAgentStore
{
    public const int BlockSize   = 100;   // 한 번에 확보하는 LOT 번호 수
    public const int RefillBelow = 20;    // 이 아래로 남으면 다음 주기에 새 구간을 받는다

    readonly IInjAgentReplayStore    _db;
    readonly OfflineStateFile        _file;
    readonly OfflineState            _state;
    readonly Func<Exception, bool>   _isDbDown;
    readonly Action<string>          _log;
    readonly Func<DateTime>          _now;
    readonly TimeSpan                _downBackoff;   // DB 장애 뒤 이 시간 동안은 DB 를 부르지 않는다(연결 대기 방지)
    readonly object                  _gate = new();
    DateTime                         _skipDbUntilUtc = DateTime.MinValue;

    public QueuingInjAgentStore(IInjAgentReplayStore db, OfflineStateFile file, Func<Exception, bool> isDbDown,
                                Action<string> log, Func<DateTime>? now = null, TimeSpan? downBackoff = null)
    {
        _db = db;
        _file = file;
        _state = file.Load();
        _isDbDown = isDbDown;
        _log = log;
        _now = now ?? (() => DbClock.Now);
        _downBackoff = downBackoff ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>대기 중인 작업 수(테스트·상태 표시용).</summary>
    public int PendingCount { get { lock (_gate) return _state.Ops.Count; } }

    // ── 마스터 조회: 온라인이면 읽어서 캐시, 끊기면 캐시 ─────────────

    public List<MoldItemDto> GetMoldItems(string moldCode, string colorCode)
    {
        var key = $"{moldCode}|{colorCode}";
        lock (_gate)
        {
            if (DbOpen())
            {
                try
                {
                    var rows = _db.GetMoldItems(moldCode, colorCode);
                    _state.Molds[key] = rows;
                    SaveLocked();
                    return rows;
                }
                catch (Exception ex) when (IsDown(ex)) { }
            }
            return _state.Molds.TryGetValue(key, out var cached)
                ? cached
                : throw new InvalidOperationException($"금형 마스터를 읽지 못했고 오프라인 캐시도 없다: {key}");
        }
    }

    public List<InjCondItemDto> GetCondItems(string lineId)
    {
        lock (_gate)
        {
            if (DbOpen())
            {
                try
                {
                    var rows = _db.GetCondItems(lineId);
                    _state.Conds[lineId] = rows;
                    SaveLocked();
                    return rows;
                }
                catch (Exception ex) when (IsDown(ex)) { }
            }
            return _state.Conds.TryGetValue(lineId, out var cached) ? cached : new List<InjCondItemDto>();
        }
    }

    // ── 쓰기: 온라인이면 바로, 아니면 큐 ─────────────────────────────

    public (int LotId, string LotCode) CreateRawLot(string lineId, string equipId, MoldItemDto map, long machineShotCount)
    {
        lock (_gate)
        {
            if (_state.Ops.Count > 0) ReplayLocked();   // 밀린 작업이 먼저 들어가야 번호·순서가 맞는다
            if (_state.Ops.Count == 0 && DbOpen())
            {
                try { return _db.CreateRawLot(lineId, equipId, map, machineShotCount); }
                catch (Exception ex) when (IsDown(ex)) { }
            }

            var block = _state.Blocks.FirstOrDefault(b => b.LineId == lineId && b.Day == _now().Date && b.Next <= b.Last)
                ?? throw new InvalidOperationException($"오프라인 LOT 번호 구간이 없다({lineId}) — DB 가 끊기기 전에 이 날 구간을 받지 못했다");
            var code = block.Header + block.Next.ToString("D4");
            block.Next++;

            var temp = -(++_state.TempSeq);
            _state.Ops.Add(new QueuedOp
            {
                Kind = "LOT", LineId = lineId, EquipId = equipId, LotId = temp, LotCode = code,
                Map = map, Shot = machineShotCount, OccurredAt = _now(),
            });
            SaveLocked();
            return (temp, code);
        }
    }

    /// <summary>검사 이력 행 수. DB 가 안 되거나 임시 번호(오프라인 LOT)면 -1 — 알 수 없음.</summary>
    public int CountInspections(int lotId, string cavityPos)
    {
        lock (_gate)
        {
            if (lotId < 0 || !DbOpen()) return -1;
            try { return _db.CountInspections(lotId, cavityPos); }
            catch (Exception ex) when (IsDown(ex)) { return -1; }
        }
    }

    public void SaveInspection(int lotId, string equipId, string cavityPos,
                               string shortMold, string weldLine, string gas, string weight, bool overallNg)
    {
        Write(
            new QueuedOp
            {
                Kind = "INSP", EquipId = equipId, LotId = lotId, CavityPos = cavityPos, ShortMold = shortMold,
                WeldLine = weldLine, Gas = gas, Weight = weight, OverallNg = overallNg,
            },
            id => _db.SaveInspection(id, equipId, cavityPos, shortMold, weldLine, gas, weight, overallNg),
            id => _db.CountInspections(id, cavityPos));
    }

    public void MarkNgBlocked(int lotId)
        => Write(new QueuedOp { Kind = "NG", LotId = lotId }, id => _db.MarkNgBlocked(id));

    public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue)
        => Write(
            new QueuedOp { Kind = "COND", LineId = lineId, ItemCode = itemCode, Shot = shotSeq, SetValue = setValue, ActualValue = actualValue },
            _ => _db.InsertCondLog(lineId, itemCode, shotSeq, setValue, actualValue));

    /// <summary>
    /// 누가 바로 할 수 없었던 쓰기 — 큐가 비었고 DB 가 살아 있으면 <paramref name="online"/> 을 바로 부른다.
    /// 아니면 큐에 넣는다. 임시 번호는 온라인 호출 전에 실제 번호로 바꾸고, 못 바꾸면 큐에 넣는다.
    /// </summary>
    void Write(QueuedOp op, Action<int> online, Func<int, int>? baseline = null)
    {
        lock (_gate)
        {
            if (_state.Ops.Count > 0) ReplayLocked();
            if (_state.Ops.Count == 0 && DbOpen() && TryResolve(op.LotId, out var real))
            {
                try
                {
                    // 저장 전 행 수를 남긴다 — 저장이 커밋된 뒤 연결이 끊겨 큐에 들어가도 재생이 두 번 넣지 않는다.
                    if (baseline is not null) op.Before = baseline(real);
                    online(real);
                    return;
                }
                catch (Exception ex) when (IsDown(ex)) { }
            }
            op.OccurredAt = _now();
            _state.Ops.Add(op);
            SaveLocked();
        }
    }

    // ── 복구 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 주기적으로 부른다(5초). DB 가 살아 있으면 밀린 작업을 넣고, 번호 구간이 모자라면 새로 받는다.
    /// </summary>
    public void Sync(IEnumerable<string> lineIds)
    {
        lock (_gate)
        {
            if (!DbOpen()) return;
            try
            {
                if (_state.Ops.Count > 0) ReplayLocked();
                foreach (var line in lineIds.Distinct())
                    RefillLocked(line);
                _state.Blocks.RemoveAll(b => b.Day != _now().Date);   // 어제 구간은 큐 작업이 참조하지 않는다(LOT 코드로 이미 들어 있다)
                SaveLocked();
            }
            catch (Exception ex) when (IsDown(ex)) { }
        }
    }

    /// <summary>밀린 작업을 순서대로 넣는다. DB 장애면 멈추고 남긴다. 데이터 오류면 그 한 건만 버린다(막히지 않게).</summary>
    void ReplayLocked()
    {
        while (_state.Ops.Count > 0)
        {
            var op = _state.Ops[0];
            try { ApplyLocked(op); }
            catch (Exception ex) when (IsDown(ex)) { return; }
            catch (Exception ex)
            {
                _log($"Offline replay dropped {op.Kind} {op.LotCode}: {ex.Message}");
            }
            _state.Ops.RemoveAt(0);
            SaveLocked();
        }
    }

    void ApplyLocked(QueuedOp op)
    {
        switch (op.Kind)
        {
            case "LOT":
                // 이전 재생이 넣고 죽었을 수 있다 — 번호가 이미 있으면 다시 넣지 않고 임시 번호만 잇는다.
                var lotId = _db.FindLotId(op.LotCode)
                         ?? _db.CreateRawLotAt(op.LineId, op.EquipId, op.Map!, op.Shot, op.LotCode, op.OccurredAt).LotId;
                _state.TempMap[op.LotId] = lotId;
                _log($"offline LOT {op.LotCode} → DB (shot {op.Shot})");
                break;
            case "INSP":
            {
                var realLotId = Real(op.LotId);
                // 끊기기 전에 이미 커밋됐다면(저장 전 행 수보다 많다) 다시 넣지 않는다.
                if (op.Before >= 0 && _db.CountInspections(realLotId, op.CavityPos) > op.Before) break;
                _db.SaveInspection(realLotId, op.EquipId, op.CavityPos, op.ShortMold, op.WeldLine, op.Gas, op.Weight, op.OverallNg);
                break;
            }
            case "NG":
                _db.MarkNgBlocked(Real(op.LotId));
                break;
            case "COND":
                _db.InsertCondLog(op.LineId, op.ItemCode, op.Shot, op.SetValue, op.ActualValue);
                break;
            default:
                throw new InvalidOperationException($"알 수 없는 큐 작업: {op.Kind}");
        }
    }

    void RefillLocked(string lineId)
    {
        var today = _now().Date;
        var left = _state.Blocks.Where(b => b.LineId == lineId && b.Day == today).Sum(b => Math.Max(0, b.Last - b.Next + 1));
        if (left >= RefillBelow) return;
        try
        {
            var block = _db.ReserveLotNos(lineId, today, BlockSize);
            if (block.Count > 0)
                _state.Blocks.Add(new LotBlock
                {
                    LineId = lineId, Day = today, Header = block.Header,
                    Next = block.FirstSeq, Last = block.FirstSeq + block.Count - 1,
                });
        }
        catch (Exception ex) when (IsDown(ex)) { throw; }   // 바깥 Sync 에서 장애로 처리
        catch (Exception ex) { _log($"LOT 번호 구간 확보 실패({lineId}): {ex.Message}"); }
    }

    // ── 공통 ─────────────────────────────────────────────────────────

    int Real(int id)
        => TryResolve(id, out var real)
            ? real
            : throw new InvalidOperationException($"임시 LOT 번호 {id} 가 아직 실제 번호로 바뀌지 않았다");

    bool TryResolve(int id, out int real)
    {
        if (id >= 0) { real = id; return true; }   // 0 = 대상 LOT 없음(COND)
        return _state.TempMap.TryGetValue(id, out real);
    }

    /// <summary>DB 장애로 판정되면 잠시 DB 호출을 멈춘다. 장애가 아니면 false.</summary>
    bool IsDown(Exception ex)
    {
        if (!_isDbDown(ex)) return false;
        _skipDbUntilUtc = DateTime.UtcNow + _downBackoff;
        return true;
    }

    bool DbOpen() => DateTime.UtcNow >= _skipDbUntilUtc;

    void SaveLocked()
    {
        try { _file.Save(_state); }
        catch (Exception ex) { _log($"offline state save failed: {ex.Message}"); }
    }
}
