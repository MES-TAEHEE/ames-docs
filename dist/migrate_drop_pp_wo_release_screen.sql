-- ════════════════════════════════════════════════════════════════════════
--  migrate_drop_pp_wo_release_screen.sql
--  PP-007 작업 지시 릴리스 (pp/wo-release) 화면 삭제 — SYS_Screen · SYS_RolePermission 정리
--
--  릴리스는 PP-004 작업 지시 화면에서 한다. 화면 파일이 없어졌으므로
--  메뉴·권한 행을 남기면 죽은 링크가 된다. 시드 ScreenCode 가 파일마다
--  'PP-007' / 'PP-07' 로 달라 HRef 로 찾는다.
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_drop_pp_wo_release_screen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
GO

DECLARE @Codes TABLE (ScreenCode VARCHAR(20) PRIMARY KEY);
INSERT INTO @Codes (ScreenCode)
SELECT DISTINCT ScreenCode FROM dbo.SYS_Screen WHERE HRef = 'pp/wo-release'
UNION
SELECT v.c FROM (VALUES ('PP-007'), ('PP-07')) v(c);

BEGIN TRAN;

DELETE rp FROM dbo.SYS_RolePermission rp
WHERE rp.ScreenCode IN (SELECT ScreenCode FROM @Codes);
PRINT CONCAT(N'SYS_RolePermission 삭제: ', @@ROWCOUNT, N'행');

DELETE FROM dbo.SYS_Screen
WHERE HRef = 'pp/wo-release'
   OR (ModuleCode = 'WEB' AND ScreenCode IN (SELECT ScreenCode FROM @Codes));
PRINT CONCAT(N'SYS_Screen 삭제: ', @@ROWCOUNT, N'행');

COMMIT;
GO
