using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// MD_DefectCode(라인 불량 팝업 코드 버튼)·MD_DefectCause(REWORK 원인 버튼) 읽기 전용.
/// PR_DefectDetail 쓰기는 LotDefectWriter(등록)와 ReworkRepository(판정)가 트랜잭션 안에서 한다.
/// </summary>
public sealed class DefectRepository
{
    private readonly AmesConnectionFactory _factory;
    public DefectRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>The 6 (or however many) defect codes for the INJ process.</summary>
    public List<DefectCodeDto> ListForProcess(string processCode)
    {
        const string sql = """
            SELECT DefectCode, DefectName, DefectNameEn, ProcessCode,
                   SeverityLevel, DefaultCauseCode
            FROM   dbo.MD_DefectCode
            WHERE  ProcessCode = @P
              AND  ActiveFlag = 1
            ORDER  BY DefectCode;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 10).Value = processCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DefectCodeDto>();
        while (rdr.Read())
            list.Add(new DefectCodeDto
            {
                DefectCode       = (string)rdr["DefectCode"],
                DefectName       = rdr["DefectName"]      as string ?? string.Empty,
                DefectNameEn     = rdr["DefectNameEn"]    as string,
                ProcessCode      = rdr["ProcessCode"]     as string,
                SeverityLevel    = rdr["SeverityLevel"]   as string,
                DefaultCauseCode = rdr["DefaultCauseCode"] as string,
            });
        return list;
    }

    /// <summary>REWORK 원인 버튼: 그 공정의 활성 원인 + 공정 미지정(공통) 원인. SortOrder 순.</summary>
    public List<DefectCauseDto> ListCausesForProcess(string processCode)
    {
        const string sql = """
            SELECT CauseCode, CauseName, CauseNameEn, ProcessCode
            FROM   dbo.MD_DefectCause
            WHERE  ISNULL(ActiveFlag,1) = 1
              AND  (ProcessCode = @P OR ProcessCode IS NULL)
            ORDER  BY ISNULL(SortOrder,9999), CauseCode;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 10).Value = processCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DefectCauseDto>();
        while (rdr.Read())
            list.Add(new DefectCauseDto
            {
                CauseCode   = (string)rdr["CauseCode"],
                CauseName   = rdr["CauseName"]   as string ?? string.Empty,
                CauseNameEn = rdr["CauseNameEn"] as string,
                ProcessCode = rdr["ProcessCode"] as string,
            });
        return list;
    }
}
