using System.Transactions;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 공용 헬퍼로 저장하는 MD 20·SYS 19 메서드 왕복(10-12) — 컬럼 최대 길이 값(varchar 'X', nvarchar '가')을 저장했다가 그대로 읽히는지,
// 한 글자 넘치면 저장 전에 DbValueException 인지 본다. 한 트랜잭션(TransactionScope) 안에서 돌리고 끝에 되돌린다.
// 되돌리더라도 쓰기라서 AMES_TEST_CONN 으로 DB 를 지정했을 때만 돈다(로컬 DB 권장 — IDENTITY 번호는 소모된다).
public class TypedSaveRoundTripTests
{
    static SqlConnection Open() { var c = new SqlConnection(Conn); c.Open(); return c; }

    // 한 트랜잭션 안에서 연결을 동시에 둘 열면 분산 트랜잭션으로 승격되므로 도우미마다 열고 닫는다(같은 연결을 이어 쓴다)
    static ColumnParams.ColumnInfo Info(string col) { using var c = Open(); return ColumnParams.Info(c, col); }
    static int Len(string col) => Info(col).MaxLength is > 0 and var n ? n : 300;
    static bool Unicode(string col) => Info(col).DataType.StartsWith("n", StringComparison.OrdinalIgnoreCase);
    /// <summary>그 컬럼의 최대 길이 값 — varchar 는 ch(기본 X), nvarchar 는 uni(기본 가).</summary>
    static string Max(string col, char ch = 'X', char uni = '가') => new(Unicode(col) ? uni : ch, Len(col));
    static string Over(string col) => Max(col) + (Unicode(col) ? "가" : "X");
    static void Exec(string sql) { using var c = Open(); new SqlCommand(sql, c).ExecuteNonQuery(); }

    static void Same(string table, string where, object key, params (string Col, object? Expected)[] cols)
    {
        using var c = Open();
        using var cmd = new SqlCommand($"SELECT {string.Join(",", cols.Select(x => x.Col))} FROM dbo.{table} WHERE {where} = @K", c);
        cmd.Parameters.AddWithValue("@K", key);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read(), $"{table} row {key} not found");
        for (var i = 0; i < cols.Length; i++)
        {
            var actual = r.IsDBNull(i) ? null : r.GetValue(i);
            var exp = cols[i].Expected;
            if (exp is DateTime d && actual is DateTime a) { Assert.Equal(d, a); continue; }
            Assert.True(Equals(exp, actual) || (exp is decimal de && actual is decimal ad && de == ad),
                $"{table}.{cols[i].Col}: expected <{exp}> actual <{actual}>");
        }
    }

    static int Scalar(string sql, params (string, object)[] ps)
    {
        using var c = Open();
        using var cmd = new SqlCommand(sql, c);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void TooLong(Action a) => Assert.Equal(DbValueProblem.TooLong, Assert.Throws<DbValueException>(a).Problem);

    static TransactionScope Scope() => new(TransactionScopeOption.RequiresNew,
        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TimeSpan.FromMinutes(2) });

    [SkippableFact]
    public void Master_data_saves_round_trip_at_column_limits()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN (local DB) to run — writes inside a rolled-back transaction");
        var f = new AmesConnectionFactory(Conn);
        var md = new MasterDataRepository(f);
        using var scope = Scope();
        var by = Max("MD_Item.CreatedBy");

        // 공통코드 그룹·항목
        var grp = "ITQ" + new string('G', Len("MD_CodeGroup.GroupCode") - 3);
        md.InsertCodeGroup(grp, Max("MD_CodeGroup.GroupName"), Max("MD_CodeGroup.GroupNameEn", 'Y'), Max("MD_CodeGroup.Description"), true, by);
        Same("MD_CodeGroup", "GroupCode", grp, ("GroupName", Max("MD_CodeGroup.GroupName")), ("Description", Max("MD_CodeGroup.Description")), ("CreatedBy", by));
        md.UpdateCodeGroup(grp, Max("MD_CodeGroup.GroupName", uni: '나'), null, Max("MD_CodeGroup.Description", uni: '나'), false, by);
        Same("MD_CodeGroup", "GroupCode", grp, ("GroupName", Max("MD_CodeGroup.GroupName", uni: '나')), ("UseFlag", false), ("ModifiedBy", by));
        TooLong(() => md.UpdateCodeGroup(grp, Over("MD_CodeGroup.GroupName"), null, null, true, by));
        Exec($"INSERT dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CreatedBy) VALUES ('{grp}_A', '{grp}', 'A', 'itest')");
        md.DeleteCodeItem(grp + "_A");
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.MD_CodeItem WHERE CodeID = @I", ("@I", grp + "_A")));

        // 품목
        var item = "ITQ" + new string('I', Len("MD_Item.ItemNo") - 3);
        var minScale = Info("MD_Item.MinStock").Scale ?? 0;
        md.InsertItem(item, Max("MD_Item.ItemName"), "SUB", Max("MD_Item.ItemCategory"), true, Max("MD_Item.CarType"), Max("MD_Item.DefaultUOM"),
            Max("MD_Item.RoutingType"), 1.23456789m, 2m, 3m, 4.5m, Max("MD_Item.PGN"), Max("MD_Item.ALC"), Max("MD_Item.DrawingNo"),
            10, 20, false, true, by, true);
        Same("MD_Item", "ItemNo", item, ("ItemName", Max("MD_Item.ItemName")), ("DrawingNo", Max("MD_Item.DrawingNo")), ("PGN", Max("MD_Item.PGN")),
            ("MinStock", Math.Round(1.23456789m, minScale, MidpointRounding.AwayFromZero)), ("InjFlag", true), ("ScanRequired", true), ("PalletQty", 10));
        // varchar 바이트 검사는 컬럼 콜레이션 코드 페이지 기준(10-29) — Korean_Wansung 은 한글 2바이트, SQL_Latin1 은 é 1바이트·한글 거부
        var korean = Info("MD_Item.DrawingNo").CodePage == 949;
        var drawing = korean ? new string('가', Len("MD_Item.DrawingNo") / 2) : new string('é', Len("MD_Item.DrawingNo"));
        var drawingOver = korean ? new string('가', Len("MD_Item.DrawingNo") / 2 + 1) : new string('é', Len("MD_Item.DrawingNo") + 1);
        md.UpdateItem(item, Max("MD_Item.ItemName", uni: '나'), "SUB", null, false, null, null, null, null, null, null, null, null, null,
            drawing, null, null, true, false, by);
        Same("MD_Item", "ItemNo", item, ("ItemName", Max("MD_Item.ItemName", uni: '나')), ("DrawingNo", drawing), ("ToteFlag", true));
        Assert.True(Assert.Throws<DbValueException>(() => md.UpdateItem(item, "x", "SUB", null, false, null, null, null, null, null, null, null, null, null,
            drawingOver, null, null, false, true, by)).BytesUnit);
        if (!korean)
            Assert.Equal(DbValueProblem.Unencodable, Assert.Throws<DbValueException>(() => md.UpdateItem(item, "x", "SUB", null, false, null, null, null, null, null, null, null, null, null,
                "가", null, null, false, true, by)).Problem);
        Assert.Equal(DbValueProblem.Unencodable, Assert.Throws<DbValueException>(() => md.UpdateItem(item, "x", "SUB", null, false, null, null, null, null, null, null, null, null, null,
            "A😀", null, null, false, true, by)).Problem);

        // BOM 버전·라인
        var ver = "ITQ" + new string('V', Len("MD_BomVersion.VersionID") - 3);
        md.InsertBomVersion(ver, item, Max("MD_BomVersion.VersionNo"), new DateOnly(2099, 1, 1), new DateOnly(2099, 12, 31),
            Max("MD_BomVersion.ChangeType"), Max("MD_BomVersion.ChangeReason"), by);
        Same("MD_BomVersion", "VersionID", ver, ("VersionNo", Max("MD_BomVersion.VersionNo")), ("ChangeReason", Max("MD_BomVersion.ChangeReason")),
            ("EffFrom", new DateTime(2099, 1, 1)), ("Status", "DRAFT"));
        Assert.True(md.UpdateBomVersion(ver, item, "V1.0", null, null, null, Max("MD_BomVersion.ChangeReason", uni: '나'), by));
        Same("MD_BomVersion", "VersionID", ver, ("ChangeReason", Max("MD_BomVersion.ChangeReason", uni: '나')), ("EffFrom", null));
        var bom = "ITQ" + new string('B', Len("MD_Bom.BOMID") - 3);
        var qtyScale = Info("MD_Bom.QtyPer").Scale ?? 0;
        Assert.True(md.InsertBomLine(bom, item, item, 1, 1.123456789m, 0.5m, ver, 10, Max("MD_Bom.Note"), by));
        Same("MD_Bom", "BOMID", bom, ("QtyPer", Math.Round(1.123456789m, qtyScale, MidpointRounding.AwayFromZero)), ("Note", Max("MD_Bom.Note")), ("UOM", null));   // 단위 = 구성품 기본 단위(위에서 비움, 10-23)
        Assert.True(md.UpdateBomLine(bom, item, item, 2, 3m, null, 20, Max("MD_Bom.Note", uni: '나'), by));
        Same("MD_Bom", "BOMID", bom, ("BOMLevel", 2), ("Note", Max("MD_Bom.Note", uni: '나')), ("ModifiedBy", by));
        Assert.True(md.DeleteBomLine(bom, by));
        Same("MD_Bom", "BOMID", bom, ("ActiveFlag", false));
        Assert.True(md.ReactivateBomLine(bom, by));
        Same("MD_Bom", "BOMID", bom, ("ActiveFlag", true));
        Assert.True(md.RequestBomApproval(ver, by));
        Same("MD_BomVersion", "VersionID", ver, ("Status", "PENDING"), ("RequestedBy", by));
        Assert.True(md.RejectBomVersion(ver, by));
        Same("MD_BomVersion", "VersionID", ver, ("Status", "REJECTED"));
        Assert.True(md.ReviveBomVersion(ver, by));
        Same("MD_BomVersion", "VersionID", ver, ("Status", "DRAFT"));

        // BOP·라우팅
        var bop = "ITQ" + new string('P', Len("MD_Bop.BOPID") - 3);
        md.InsertBopStep(bop, item, Max("MD_Bop.RoutingType"), 999, Max("MD_Bop.StationCode"), 12.5m, 1.25m, true, Max("MD_Bop.StepDescription"), true, by);
        Same("MD_Bop", "BOPID", bop, ("StationCode", Max("MD_Bop.StationCode")), ("StepDescription", Max("MD_Bop.StepDescription")), ("StepSeq", 999));
        md.UpdateBopStep(bop, null, 998, null, null, null, false, Max("MD_Bop.StepDescription", uni: '나'), false, by);
        Same("MD_Bop", "BOPID", bop, ("StepDescription", Max("MD_Bop.StepDescription", uni: '나')), ("ActiveFlag", false));
        TooLong(() => md.UpdateBopStep(bop, null, 998, null, null, null, false, Over("MD_Bop.StepDescription"), false, by));
        md.DeleteBopStep(bop);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.MD_Bop WHERE BOPID = @I", ("@I", bop)));
        md.InsertRoutingStep("Z", 999, Max("MD_RoutingStep.ProcessCode"), true, true, by);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM dbo.MD_RoutingStep WHERE RoutingType='Z' AND StepSeq=999 AND ProcessCode=@P", ("@P", Max("MD_RoutingStep.ProcessCode"))));
        md.UpdateRoutingStep("Z", 999, "INJ", false, false, by);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM dbo.MD_RoutingStep WHERE RoutingType='Z' AND StepSeq=999 AND ProcessCode='INJ' AND ActiveFlag=0", ("@x", 0)));
        TooLong(() => md.InsertRoutingStep("ZZ", 1, "INJ", false, true, by));   // char(1)
        md.DeleteRoutingStep("Z", 999);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.MD_RoutingStep WHERE RoutingType='Z' AND StepSeq=999", ("@x", 0)));

        // 구매 자재 단가(낙관적 비교 — 읽어 온 단가와 같을 때만)
        var mat = "ITQ" + new string('M', Len("MD_Item.ItemNo") - 3);
        md.InsertItem(mat, "itest", "MATERIAL", null, false, null, "EA", null, null, null, null, 1.25m, null, null, null, null, null, false, true, by);
        Assert.True(md.UpdateMaterialUnitCost(mat, 9.5m, 1.25m, by));
        Assert.False(md.UpdateMaterialUnitCost(mat, 3m, 1.25m, by));       // 그사이 바뀐 단가 — 변경 없음
        Same("MD_Item", "ItemNo", mat, ("UnitCost", 9.50m), ("ModifiedBy", by));
        // scope.Complete() 를 부르지 않는다 — 전부 되돌린다
    }

    [SkippableFact]
    public void System_saves_round_trip_at_column_limits()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN (local DB) to run — writes inside a rolled-back transaction");
        var sys = new SysRepository(new AmesConnectionFactory(Conn));
        using var scope = Scope();
        var by = Max("SYS_UserProfile.CreatedBy");

        // 프로필·PIN
        var uid = "ITQ-" + Guid.NewGuid().ToString("N");
        sys.CreateProfile(uid, Max("SYS_UserProfile.EmployeeNo"), Max("SYS_UserProfile.EmployeeName"), Max("SYS_UserProfile.Department"),
            Max("SYS_UserProfile.PlantCode"), Max("SYS_UserProfile.DefaultShift"), by, Max("SYS_UserProfile.AccountStatus"));
        Same("SYS_UserProfile", "UserID", uid, ("EmployeeNo", Max("SYS_UserProfile.EmployeeNo")), ("EmployeeName", Max("SYS_UserProfile.EmployeeName")),
            ("Department", Max("SYS_UserProfile.Department")), ("AccountStatus", Max("SYS_UserProfile.AccountStatus")));
        TooLong(() => sys.CreateProfile(uid + "b", Over("SYS_UserProfile.EmployeeNo"), "x", null, null, null, by));
        sys.SetPin(uid, Max("SYS_UserProfile.PinHash"), by);
        Same("SYS_UserProfile", "UserID", uid, ("PinHash", Max("SYS_UserProfile.PinHash")), ("ModifiedBy", by));
        var uid2 = "ITQ-" + Guid.NewGuid().ToString("N");
        sys.EnsureActiveProfile(uid2, Max("SYS_UserProfile.EmployeeName", uni: '나'), by);
        Same("SYS_UserProfile", "UserID", uid2, ("EmployeeName", Max("SYS_UserProfile.EmployeeName", uni: '나')), ("AccountStatus", "ACTIVE"));

        // 역할 권한
        var screen = "ITQ" + new string('S', Len("SYS_RolePermission.ScreenCode") - 3);
        sys.CreateRolePerm("ITQ-ROLE", Max("SYS_RolePermission.RoleName"), Max("SYS_RolePermission.ModuleCode"), screen, Max("SYS_RolePermission.PermissionLevel"), false, by);
        var rpId = Scalar("SELECT RolePermissionID FROM dbo.SYS_RolePermission WHERE RoleID='ITQ-ROLE' AND ScreenCode=@S", ("@S", screen));
        Same("SYS_RolePermission", "RolePermissionID", rpId, ("RoleName", Max("SYS_RolePermission.RoleName")), ("ModuleCode", Max("SYS_RolePermission.ModuleCode")));
        sys.UpdateRolePerm(rpId, "ITQ-ROLE", "x", "WEB", screen, "R__", true, by);
        Same("SYS_RolePermission", "RolePermissionID", rpId, ("PermissionLevel", "R__"), ("IsSystemRole", true), ("ModifiedBy", by));
        TooLong(() => sys.UpdateRolePerm(rpId, "ITQ-ROLE", Over("SYS_RolePermission.RoleName"), "WEB", screen, "R__", true, by));
        sys.DeleteRolePerm(rpId);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.SYS_RolePermission WHERE RolePermissionID = @I", ("@I", rpId)));

        // 공장 달력(먼 미래 날짜에 시험 행을 넣고 고친다)
        var day = new DateTime(2099, 12, 31);
        var calId = Scalar("INSERT dbo.SYS_FactoryCalendar (CalendarDate, DayType, CreatedBy) VALUES (@D, 'WORKDAY', 'itest'); SELECT CAST(SCOPE_IDENTITY() AS int)", ("@D", day));
        sys.UpdateCalendarDayMeta(day, Max("SYS_FactoryCalendar.DayType"), Max("SYS_FactoryCalendar.HolidayName"), by);
        Same("SYS_FactoryCalendar", "FactoryCalendarID", calId, ("DayType", Max("SYS_FactoryCalendar.DayType")), ("HolidayName", Max("SYS_FactoryCalendar.HolidayName")));
        sys.UpdateCalendarShift(calId, new TimeSpan(7, 30, 0), new TimeSpan(16, 30, 0), 65, 7.926m, by);
        Same("SYS_FactoryCalendar", "FactoryCalendarID", calId, ("StartTime", new TimeSpan(7, 30, 0)), ("BreakMinutes", 65), ("NetWorkHours", 7.93m));
        Assert.Equal(DbValueProblem.OutOfRange, Assert.Throws<DbValueException>(() => sys.UpdateCalendarShift(calId, null, null, null, 123.45m, by)).Problem);
        sys.DeleteCalendarShift(calId);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.SYS_FactoryCalendar WHERE FactoryCalendarID = @I", ("@I", calId)));
        Scalar("INSERT dbo.SYS_FactoryCalendar (CalendarDate, DayType, CreatedBy) VALUES (@D, 'WORKDAY', 'itest'); SELECT 0", ("@D", day));
        sys.DeleteCalendarDate(day);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = @D", ("@D", day)));

        // 알림 규칙·채널
        var ev = "ITQ" + new string('E', Len("SYS_NotificationRule.EventTypeCode") - 3);
        sys.InsertNotificationRule(ev, Max("SYS_NotificationRule.EventName"), Max("SYS_NotificationRule.ModuleCode"), Max("SYS_NotificationRule.ProcessCode"),
            Max("SYS_NotificationRule.TriggerCondition"), true, "[\"PUSH\"]", "[\"ROLE-SYSADMIN\"]", by);
        var ruleId = Scalar("SELECT NotificationRuleID FROM dbo.SYS_NotificationRule WHERE EventTypeCode = @E", ("@E", ev));
        Same("SYS_NotificationRule", "NotificationRuleID", ruleId, ("EventName", Max("SYS_NotificationRule.EventName")), ("TriggerCondition", Max("SYS_NotificationRule.TriggerCondition")));
        sys.UpdateNotificationRule(ruleId, ev, "x", null, null, Max("SYS_NotificationRule.TriggerCondition", uni: '나'), false, null, null, by);
        Same("SYS_NotificationRule", "NotificationRuleID", ruleId, ("TriggerCondition", Max("SYS_NotificationRule.TriggerCondition", uni: '나')), ("IsEnabled", false));
        TooLong(() => sys.UpdateNotificationRule(ruleId, Over("SYS_NotificationRule.EventTypeCode"), "x", null, null, null, false, null, null, by));
        sys.DeleteNotificationRule(ruleId);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.SYS_NotificationRule WHERE NotificationRuleID = @I", ("@I", ruleId)));
        sys.InsertNotificationChannel(uid, Max("SYS_NotificationChannel.Channel"), Max("SYS_NotificationChannel.Address"), true, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), by);
        var chId = Scalar("SELECT NotificationChannelID FROM dbo.SYS_NotificationChannel WHERE UserID = @U", ("@U", uid));
        Same("SYS_NotificationChannel", "NotificationChannelID", chId, ("Address", Max("SYS_NotificationChannel.Address")), ("QuietHoursStart", new TimeSpan(22, 0, 0)));
        sys.UpdateNotificationChannel(chId, uid, "SMS", Max("SYS_NotificationChannel.Address", 'Y'), false, null, null, by);
        Same("SYS_NotificationChannel", "NotificationChannelID", chId, ("Address", Max("SYS_NotificationChannel.Address", 'Y')), ("QuietHoursStart", null));
        TooLong(() => sys.UpdateNotificationChannel(chId, uid, "SMS", Over("SYS_NotificationChannel.Address"), false, null, null, by));
        sys.DeleteNotificationChannel(chId);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM dbo.SYS_NotificationChannel WHERE NotificationChannelID = @I", ("@I", chId)));

        // 화면
        var code = "ITQ" + new string('C', Len("SYS_Screen.ScreenCode") - 3);
        sys.InsertScreen(code, Max("SYS_Screen.ModuleCode"), Max("SYS_Screen.ProcessCode"), Max("SYS_Screen.SubProcessCode"), Max("SYS_Screen.ScreenName"),
            Max("SYS_Screen.ScreenNameEn", uni: '나'), "itq/" + new string('h', Len("SYS_Screen.HRef") - 4), Max("SYS_Screen.LidLabel"), 999, false, by, "__X");
        var sid = Scalar("SELECT ScreenID FROM dbo.SYS_Screen WHERE ScreenCode = @C", ("@C", code));
        Same("SYS_Screen", "ScreenID", sid, ("ScreenName", Max("SYS_Screen.ScreenName")), ("ScreenNameEn", Max("SYS_Screen.ScreenNameEn", uni: '나')),
            ("LidLabel", Max("SYS_Screen.LidLabel")), ("PermissionCriteria", "__X"));
        sys.UpdateScreen(sid, code, "WEB", null, null, Max("SYS_Screen.ScreenName", uni: '나'), null, "itq/x", null, 1, true, by, "_XX");
        Same("SYS_Screen", "ScreenID", sid, ("ScreenName", Max("SYS_Screen.ScreenName", uni: '나')), ("HRef", "itq/x"), ("PermissionCriteria", "_XX"), ("IsVisible", true));
        TooLong(() => sys.UpdateScreen(sid, code, "WEB", null, null, Over("SYS_Screen.ScreenName"), null, "itq/x", null, 1, true, by));
        // scope.Complete() 를 부르지 않는다 — 전부 되돌린다
    }

    // SYS-005 날짜 수정·일정 생성 — 자체 트랜잭션(BeginTransaction)이라 바깥 롤백 트랜잭션에 넣을 수 없어 먼 미래 날짜에 쓰고 지운다
    [SkippableFact]
    public void Calendar_saves_round_trip_and_reject_whole_day_on_bad_value()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN (local DB) to run — writes a 2099 test date and deletes it");
        var sys = new SysRepository(new AmesConnectionFactory(Conn));
        var day = new DateTime(2099, 12, 30);
        void Clean() => Exec($"DELETE dbo.SYS_FactoryCalendar WHERE CalendarDate = '{day:yyyy-MM-dd}'");
        Clean();
        try
        {
            var by = Max("SYS_FactoryCalendar.CreatedBy");
            SysRepository.CalendarShiftInput Row(decimal net) => new(Max("SYS_FactoryCalendar.DayType"), Max("SYS_FactoryCalendar.HolidayName"), 2,
                Max("SYS_FactoryCalendar.ShiftCode"), new TimeSpan(7, 0, 0), new TimeSpan(16, 30, 0), 65, net, Max("SYS_FactoryCalendar.PlantCode"));

            sys.ReplaceCalendarDate(day, [Row(7.926m)], by);
            var id = Scalar($"SELECT MAX(FactoryCalendarID) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = '{day:yyyy-MM-dd}'");
            Same("SYS_FactoryCalendar", "FactoryCalendarID", id, ("DayType", Max("SYS_FactoryCalendar.DayType")), ("HolidayName", Max("SYS_FactoryCalendar.HolidayName")),
                ("ShiftCode", Max("SYS_FactoryCalendar.ShiftCode")), ("PlantCode", Max("SYS_FactoryCalendar.PlantCode")), ("NetWorkHours", 7.93m), ("CreatedBy", by));

            // 두 번째 줄이 컬럼에 안 맞으면 그 날짜 전체가 그대로 남는다(지우기·넣기 한 트랜잭션)
            Assert.Equal(DbValueProblem.OutOfRange, Assert.Throws<DbValueException>(() => sys.ReplaceCalendarDate(day, [Row(8m), Row(123m)], by)).Problem);
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = '{day:yyyy-MM-dd}' AND NetWorkHours = 7.93"));

            Assert.Equal(1, sys.GenerateCalendar([(day, [Row(6m)])], skipExisting: false, by));
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = '{day:yyyy-MM-dd}' AND NetWorkHours = 6"));
            TooLong(() => sys.GenerateCalendar([(day, [Row(6m) with { HolidayName = Over("SYS_FactoryCalendar.HolidayName") }])], skipExisting: false, by));
            Assert.Equal(1, Scalar($"SELECT COUNT(*) FROM dbo.SYS_FactoryCalendar WHERE CalendarDate = '{day:yyyy-MM-dd}'"));
        }
        finally { Clean(); }
    }
}
