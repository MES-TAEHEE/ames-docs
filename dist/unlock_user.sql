/* ------------------------------------------------------------------
   unlock_user.sql — 내부 계정 비상 해제 (10-07)

   계정 잠금(로그인·PIN 5회 실패 → SYS_UserProfile.AccountStatus = LOCKED)은 자동으로 풀리지 않고
   SYS-001 에서 Admin 만 푼다. Admin 계정이 모두 잠겨 화면으로 풀 사람이 없을 때만 이 스크립트를 쓴다.

   하는 일: 이메일(로그인 ID)로 찾은 계정 하나를
     SYS_UserProfile  AccountStatus = 'ACTIVE', FailedLoginCount = 0 (원래 상태가 무엇이든 — 비활성·정지도 ACTIVE 로)
     AspNetUsers      LockoutEnd = NULL, AccessFailedCount = 0
   으로 만든다. 프로필이 없는 계정은 로그인할 수 없으므로 중단한다(SYS-001 에서 프로필을 만들어야 한다).
   비밀번호는 바꾸지 않는다.

   Mode=ROLLBACK 이면 결과만 보여 주고 되돌리며, COMMIT 이면 반영한다.

   실행 (DB 에 쓰기 권한이 있는 계정으로):
     sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <pw> -C -f 65001 -I -b ^
            -v Email="admin@ames.local" Mode=ROLLBACK -i dist\unlock_user.sql
     → 확인 후 -v ... Mode=COMMIT 로 다시 실행
   반영 후 그 계정으로 로그인해 SYS-001 에서 나머지 계정을 정상 절차로 푼다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50100, N'Mode 는 ROLLBACK 또는 COMMIT', 1;

DECLARE @Email nvarchar(256) = LTRIM(RTRIM(N'$(Email)'));
DECLARE @UserID nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = UPPER(@Email));
PRINT CONCAT(N'대상: ', @@SERVERNAME, N' / ', DB_NAME(), N' / ', @Email, N' / Mode=$(Mode)');
IF @UserID IS NULL THROW 50101, N'그 이메일의 내부 계정이 없다', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE UserID = @UserID)
    THROW 50102, N'프로필(SYS_UserProfile)이 없는 계정이다 — 해제해도 로그인할 수 없다', 1;

SELECT N'적용 전' AS [구분], p.AccountStatus, p.FailedLoginCount, u.LockoutEnd, u.AccessFailedCount
FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id = p.UserID WHERE p.UserID = @UserID;

BEGIN TRAN;
UPDATE dbo.SYS_UserProfile
SET    AccountStatus = 'ACTIVE', FailedLoginCount = 0, ModifiedBy = 'EMERGENCY', ModifiedTS = SYSDATETIME()
WHERE  UserID = @UserID;
UPDATE dbo.AspNetUsers SET LockoutEnd = NULL, AccessFailedCount = 0 WHERE Id = @UserID;

SELECT N'적용 후' AS [구분], p.AccountStatus, p.FailedLoginCount, u.LockoutEnd, u.AccessFailedCount
FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUsers u ON u.Id = p.UserID WHERE p.UserID = @UserID;

IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT — 해제됨'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK — 시험 실행, 반영 안 됨'; END;
GO
