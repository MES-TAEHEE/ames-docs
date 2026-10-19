using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — BOM 버전 헤더 SAPTransFlag: 감사 컬럼 바로 앞, 등록·DRAFT 수정(null = 그대로),
// 초안이 아닌 버전은 SAP 전송·변경 유형·사유만 함께 바꾸고, 어느 경우든 SAP 전송을 바꾸면 변경 사유를 새로 적어야 한다
public class BomVersionSapTransFlagTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item and BOM version";
    const string Actor = "itest-bomsap";
    const string Item  = "ITEST-BOMSAP";
    const string Ver   = "ITEST-BOMSAP-V";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bom        WHERE VersionID = @V;
        DELETE dbo.MD_BomVersion WHERE VersionID = @V;
        DELETE dbo.MD_Item       WHERE ItemNo = @I;
        """, ("@V", Ver), ("@I", Item));

    static bool Flag(MasterDataRepository md) => md.ListBomVersions().Single(v => v.VersionID == Ver).SAPTransFlag;

    static bool Update(MasterDataRepository md, bool? flag, string? reason = null) =>
        md.UpdateBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, null, reason, Actor, flag);

    static string? Reason(Connection.AmesConnectionFactory f) =>
        Scalar(f, "SELECT ChangeReason FROM dbo.MD_BomVersion WHERE VersionID = @V", ("@V", Ver)) as string;

    [Theory]
    [InlineData(false, false, "a", "a", false)]   // 바꾸지 않으면 사유 무관
    [InlineData(false, true, null, null, true)]   // 바꾸는데 사유 없음
    [InlineData(false, true, null, "  ", true)]
    [InlineData(false, true, "INITIAL", " initial ", true)]   // 저장된 사유 그대로(공백·대소문자 무시)
    [InlineData(true, false, "INITIAL", "SAP 전송 중지", false)]
    public void Sap_change_needs_a_new_reason(bool stored, bool next, string? storedReason, string? newReason, bool missing) =>
        Assert.Equal(missing, AMES.Data.Services.BomVersionRules.SapChangeMissingReason(stored, next, storedReason, newReason));

    [SkippableFact]
    public void Flag_sits_right_before_the_audit_columns()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var cols = Scalar(f!, """
            SELECT STRING_AGG(CAST(c.name AS nvarchar(max)), ',') WITHIN GROUP (ORDER BY c.column_id)
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'dbo.MD_BomVersion')
              AND c.column_id >= COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_BomVersion'), N'Status', 'ColumnId')
            """) as string;
        Assert.Equal("Status,SAPTransFlag,CreatedBy,CreatedTS,ModifiedBy,ModifiedTS", cols);
    }

    [SkippableFact]
    public void Flag_roundtrips_and_can_change_after_approval()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            md.InsertItem(Item, "itest bom sap", "ASSY", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            md.InsertBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor);
            Assert.False(Flag(md));                                   // 기본 0

            Assert.Throws<MasterDataRepository.BomSapReasonRequiredException>(() => Update(md, true)); // 사유 없이 SAP 전송 변경 거부
            Assert.False(Flag(md));
            Assert.True(Update(md, true, "SAP 전송 시작"));
            Assert.True(Flag(md));
            Assert.True(Update(md, null, "SAP 전송 시작"));             // 값을 넘기지 않으면 그대로
            Assert.True(Update(md, true, "SAP 전송 시작"));             // 바꾸지 않으면 사유 검사 없음
            Assert.True(Flag(md));

            Exec(f!, "UPDATE dbo.MD_BomVersion SET Status = 'APPROVED' WHERE VersionID = @V", ("@V", Ver));
            Assert.False(Update(md, false, "다른 사유"));                   // 승인 뒤 버전 수정은 막힌다
            Assert.True(Flag(md));
            Assert.Throws<MasterDataRepository.BomSapReasonRequiredException>(
                () => md.ChangeBomVersionSapTransFlag(Ver, false, "ECO", " sap 전송 시작 ", Actor));   // 사유를 그대로 두면 거부
            Assert.True(Flag(md));
            Assert.True(md.ChangeBomVersionSapTransFlag(Ver, false, "ECO", "SAP 전송 중지", Actor));
            Assert.False(Flag(md));
            Assert.Equal("SAP 전송 중지", Reason(f!));
            Assert.Equal(Actor, Scalar(f!, "SELECT ModifiedBy FROM dbo.MD_BomVersion WHERE VersionID = @V", ("@V", Ver)) as string);
            Assert.False(md.ChangeBomVersionSapTransFlag(Ver, false, "ECO", "또 중지", Actor));   // 이미 그 값 — 변경 없음
            Assert.Equal("SAP 전송 중지", Reason(f!));

            Assert.False(md.ChangeBomVersionSapTransFlag(Ver + "-NONE", true, null, "x", Actor));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Insert_can_start_as_a_sap_target()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            md.InsertItem(Item, "itest bom sap", "ASSY", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            md.InsertBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor, sapTransFlag: true);
            Assert.True(Flag(md));
        }
        finally { Cleanup(f!); }
    }
}
