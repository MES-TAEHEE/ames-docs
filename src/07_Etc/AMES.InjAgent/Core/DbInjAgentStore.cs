using AMES.Contracts.Dto;
using AMES.Data.Repositories;

namespace AMES.InjAgent.Core;

/// <summary>IInjAgentStore → AMES.Data 리포지토리 위임.</summary>
public sealed class DbInjAgentStore : IInjAgentStore
{
    private readonly InjLotRepository  _lots;
    private readonly InjCondRepository _cond;

    public DbInjAgentStore(InjLotRepository lots, InjCondRepository cond)
    {
        _lots = lots;
        _cond = cond;
    }

    public List<MoldItemMapDto> GetMoldItems(string moldCode, string colorCode)
        => _lots.GetMoldItems(moldCode, colorCode);

    public (int LotId, string LotCode) CreateRawLot(string lineId, string equipId, MoldItemMapDto map, long machineShotCount)
        => _lots.CreateRawLot(lineId, equipId, map, machineShotCount);

    public void SaveInspection(int lotId, string equipId, string cavityPos,
        string shortMold, string weldLine, string gas, string weight, bool overallNg)
        => _lots.SaveInspection(lotId, equipId, cavityPos, shortMold, weldLine, gas, weight, overallNg);

    public void MarkNgBlocked(int lotId) => _lots.MarkNgBlocked(lotId);

    public List<InjCondItemDto> GetCondItems(string lineId) => _cond.GetItems(lineId);

    public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue)
        => _cond.InsertLog(lineId, itemCode, shotSeq, setValue, actualValue);
}
