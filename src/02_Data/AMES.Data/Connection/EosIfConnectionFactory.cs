using Microsoft.Data.SqlClient;

namespace AMES.Data.Connection;

/// <summary>
/// SAP B1 인터페이스 DB(EOS_IF, 웅진 소유)에 여는 연결. 접속문자열은 환경변수 <c>ConnectionStrings__EOS_IF</c> 로만 받는다 —
/// 소스·appsettings·공통코드에 두지 않는다. 비어 있으면 <see cref="IsConfigured"/> 가 false 이고 Worker 는 돌지 않는다.
/// AMES DB 와 다른 서버라 <see cref="DbHealth"/> 를 건드리지 않는다(EOS_IF 장애가 AMES 화면의 DB 장애 배너로 보이면 안 된다).
/// </summary>
public sealed class EosIfConnectionFactory(string? connectionString)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(connectionString);

    public SqlConnection OpenConnection()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("EOS_IF 접속문자열이 없습니다. 환경변수 ConnectionStrings__EOS_IF 를 설정하세요.");
        var conn = new SqlConnection(connectionString);
        try { conn.Open(); }
        catch { conn.Dispose(); throw; }
        return conn;
    }

    /// <summary>시작 로그용 — 비밀번호 없이 서버·DB·계정만.</summary>
    public string Describe()
    {
        if (!IsConfigured) return "EOS_IF: 미설정";
        var b = new SqlConnectionStringBuilder(connectionString);
        return $"EOS_IF: Server={b.DataSource}, Database={b.InitialCatalog}, User={b.UserID}";
    }
}
