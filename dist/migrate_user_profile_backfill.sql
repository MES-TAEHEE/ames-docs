/* ------------------------------------------------------------------
   migrate_user_profile_backfill.sql  (2026-10-07)
   SYS_UserProfile 이 없는 내부 계정(AspNetUsers)에 프로필을 채운다

   · 10-07 부터 로그인 검사(AuthRepository.GetProfileStatus)는 프로필이 없으면 승인 대기로 보고 거부한다.
     예전에는 "프로필 없음 = 승인됨" 이라 자기가입 프로필 생성이 실패한 계정이 관리자 승인 없이 로그인됐다.
   · 역할(AspNetUserRoles)이 있는 계정 = 관리자가 만든 계정 → ACTIVE(지금처럼 로그인 유지)
     역할이 없는 계정 = 관리자가 역할을 주지 않은 계정 → INACTIVE(SYS-001 에서 확인 후 활성화)
   · EmployeeNo 는 비워 둔다(SYS-001 에서 입력). EmployeeName = 사용자명 앞 50자, CreatedBy = 'PROFILE-BACKFILL'.
   · 재실행 안전(프로필이 이미 있는 계정은 건드리지 않는다). 적용: sqlcmd -f 65001 -I -b
   · 배포 순서: ① 이 마이그레이션 ② 신 Web. 반대로 하면 프로필 없는 계정이 신 Web 에서 로그인하지 못한다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;

INSERT INTO dbo.SYS_UserProfile (UserID, EmployeeName, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
SELECT u.Id,
       LEFT(COALESCE(u.UserName, u.Email, u.Id), 50),
       CASE WHEN EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id) THEN 'ACTIVE' ELSE 'INACTIVE' END,
       0, 'PROFILE-BACKFILL', SYSDATETIME()
FROM   dbo.AspNetUsers u
WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile p WHERE p.UserID = u.Id);

PRINT N'· 프로필 생성: ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';
GO

SELECT COUNT(*) AS 프로필없는계정
FROM   dbo.AspNetUsers u
WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile p WHERE p.UserID = u.Id);
GO
