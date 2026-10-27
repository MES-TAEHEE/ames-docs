using AMES.Contracts.Dto;
using AMES.Data.Repositories;

namespace AMES.InjAgent.Core;

/// <summary>IInjAgentStore → AMES.Data 리포지토리 위임.</summary>
public sealed class DbInjAgentStore : IInjAgentReplayStore
{
    private readonly InjLotRepository  _lots;
    private readonly InjCondRepository _cond;
    private readonly string?           _lateNgDefectCode;
    private readonly Action<string>    _log;

    public DbInjAgentStore(InjLotRepository lots, InjCondRepository cond,
                           string? lateNgDefectCode = null, Action<string>? log = null)
    {
        _lots = lots;
        _cond = cond;
        _lateNgDefectCode = lateNgDefectCode;
        _log  = log ?? (_ => { });
    }

    public List<MoldItemDto> GetMoldItems(string moldCode, string colorCode)
        => _lots.GetMoldItems(moldCode, colorCode);

    public (int LotId, string LotCode) CreateRawLot(string lineId, string equipId, MoldItemDto map, long machineShotCount)
        => _lots.CreateRawLot(lineId, equipId, map, machineShotCount);

    public (int LotId, string LotCode) CreateRawLotAt(string lineId, string equipId, MoldItemDto map, long machineShotCount,
                                                      string lotCode, DateTime occurredAt)
        => _lots.CreateRawLotAt(lineId, equipId, map, machineShotCount, lotCode, occurredAt);

    public int? FindLotId(string lotCode) => _lots.FindLotId(lotCode);

    public AMES.Data.Services.LotNoGenerator.LotNoBlock ReserveLotNos(string lineId, DateTime date, int count)
        => _lots.ReserveLotNos(lineId, date, count);

    public void SaveInspection(int lotId, string equipId, string cavityPos,
        string shortMold, string weldLine, string gas, string weight, bool overallNg)
        => _lots.SaveInspection(lotId, equipId, cavityPos, shortMold, weldLine, gas, weight, overallNg);

    public void MarkNgBlocked(int lotId)
    {
        var r = _lots.MarkNgBlocked(lotId, _lateNgDefectCode);
        switch (r.Outcome)
        {
            case InjLotRepository.NgMarkOutcome.LateRegistered:
                _log($"{r.LotCode} robot NG arrived after scan confirm — registered defect {_lateNgDefectCode} (result reversed, sent to REWORK)");
                break;
            case InjLotRepository.NgMarkOutcome.LateAfterConfirm:
                _log(r.Register is null
                    ? $"WARNING {r.LotCode} robot NG arrived after scan confirm — counted as good. Register defect on POP (set Agent:LateNgDefectCode to automate)"
                    : $"WARNING {r.LotCode} robot NG arrived after scan confirm — auto defect registration failed ({r.Register}). Register defect on POP");
                break;
        }
    }

    public List<InjCondItemDto> GetCondItems(string lineId) => _cond.GetItems(lineId);

    public int CountInspections(int lotId, string cavityPos) => _lots.CountInspections(lotId, cavityPos);

    public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue)
        => _cond.InsertLog(lineId, itemCode, shotSeq, setValue, actualValue);
}
