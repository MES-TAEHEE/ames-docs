namespace AMES.Devices;

/// <summary>
/// IMG 완제품 라벨 한 장의 데이터. 고객(수주처) 표준 DataMatrix + 사람이 읽는 5줄.
/// </summary>
/// <param name="LotCode">우리 LotNo (9자). DataMatrix T 토큰 끝과 인쇄 LOT 줄에 쓴다.</param>
/// <param name="ItemNo">MD_Item.ItemNo. 인쇄는 그대로, DataMatrix P 토큰은 하이픈을 뺀다.</param>
/// <param name="CustomerCode">수주처 MD_Customer.CustomerCode — DataMatrix V 토큰.</param>
/// <param name="Pgn">MD_Item.PGN — S 토큰 앞부분.</param>
/// <param name="Alc">MD_Item.ALC — S 토큰 뒷부분 + 좌상 큰 글자.</param>
/// <param name="MountPos">MD_Item.MountPos (FL/FR/RL/RR) — 우상 글자.</param>
/// <param name="ShiftLetter">생산 교대 A/B/C — part4M 마지막 글자.</param>
/// <param name="IssuedAt">발행 시각 — 인쇄 날짜(M/d/yyyy)와 T 토큰 앞 6자리(yyMMdd).</param>
public sealed record ImgLabel(
    string   LotCode,
    string   ItemNo,
    string?  CustomerCode,
    string?  Pgn,
    string?  Alc,
    string?  MountPos,
    string   ShiftLetter,
    DateTime IssuedAt);
