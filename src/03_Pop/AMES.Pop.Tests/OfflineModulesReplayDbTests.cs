using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Pop.Common;
using AMES.Pop.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>
/// 오프라인 안돈·QC 작업이 복구 뒤 원래 시각으로 DB 에 반영되는지(10-28). AMES_TEST_CONN 이 닿을 때만 돈다 — 쓰기라 로컬 DB 권장.
/// 시험 전용 라인 ID 를 써서 실제 안돈과 섞이지 않고 끝에 지운다.
/// </summary>
[Collection("OfflineAuth")]
public sealed class OfflineModulesReplayDbTests : IDisposable
{
    private const string Line = "ITEST-ANDON-OFF", QcItem = "ITEST-QC-OFF";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-modreplay-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;
    private readonly AmesConnectionFactory? _factory;

    private static readonly string Conn = Environment.GetEnvironmentVariable("AMES_TEST_CONN") ?? "";

    public OfflineModulesReplayDbTests()
    {
        OfflineStore.Dir = _dir;
        OfflineWork.ResetForTests();
        OfflineReads.ResetForTests();
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using (f.OpenConnection()) { }
            _factory = f;
            PopServices.Initialize(Conn);
            Clean();
        }
        catch { _factory = null; }
    }

    public void Dispose()
    {
        DbHealth.RecordSuccess();
        if (_factory is not null) Clean();
        OfflineStore.Dir = _origDir;
        OfflineWork.ResetForTests();
        OfflineReads.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private object? Scalar(string sql, params (string, object)[] ps)
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    private void Clean()
    {
        using var conn = _factory!.OpenConnection();
        using var cmd = new SqlCommand("""
            DECLARE @a TABLE (AndonID int);
            INSERT @a SELECT AndonID FROM dbo.PR_AndonCall WHERE LineID = @L;
            DECLARE @f TABLE (FailureID int, WorkOrderID int NULL);
            INSERT @f SELECT FailureID, WorkOrderID FROM dbo.MNT_FailureRegister
            WHERE  Source = 'ANDON' AND AndonRefID IN (SELECT CAST(AndonID AS varchar(24)) FROM @a);
            DELETE dbo.MNT_FailureAction WHERE FailureID IN (SELECT FailureID FROM @f);
            DELETE dbo.MNT_WorkOrder WHERE WorkOrderID IN (SELECT WorkOrderID FROM @f);
            DELETE dbo.MNT_FailureRegister WHERE FailureID IN (SELECT FailureID FROM @f);
            DELETE dbo.PR_AndonDeptCall WHERE AndonID IN (SELECT AndonID FROM @a);
            DELETE dbo.PP_LineDowntimeLog WHERE LineID = @L;
            DELETE dbo.PR_AndonCall WHERE AndonID IN (SELECT AndonID FROM @a);
            DELETE dbo.QC_Inspection WHERE ItemNo = @I;
            DELETE dbo.MD_Item WHERE ItemNo = @I AND CreatedBy = 'ITEST';
            """, conn);
        cmd.Parameters.AddWithValue("@L", Line);
        cmd.Parameters.AddWithValue("@I", QcItem);
        cmd.ExecuteNonQuery();
    }

    private sealed class NoNames : IBadgeResolver { public string? ResolveName(string workerNo) => null; }

    // 큐 항목의 발생 시각을 몇 분 전으로 벌린다 — 반영이 서버 시각이 아니라 이 시각을 쓰는지 본다
    private static List<DateTime> SpreadTimes(params int[] minutesAgo)
    {
        var file = OfflineStore.Load<WorkOpsFile>("work-ops.bin")!;
        var now = DateTime.Now;
        var times = minutesAgo.Select(m => new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(-m)).ToList();
        for (var i = 0; i < file.Ops.Count; i++) file.Ops[i].At = times[i];
        OfflineStore.Save("work-ops.bin", file);
        OfflineWork.ResetForTests();
        return times;
    }

    [SkippableFact]
    public void Offline_andon_replays_with_its_original_times_and_maintenance_records()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        DbHealth.RecordFailure();
        OfflineReads.Put("andon.causes", new List<AndonCauseDto> { new("MACHINE", "설비", null, "MAINT") });
        OfflineReads.Put("andon.depts", new List<AndonDeptDto> { new("MAINT", "보전", null) });
        OfflineReads.Put("andon.severities", new List<AndonSeverityDto> { new("MAJOR", "중", null) });

        var wf = new AndonWorkflow(new OfflineAwareAndonStore(new RepoAndonStore(), Line), new NoNames(), Line, null, "ITEST");
        wf.Raise();
        wf.OnBadgeScan("EOS*ITS01*반장");
        wf.SelectCause("MACHINE"); wf.SelectSeverity("MAJOR"); wf.CallDepts();
        wf.OnBadgeScan("EOS*ITM01*보전");
        wf.AckDept(wf.Call!.Depts.Single().DeptCallId);
        Assert.Equal(AndonUiState.Ready, wf.State);
        var t = SpreadTimes(30, 28, 27, 20, 10, 10);   // Raise·SupAck·CallDepts·Arrival·AckDept·Resolve

        DbHealth.RecordSuccess();
        OfflineWork.Replay();
        Assert.Equal(0, OfflineWork.PendingCount);
        Assert.Empty(OfflineWork.Conflicts);

        var id = (int)Scalar("SELECT AndonID FROM dbo.PR_AndonCall WHERE LineID = @L", ("@L", Line))!;
        Assert.Equal("RESOLVED", Scalar("SELECT Status FROM dbo.PR_AndonCall WHERE AndonID = @A", ("@A", id)));
        Assert.Equal(t[0], Scalar("SELECT TriggeredAt FROM dbo.PR_AndonCall WHERE AndonID = @A", ("@A", id)));
        Assert.Equal(t[1], Scalar("SELECT AckedAt FROM dbo.PR_AndonCall WHERE AndonID = @A", ("@A", id)));
        Assert.Equal(t[5], Scalar("SELECT ResumedAt FROM dbo.PR_AndonCall WHERE AndonID = @A", ("@A", id)));
        Assert.Equal($"{t[2]:yyyyMMddHHmm}|{t[3]:yyyyMMddHHmm}|{t[4]:yyyyMMddHHmm}|ITM01", Scalar("""
            SELECT CONCAT(FORMAT(CalledAt,'yyyyMMddHHmm'), '|', FORMAT(ArrivedAt,'yyyyMMddHHmm'), '|', FORMAT(AckedAt,'yyyyMMddHHmm'), '|', ArrivedNo)
            FROM dbo.PR_AndonDeptCall WHERE AndonID = @A
            """, ("@A", id)));
        Assert.Equal($"RESOLVED|{t[2]:yyyyMMddHHmm}|COMPLETED", Scalar("""
            SELECT CONCAT(f.Status, '|', FORMAT(f.ReportedAt,'yyyyMMddHHmm'), '|', w.Status)
            FROM dbo.MNT_FailureRegister f LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
            WHERE f.Source = 'ANDON' AND f.AndonRefID = CAST(@A AS varchar(24))
            """, ("@A", id)));
        Assert.Equal(20, Scalar("SELECT DurationMin FROM dbo.PP_LineDowntimeLog WHERE AndonID = @A", ("@A", id)));
        Assert.Null(OfflineAndon.GetOpenForLine(Line));
    }

    [SkippableFact]
    public void Offline_qc_inspection_is_created_and_finalized_at_its_original_times()
    {
        Skip.If(_factory is null, "AMES_DEV unreachable");
        using (var conn = _factory!.OpenConnection())
        using (var cmd = new SqlCommand("INSERT INTO dbo.MD_Item (ItemNo, ItemName, ActiveFlag, CreatedBy) VALUES (@I, N'ITEST QC offline', 1, 'ITEST')", conn))
        { cmd.Parameters.AddWithValue("@I", QcItem); cmd.ExecuteNonQuery(); }

        DbHealth.RecordFailure();
        var s = new PopSessionDto
        {
            SessionId = 0, OperatorId = "ITEST", EmployeeNo = "ITEST", EmployeeName = "t", TerminalId = "ST-QC", LineId = "LINE-QC-01",
            ShiftCode = "A", AuthMethod = AuthMethod.Pin, StartedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddHours(1),
        };
        var r = OfflineQc.StartInspection("Final", QcItem, 32, s)!;
        Assert.True(OfflineQc.FinalizeInspection(r.Id, "PASS", s));
        var t = SpreadTimes(40, 15);

        DbHealth.RecordSuccess();
        OfflineWork.Replay();
        Assert.Empty(OfflineWork.Conflicts);
        Assert.Equal($"PASS|{t[0]:yyyyMMddHHmm}|{t[1]:yyyyMMddHHmm}|LINE-QC-01", Scalar("""
            SELECT CONCAT(Verdict, '|', FORMAT(InsStartTS,'yyyyMMddHHmm'), '|', FORMAT(InsEndTS,'yyyyMMddHHmm'), '|', LineID)
            FROM dbo.QC_Inspection WHERE ItemNo = @I
            """, ("@I", QcItem)));
    }
}
