using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// 외부 포탈 사용자(SCM_PortalVendorUser). AspNet 계정과 무관한 로그인 테이블이며 행 하나가 협력업체 하나에 묶인다.
/// UserID 는 이메일이고 소문자로 저장·비교한다. 비밀번호 해시는 호출자(Web)가 PasswordHasher 로 만든다.
/// </summary>
public sealed partial class ScmRepository
{
    /// <summary>로그인 실패가 이 횟수에 이르면 잠근다(내부 사용자 SYS_UserProfile 과 같은 기준).</summary>
    public const int PortalMaxFailedLogins = 5;

    public record PortalUserRow(string UserID, string VendorID, string VendorName, bool VendorActive,
        string? UserName, string PasswordHash, int FailedLoginCount, bool LockedFlag, DateTime? LastLoginTS,
        bool ActiveFlag, string CreatedBy, DateTime CreatedTS, string? ModifiedBy, DateTime? ModifiedTS);

    public static string NormalizePortalUserId(string email) => email.Trim().ToLowerInvariant();

    const string PortalUserSelect = """
        SELECT u.UserID, u.VendorID, COALESCE(v.VendorName, u.VendorID), ISNULL(v.ActiveFlag,1),
               u.UserName, u.PasswordHash, u.FailedLoginCount, u.LockedFlag, u.LastLoginTS,
               u.ActiveFlag, u.CreatedBy, u.CreatedTS, u.ModifiedBy, u.ModifiedTS
        FROM dbo.SCM_PortalVendorUser u LEFT JOIN dbo.MD_Vendor v ON v.VendorID=u.VendorID
        """;

    static PortalUserRow ReadPortalUser(SqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetBoolean(3),
        r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetBoolean(7),
        r.IsDBNull(8) ? null : r.GetDateTime(8), r.GetBoolean(9), r.GetString(10), r.GetDateTime(11),
        r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetDateTime(13));

    public List<PortalUserRow> ListPortalUsers()
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand(PortalUserSelect + " ORDER BY u.UserID;", conn);
        using var r = cmd.ExecuteReader();
        var rows = new List<PortalUserRow>();
        while (r.Read()) rows.Add(ReadPortalUser(r));
        return rows;
    }

    public PortalUserRow? FindPortalUser(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand(PortalUserSelect + " WHERE u.UserID=@Id;", conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadPortalUser(r) : null;
    }

    /// <summary>웹 사용자(AspNetUsers)가 같은 이메일·사용자명을 쓰는지 — 로그인 ID 는 두 곳을 합쳐 유일해야 한다.</summary>
    public bool EmailUsedByWebUser(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AspNetUsers WHERE NormalizedEmail=@N OR NormalizedUserName=@N", conn);
        Add(cmd, ("@N", email.Trim().ToUpperInvariant()));
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertPortalUser(string email, string vendorId, string? userName, string passwordHash, bool activeFlag, string actor)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT dbo.SCM_PortalVendorUser (UserID,VendorID,UserName,PasswordHash,FailedLoginCount,LockedFlag,ActiveFlag,CreatedBy,CreatedTS)
            VALUES (@Id,@Vendor,@Name,@Hash,0,0,@Active,@Actor,SYSDATETIME());
            """, conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)), ("@Vendor", vendorId), ("@Name", (object?)userName ?? DBNull.Value),
            ("@Hash", passwordHash), ("@Active", activeFlag), ("@Actor", actor));
        cmd.ExecuteNonQuery();
    }

    /// <summary>협력업체·이름·사용 여부 수정. passwordHash 가 있으면 비밀번호도 바꾸고 실패 수·잠금을 푼다.</summary>
    public void UpdatePortalUser(string email, string vendorId, string? userName, bool activeFlag, string? passwordHash, string actor)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.SCM_PortalVendorUser
            SET VendorID=@Vendor, UserName=@Name, ActiveFlag=@Active,
                PasswordHash=COALESCE(@Hash,PasswordHash),
                FailedLoginCount=CASE WHEN @Hash IS NULL THEN FailedLoginCount ELSE 0 END,
                LockedFlag=CASE WHEN @Hash IS NULL THEN LockedFlag ELSE 0 END,
                ModifiedBy=@Actor, ModifiedTS=SYSDATETIME()
            WHERE UserID=@Id;
            """, conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)), ("@Vendor", vendorId), ("@Name", (object?)userName ?? DBNull.Value),
            ("@Active", activeFlag), ("@Hash", (object?)passwordHash ?? DBNull.Value), ("@Actor", actor));
        cmd.ExecuteNonQuery();
    }

    public void UnlockPortalUser(string email, string actor)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("UPDATE dbo.SCM_PortalVendorUser SET FailedLoginCount=0, LockedFlag=0, ModifiedBy=@Actor, ModifiedTS=SYSDATETIME() WHERE UserID=@Id;", conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)), ("@Actor", actor));
        cmd.ExecuteNonQuery();
    }

    public void DeletePortalUser(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE dbo.SCM_PortalVendorUser WHERE UserID=@Id;", conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)));
        cmd.ExecuteNonQuery();
    }

    public void RecordPortalLoginSuccess(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("UPDATE dbo.SCM_PortalVendorUser SET FailedLoginCount=0, LastLoginTS=SYSDATETIME() WHERE UserID=@Id;", conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)));
        cmd.ExecuteNonQuery();
    }

    /// <returns>이번 실패로 잠겼으면 true.</returns>
    public bool RecordPortalLoginFailure(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.SCM_PortalVendorUser
            SET FailedLoginCount=FailedLoginCount+1,
                LockedFlag=CASE WHEN FailedLoginCount+1>=@Max THEN 1 ELSE LockedFlag END
            OUTPUT inserted.LockedFlag, deleted.LockedFlag
            WHERE UserID=@Id;
            """, conn);
        Add(cmd, ("@Id", NormalizePortalUserId(email)), ("@Max", PortalMaxFailedLogins));
        using var r = cmd.ExecuteReader();
        return r.Read() && r.GetBoolean(0) && !r.GetBoolean(1);
    }
}
