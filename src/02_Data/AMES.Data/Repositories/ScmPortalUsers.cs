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
        Filter(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadPortalUser(r) : null;
    }

    /// <summary>웹 사용자(AspNetUsers)가 같은 이메일·사용자명을 쓰는지 — 로그인 ID 는 두 곳을 합쳐 유일해야 한다.</summary>
    public bool EmailUsedByWebUser(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AspNetUsers WHERE NormalizedEmail=@N OR NormalizedUserName=@N", conn);
        Filter(cmd, ("@N", "AspNetUsers.NormalizedEmail", email.Trim().ToUpperInvariant()));
        return (int)cmd.ExecuteScalar()! > 0;
    }

    public void InsertPortalUser(string email, string vendorId, string? userName, string passwordHash, bool activeFlag, string actor)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            INSERT dbo.SCM_PortalVendorUser (UserID,VendorID,UserName,PasswordHash,FailedLoginCount,LockedFlag,ActiveFlag,CreatedBy,CreatedTS)
            SELECT @Id,@Vendor,@Name,@Hash,0,0,@Active,@Actor,SYSDATETIME()
            FROM dbo.MD_Vendor WHERE VendorID=@Vendor AND ISNULL(ActiveFlag,1)=1;
            IF @@ROWCOUNT<>1 THROW 50021,'An active supplier is required. Refresh and retry.',1;
            """, conn);
        Add(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)), ("@Vendor", "SCM_PortalVendorUser.VendorID", vendorId), ("@Name", "SCM_PortalVendorUser.UserName", (object?)userName ?? DBNull.Value),
            ("@Hash", "SCM_PortalVendorUser.PasswordHash", passwordHash), ("@Active", "SCM_PortalVendorUser.ActiveFlag", activeFlag), ("@Actor", "SCM_PortalVendorUser.CreatedBy", actor));
        cmd.ExecuteNonQuery();
    }

    /// <summary>협력업체·이름·사용 여부 수정. passwordHash 가 있으면 비밀번호도 바꾸고 실패 수·잠금을 푼다.</summary>
    public void UpdatePortalUser(string email, string vendorId, string? userName, bool activeFlag, string? passwordHash, string actor, PortalUserRow expected)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.SCM_PortalVendorUser
            SET VendorID=@Vendor, UserName=@Name, ActiveFlag=@Active,
                PasswordHash=COALESCE(@Hash,PasswordHash),
                FailedLoginCount=CASE WHEN @Hash IS NULL THEN FailedLoginCount ELSE 0 END,
                LockedFlag=CASE WHEN @Hash IS NULL THEN LockedFlag ELSE 0 END,
                ModifiedBy=@Actor, ModifiedTS=SYSDATETIME()
            WHERE UserID=@Id AND CreatedTS=@Created AND (ModifiedTS=@Modified OR ModifiedTS IS NULL AND @Modified IS NULL)
                AND PasswordHash=@ExpectedHash AND FailedLoginCount=@Failed AND LockedFlag=@Locked
                AND (VendorID=@Vendor OR EXISTS(SELECT 1 FROM dbo.MD_Vendor WHERE VendorID=@Vendor AND ISNULL(ActiveFlag,1)=1));
            """, conn);
        AddExpectedPortalUser(cmd,expected);
        Add(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)), ("@Vendor", "SCM_PortalVendorUser.VendorID", vendorId), ("@Name", "SCM_PortalVendorUser.UserName", (object?)userName ?? DBNull.Value),
            ("@Active", "SCM_PortalVendorUser.ActiveFlag", activeFlag), ("@Hash", "SCM_PortalVendorUser.PasswordHash", (object?)passwordHash ?? DBNull.Value), ("@Actor", "SCM_PortalVendorUser.ModifiedBy", actor));
        EnsurePortalUserChanged(cmd);
    }

    /// <summary>외부 사용자 본인의 비밀번호 변경(포탈 상단바, 10-07) — 해시만 바꾼다. 잠금·실패 횟수는 SCM-004 관리 영역이라 건드리지 않는다.</summary>
    public void ChangePortalPassword(string email, string passwordHash, string actor)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("UPDATE dbo.SCM_PortalVendorUser SET PasswordHash=@Hash, ModifiedBy=@Actor, ModifiedTS=SYSDATETIME() WHERE UserID=@Id;", conn);
        Add(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)), ("@Hash", "SCM_PortalVendorUser.PasswordHash", passwordHash), ("@Actor", "SCM_PortalVendorUser.ModifiedBy", actor));
        cmd.ExecuteNonQuery();
    }

    public void UnlockPortalUser(string email, string actor, PortalUserRow expected)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("UPDATE dbo.SCM_PortalVendorUser SET FailedLoginCount=0, LockedFlag=0, ModifiedBy=@Actor, ModifiedTS=SYSDATETIME() WHERE UserID=@Id AND CreatedTS=@Created AND (ModifiedTS=@Modified OR ModifiedTS IS NULL AND @Modified IS NULL) AND PasswordHash=@ExpectedHash AND FailedLoginCount=@Failed AND LockedFlag=@Locked;", conn);
        Add(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)), ("@Actor", "SCM_PortalVendorUser.ModifiedBy", actor));
        AddExpectedPortalUser(cmd,expected);
        EnsurePortalUserChanged(cmd);
    }

    public void DeletePortalUser(string email, PortalUserRow expected)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("DELETE dbo.SCM_PortalVendorUser WHERE UserID=@Id AND CreatedTS=@Created AND (ModifiedTS=@Modified OR ModifiedTS IS NULL AND @Modified IS NULL) AND PasswordHash=@ExpectedHash AND FailedLoginCount=@Failed AND LockedFlag=@Locked;", conn);
        Filter(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)));
        AddExpectedPortalUser(cmd,expected);
        EnsurePortalUserChanged(cmd);
    }

    static void AddExpectedPortalUser(SqlCommand cmd,PortalUserRow expected)
    {
        Filter(cmd,("@Created","SCM_PortalVendorUser.CreatedTS",expected.CreatedTS),
            ("@Modified","SCM_PortalVendorUser.ModifiedTS",(object?)expected.ModifiedTS ?? DBNull.Value),
            ("@ExpectedHash","SCM_PortalVendorUser.PasswordHash",expected.PasswordHash),
            ("@Failed","SCM_PortalVendorUser.FailedLoginCount",expected.FailedLoginCount),
            ("@Locked","SCM_PortalVendorUser.LockedFlag",expected.LockedFlag));
    }
    static void EnsurePortalUserChanged(SqlCommand cmd)
    {
        if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("Account changed or was deleted. Close and refresh before retrying.");
    }

    public void RecordPortalLoginSuccess(string email)
    {
        using var conn = factory.OpenConnection();
        using var cmd = new SqlCommand("UPDATE dbo.SCM_PortalVendorUser SET FailedLoginCount=0, LastLoginTS=SYSDATETIME() WHERE UserID=@Id;", conn);
        Filter(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)));
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
        Filter(cmd, ("@Id", "SCM_PortalVendorUser.UserID", NormalizePortalUserId(email)), ("@Max", null, PortalMaxFailedLogins));
        using var r = cmd.ExecuteReader();
        return r.Read() && r.GetBoolean(0) && !r.GetBoolean(1);
    }
}
