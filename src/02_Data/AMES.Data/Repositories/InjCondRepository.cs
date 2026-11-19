using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>사출조건 항목 마스터(MD_InjCondItem) 조회 + 샷별 이력(PR_InjCondLog) 적재.</summary>
public sealed class InjCondRepository
{
    private readonly AmesConnectionFactory _factory;
    public InjCondRepository(AmesConnectionFactory f) => _factory = f;

    public List<InjCondItemDto> GetItems(string lineId)
    {
        const string sql = """
            SELECT ItemCode, ItemName, SetAddress, ActualAddress, DataType
            FROM   dbo.MD_InjCondItem
            WHERE  LineID = @Line AND Enabled = 1
            ORDER  BY ItemCode;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjCondItemDto>();
        while (rdr.Read())
            list.Add(new InjCondItemDto
            {
                ItemCode      = (string)rdr["ItemCode"],
                ItemName      = rdr["ItemName"] as string,
                SetAddress    = rdr["SetAddress"]    as int?,
                ActualAddress = rdr["ActualAddress"] as int?,
                DataType      = (string)rdr["DataType"],
            });
        return list;
    }

    /// <summary>사출 조건 로그 한 건. stationCode 는 PLC 브리지가 알 때만 — 없으면 라인에 스테이션이 하나뿐일 때 그 코드(FN_StationOfLine).</summary>
    public void InsertLog(string lineId, string itemCode, long shotSeq, decimal? setValue, decimal? actualValue, string? stationCode = null)
    {
        const string sql = """
            INSERT INTO dbo.PR_InjCondLog
                (LineID, ItemCode, ShotSeq, SetValue, ActualValue, CollectedAt, CreatedBy, CreatedTS, StationCode)
            VALUES
                (@Line, @Item, @Seq, @Set, @Act, SYSDATETIME(), 'AGENT', SYSDATETIME(), COALESCE(@Station, dbo.FN_StationOfLine(@Line)));
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20 ).Value = lineId;
        cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20 ).Value = itemCode;
        cmd.Parameters.Add("@Seq",  SqlDbType.BigInt      ).Value = shotSeq;
        cmd.Parameters.Add("@Set",  SqlDbType.Decimal     ).Value = (object?)setValue ?? DBNull.Value;
        cmd.Parameters.Add("@Act",  SqlDbType.Decimal     ).Value = (object?)actualValue ?? DBNull.Value;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = (object?)stationCode ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
