/* ------------------------------------------------------------------
   migrate_user_status_mail_cleanup.sql  (2026-10-07)
   계정 메일(가입·이메일 인증·비밀번호 찾기) 기능 폐지에 따른 계정 상태 정리

   · 내부 계정은 SYS-001 에서 관리자만 만든다 — 자기가입·이메일 인증 단계가 없어졌다(사용자 결정 10-07).
   · ① 계정 상태 UNVERIFIED(미인증)·PENDING(승인대기) → ACTIVE (사용자 결정: UNVERIFIED 계정도 활성으로)
     ② 이메일 미인증 계정을 모두 인증됨으로(로그인은 계정 상태만 본다 — RequireConfirmedAccount=false)
     ③ 공통코드 USER_STATUS 에서 PENDING·UNVERIFIED 항목 삭제
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 배포 순서: 이 마이그레이션 → 신 Web. 구 Web 에 먼저 적용해도 미인증 화면 문구만 사라질 뿐 오류는 없다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

UPDATE dbo.SYS_UserProfile
SET    AccountStatus = 'ACTIVE', ModifiedBy = 'MAIL-CLEANUP', ModifiedTS = SYSDATETIME()
WHERE  UPPER(AccountStatus) IN ('UNVERIFIED', 'PENDING');
PRINT N'· 계정 상태 UNVERIFIED·PENDING → ACTIVE: ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

UPDATE dbo.AspNetUsers SET EmailConfirmed = 1 WHERE ISNULL(EmailConfirmed, 0) = 0;
PRINT N'· 이메일 인증됨으로 정리: ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

DELETE dbo.MD_CodeItem WHERE GroupCode = 'USER_STATUS' AND CodeValue IN ('PENDING', 'UNVERIFIED');
PRINT N'· 공통코드 USER_STATUS 항목 삭제: ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

COMMIT;
GO

SELECT AccountStatus, COUNT(*) AS 계정수 FROM dbo.SYS_UserProfile GROUP BY AccountStatus;
SELECT CodeValue FROM dbo.MD_CodeItem WHERE GroupCode = 'USER_STATUS' ORDER BY SortOrder;
GO
