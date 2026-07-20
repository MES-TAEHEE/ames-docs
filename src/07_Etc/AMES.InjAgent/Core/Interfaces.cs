using AMES.Contracts.Dto;

namespace AMES.InjAgent.Core;

/// <summary>사출기 (Modbus TCP) 읽기 전용 시야. 실패 시 예외 → 폴러가 잡아서 로그.</summary>
public interface IInjectionMachine
{
    bool Connected { get; }
    bool EnsureConnected();
    long   ReadShotCount();          // 주소 5000, LONG
    string ReadMoldCode();           // 주소 5330, ASCII 6워드
    long   ReadLong(int address);    // 사출조건 LONG
    float  ReadFloat(int address);   // 사출조건 FLOAT

    /// <summary>소켓 해제 (Dispose 와 달리 이후 EnsureConnected 로 재접속 가능).</summary>
    void Disconnect();
}

/// <summary>취출로봇 (FEnet) 링크. D영역 베이스 5000, 48워드 블록 캐시 기반.</summary>
public interface IRobotLink
{
    bool Connected { get; }
    bool EnsureConnected();
    bool RefreshBlock();                       // D5000~D5047 블록 읽기 → 캐시
    int  ReadBit(int word, int bit);           // 캐시에서 비트 (오류 -1)
    bool WriteBit(int point, bool on);         // 베이스 5000 + point (32=D5002.0, 33=D5002.1)
    bool WriteString(int wordAddr, string value); // CP949, %DB{addr*2}

    /// <summary>소켓 해제 (Dispose 와 달리 이후 EnsureConnected 로 재접속 가능).</summary>
    void Disconnect();
}

/// <summary>DB 저장소 시야 (Repository 어댑터). Task 7/8 에서 구현.</summary>
public interface IInjAgentStore
{
    List<MoldItemDto> GetMoldItems(string moldCode, string colorCode);
    (int LotId, string LotCode) CreateRawLot(string lineId, string equipId, MoldItemDto map, long machineShotCount);
    void MarkLabelPrinted(int lotId);
    void SaveInspection(int lotId, string equipId, string cavityPos,
                        string shortMold, string weldLine, string gas, string weight, bool overallNg);
    void MarkNgBlocked(int lotId);
    List<InjCondItemDto> GetCondItems(string lineId);
    void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue);
}

/// <summary>라벨 발행 (AMES.Devices 어댑터).</summary>
public interface ILabelPrinter
{
    void PrintLabel(string lotCode, string itemNo, string? itemName, string? colorCode, string? cavityPos, string lineId);
}
