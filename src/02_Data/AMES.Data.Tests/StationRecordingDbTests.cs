using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 워크스테이션(StationCode) 기록 — 세션 스테이션이 LOT 발행·확정·불량 등록 SP 에서 StationCode 로 남는지. 한 배치 안에서 롤백해 데이터를 남기지 않는다.
public class StationRecordingDbTests
{
    [SkippableFact]
    public void Lot_create_confirm_and_defect_record_the_session_station_and_roll_back()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        Skip.If(Scalar(f!, "SELECT COL_LENGTH(N'dbo.tbl_Lot', N'StationCode')") is null or DBNull, "apply dist/migrate_station_extend.sql first");
        Skip.If(Scalar(f!, "SELECT TOP 1 StationCode FROM dbo.MD_Station WHERE ISNULL(Status,'ACTIVE') <> 'INACTIVE'") is null, "no station");
        const string sql = """
            SET NOCOUNT ON; BEGIN TRAN;
            DECLARE @st varchar(20) = (SELECT TOP 1 StationCode FROM dbo.MD_Station WHERE ISNULL(Status,'ACTIVE') <> 'INACTIVE');
            DECLARE @ln varchar(20) = (SELECT LineID FROM dbo.MD_Station WHERE StationCode = @st);
            INSERT dbo.PR_PopSession(OperatorID,TerminalID,LineID,ShiftCode,AuthMethod,StartedAt,ExpiresAt,CreatedBy) VALUES (N'ZZT',@st,@ln,'D','Pin',SYSDATETIME(),DATEADD(hour,1,SYSDATETIME()),'T');
            DECLARE @s int = SCOPE_IDENTITY();
            CREATE TABLE #o(LotID int, CreatedTS datetime2);
            INSERT #o EXEC dbo.SP_POP_INJ_CreateManualRawLots_InsLot @LotCode='ZZSTLOT',@ItemNo='X',@LineID=@ln,@By='ZZT';
            DECLARE @lot int = (SELECT LotID FROM #o);
            DECLARE @created varchar(20) = (SELECT StationCode FROM dbo.tbl_Lot WHERE LotID = @lot);
            UPDATE dbo.tbl_Lot SET StationCode = NULL WHERE LotID = @lot;
            EXEC dbo.SP_POP_INJ_ConfirmByLotCode_UpdLot @WoID=NULL,@Op=N'ZZT',@LotID=@lot,@Sess=@s;
            DECLARE @confirmed varchar(20) = (SELECT StationCode FROM dbo.tbl_Lot WHERE LotID = @lot);
            CREATE TABLE #d(DefectID int);
            INSERT #d EXEC dbo.SP_POP_LOT_InsertDefectDetail @R=NULL,@W=NULL,@L=@lot,@P='INJ',@C='X',@N=N'n',@Prior='RAW',@Rev=NULL,@Op='ZZT',@By='ZZT';
            DECLARE @defect varchar(20) = (SELECT StationCode FROM dbo.PR_DefectDetail WHERE DefectID = (SELECT DefectID FROM #d));
            ROLLBACK;
            SELECT @st + '|' + ISNULL(@created,'-') + '|' + ISNULL(@confirmed,'-') + '|' + ISNULL(@defect,'-');
            """;
        var parts = ((string)Scalar(f!, sql)!).Split('|');
        Assert.Equal(parts[0], parts[1]);   // 발행: 열린 세션의 스테이션
        Assert.Equal(parts[0], parts[2]);   // 확정: 비어 있으면 세션 스테이션으로 채움
        Assert.Equal(parts[0], parts[3]);   // 불량: LOT 의 스테이션
        Assert.Equal(0, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.tbl_Lot WHERE LotCode = 'ZZSTLOT'")));
    }
}
