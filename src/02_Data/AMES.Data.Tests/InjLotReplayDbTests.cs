using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// 오프라인 샷 LOT 복구 재생(InjLotRepository.CreateRawLotAt·FindLotId·ReserveLotNos) — 로컬 AMES_DEV 에서만 쓴다.
/// 개발 서버에 쓰지 않는다 — AMES_TEST_CONN 으로 로컬 DB 를 지정해 돌린다.
/// </summary>
public class InjLotReplayDbTests
{
    static readonly DateTime FarDay = new(2036, 2, 9);   // 평소 채번과 섞이지 않는 먼 미래 날짜
    const string Line = "LINE-INJ-01";
    const string Equip = "INJ-01-01-1800";

    static MoldItemDto Lh() => new()
    { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "83335-P8000RBQ", MoldId = null };

    static void Cleanup(AmesConnectionFactory f, int lotId)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand("""
            DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
            DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
            """, conn);
        cmd.Parameters.AddWithValue("@L", lotId);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void CreateRawLotAt_keeps_the_offline_code_and_shot_time_and_FindLotId_finds_it()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var when = new DateTime(2036, 2, 9, 7, 31, 5);
        var code = repo.ReserveLotNos(Line, FarDay, 3).Codes.First();   // 온라인에서 받은 구간의 첫 번호
        Assert.Null(repo.FindLotId(code));

        var (lotId, lotCode) = repo.CreateRawLotAt(Line, Equip, Lh(), 777, code, when);
        try
        {
            Assert.Equal(code, lotCode);
            Assert.Equal(lotId, repo.FindLotId(code));

            using var conn = f!.OpenConnection();
            using var cmd = new SqlCommand("SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotID = @L;", conn);
            cmd.Parameters.AddWithValue("@L", lotId);
            Assert.Equal(when, (DateTime)cmd.ExecuteScalar()!);
        }
        finally { Cleanup(f!, lotId); }
    }

    [SkippableFact]
    public void ReserveLotNos_hands_out_non_overlapping_blocks()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var a = repo.ReserveLotNos(Line, FarDay.AddDays(1), 5);
        var b = repo.ReserveLotNos(Line, FarDay.AddDays(1), 5);

        Assert.Equal(5, a.Codes.Count());
        Assert.True(b.FirstSeq > a.FirstSeq + a.Count - 1);   // 두 번째 구간은 첫 구간 뒤에서 시작한다
    }
}
