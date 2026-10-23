using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-24 SYS-008 채널 설정 — PUSH 행(FCM 기기 토큰)은 알림 앱만 만들고, 웹은 수신 여부·방해 금지 시각만 고친다.
// 웹 등록은 PUSH 를 거부하고, 수정은 PUSH 행의 수신자·채널·주소를 그대로 두며 다른 채널 행을 PUSH 로 바꾸지 못한다. 목록은 사번으로 이름을 찾는다.
// 쓰기라서 AMES_TEST_CONN(로컬 DB)을 지정했을 때만 돈다. 시험 행(사번 ZNCH01, CreatedBy itest-nch)은 끝에 지운다.
public class NotificationChannelWebEditTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes a test worker and notification channels";
    const string Actor  = "itest-nch";
    const string Worker = "ZNCH01";
    const string Token  = "itest-nch-token";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.SYS_NotificationChannel WHERE UserID = @W OR CreatedBy = @By;
        DELETE dbo.MD_Worker WHERE EmployeeNo = @W;
        """, ("@W", Worker), ("@By", Actor));

    static object? Col(Connection.AmesConnectionFactory f, int id, string col)
        => Scalar(f, $"SELECT {col} FROM dbo.SYS_NotificationChannel WHERE NotificationChannelID = @I", ("@I", id));

    [SkippableFact]
    public void Push_rows_keep_app_registered_address_and_web_cannot_create_or_switch_to_push()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var sys = new SysRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, "INSERT dbo.MD_Worker (EmployeeNo, EmployeeName, PinHash, ActiveFlag, CreatedBy, CreatedTS) VALUES (@W, N'itest 작업자', 'h', 1, @By, SYSDATETIME());",
                ("@W", Worker), ("@By", Actor));

            // 웹 등록은 PUSH 를 받지 않는다(대소문자·공백 무관)
            Assert.Throws<InvalidOperationException>(() =>
                sys.InsertNotificationChannel(Worker, " push ", "admin@ames.local", true, null, null, Actor));

            // 앱이 등록한 PUSH 행 — 웹 수정은 채널·주소·수신자를 바꾸지 못하고 수신 여부·방해 금지 시각만 바꾼다
            var pushId = Convert.ToInt32(Scalar(f!, """
                INSERT dbo.SYS_NotificationChannel (UserID, Channel, Address, IsEnabled, CreatedBy, CreatedTS)
                OUTPUT INSERTED.NotificationChannelID VALUES (@W, 'PUSH', @T, 1, @By, SYSDATETIME());
                """, ("@W", Worker), ("@T", Token), ("@By", Actor)));
            sys.UpdateNotificationChannel(pushId, "OTHER", "EMAIL", "admin@ames.local", false, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), Actor);
            Assert.Equal("PUSH", Col(f!, pushId, "Channel"));
            Assert.Equal(Token, Col(f!, pushId, "Address"));
            Assert.Equal(Worker, Col(f!, pushId, "UserID"));
            Assert.Equal(false, Col(f!, pushId, "IsEnabled"));
            Assert.Equal(new TimeSpan(22, 0, 0), Col(f!, pushId, "QuietHoursStart"));

            // 다른 채널 행은 PUSH 로 바꿀 수 없다(이번 사고: EMAIL → PUSH 로 메일 주소가 토큰 자리에 남음)
            sys.InsertNotificationChannel(Worker, "EMAIL", "a@b.c", true, null, null, Actor);
            var mailId = Convert.ToInt32(Scalar(f!, "SELECT NotificationChannelID FROM dbo.SYS_NotificationChannel WHERE UserID = @W AND Channel = 'EMAIL'", ("@W", Worker)));
            sys.UpdateNotificationChannel(mailId, Worker, "PUSH", "a@b.c", true, null, null, Actor);
            Assert.Equal("EMAIL", Col(f!, mailId, "Channel"));
            sys.UpdateNotificationChannel(mailId, Worker, "EMAIL", "x@y.z", true, null, null, Actor);   // PUSH 가 아니면 종전대로 고친다
            Assert.Equal("x@y.z", Col(f!, mailId, "Address"));

            // 목록 — 사번 행은 그 사람 이름으로 보인다(현장 작업자 포함)
            Assert.All(sys.ListNotificationChannels().Where(c => c.UserId == Worker), c => Assert.Equal("itest 작업자", c.UserName));
        }
        finally { Cleanup(f!); }
    }
}
