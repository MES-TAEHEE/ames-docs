/* ------------------------------------------------------------------
   migrate_role_permission_orphan_cleanup.sql
   화면이 없는 역할 권한 행 정리 (2026-10-09)

   SYS-003 화면 삭제가 SYS_Screen 행만 지우고 그 화면 코드의 SYS_RolePermission 행을 남겨 왔다
   (10-09 수정 — SysRepository.DeleteScreen 이 한 트랜잭션으로 같이 지운다). 남은 행은
     · 같은 코드로 새 화면을 등록하면 옛 권한이 그대로 살아나고
     · 다른 화면의 코드를 그 코드로 바꾸는 것은 막는다(UpdateScreen 51823).
   권한 판정은 SYS_Screen 에 있는 화면만 보므로 지워도 지금 동작은 바뀌지 않는다.
   10-09 기준 개발·로컬 DB 모두 FG-05·INJ-02·INJ-04·INJ-06·INJ-08·QC-01·QC-04 의 19행.

   · 지운 행은 결과 집합(OUTPUT)으로 보이고 PRINT 로 건수를 남긴다.
   · 재실행 안전(남은 행이 없으면 0건).
   · 적용: sqlcmd -f 65001 -I -b
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DELETE p
OUTPUT deleted.RolePermissionID, deleted.ScreenCode, deleted.RoleID, deleted.RoleName, deleted.PermissionLevel
FROM   dbo.SYS_RolePermission p
WHERE  p.ScreenCode IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen s WHERE s.ScreenCode = p.ScreenCode);
PRINT CONCAT(N'화면 없는 SYS_RolePermission 행 삭제: ', @@ROWCOUNT, N' 건');

COMMIT;
GO

SELECT COUNT(*) AS RemainingOrphans
FROM   dbo.SYS_RolePermission p
WHERE  p.ScreenCode IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen s WHERE s.ScreenCode = p.ScreenCode);
