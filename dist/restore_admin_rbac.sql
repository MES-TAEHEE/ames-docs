/* ------------------------------------------------------------------
   restore_admin_rbac.sql — 권한 관리 화면 비상 복구 (10-12)

   SYS-003(화면 관리, sys/screens)·SYS-004(역할/권한, sys/rbac)는 권한을 다시 줄 수 있는 화면이라
   System Administrator(ROLE-SYSADMIN)의 읽기·수정과 두 화면의 수정 기능을 화면·저장소가 끄지 못하게 막는다
   (AMES.Data.Services.ProtectedScreens). 그래도 DB 를 직접 고쳐 잠겼을 때만 이 스크립트를 쓴다.

   하는 일(두 화면 각각):
     SYS_Screen          PermissionCriteria 의 수정 자리를 '_'(기능 있음)로 — 읽기는 항상 '_', 승인 자리는 그대로
     SYS_RolePermission  ROLE-SYSADMIN 행을 읽기·수정 부여로(승인은 기능이 있고 이미 부여돼 있으면 유지), 행이 없으면 만든다
   다른 역할은 두 화면의 수정 자리 'X'(기능 없음)만 '_'(미부여)로 맞춘다(부여는 하지 않는다). 다른 화면은 건드리지 않는다. 두 화면 행 자체가 없으면 중단한다(스키마 시드로 다시 넣어야 한다).

   Mode=ROLLBACK 이면 결과만 보여 주고 되돌리며, COMMIT 이면 반영한다.

   실행 (DB 에 쓰기 권한이 있는 계정으로):
     sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <pw> -C -f 65001 -I -b ^
            -v Mode=ROLLBACK -i dist\restore_admin_rbac.sql
     → 확인 후 -v Mode=COMMIT 로 다시 실행
   반영 후 1분 안에(권한 재조회 주기) 관리자가 SYS-004 를 열 수 있다 — 바로 보려면 다시 로그인한다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50100, N'Mode 는 ROLLBACK 또는 COMMIT', 1;

DECLARE @Admin nvarchar(450) = N'ROLE-SYSADMIN';
DECLARE @AdminName varchar(40) = (SELECT LEFT(Name, 40) FROM dbo.AspNetRoles WHERE Id = @Admin);
PRINT CONCAT(N'대상: ', @@SERVERNAME, N' / ', DB_NAME(), N' / Mode=$(Mode)');
IF @AdminName IS NULL
    THROW 50111, N'System Administrator 역할(ROLE-SYSADMIN)이 없다 — dist/migrate_system_roles.sql 을 먼저 적용한다', 1;
IF (SELECT COUNT(DISTINCT TRIM('/' FROM HRef)) FROM dbo.SYS_Screen WHERE TRIM('/' FROM ISNULL(HRef, '')) IN ('sys/screens', 'sys/rbac')) < 2
    THROW 50112, N'SYS-003(sys/screens)·SYS-004(sys/rbac) 화면 행이 없다 — dist/AMES_Schema.sql 의 SYS_Screen 시드로 다시 넣는다', 1;

SELECT N'적용 전' AS [구분], s.ScreenCode, s.HRef, s.PermissionCriteria, rp.PermissionLevel AS AdminLevel
FROM   dbo.SYS_Screen s
LEFT JOIN dbo.SYS_RolePermission rp ON rp.ScreenCode = s.ScreenCode AND rp.RoleID = @Admin
WHERE  TRIM('/' FROM ISNULL(s.HRef, '')) IN ('sys/screens', 'sys/rbac')
ORDER  BY s.ScreenCode;

BEGIN TRAN;

UPDATE dbo.SYS_Screen
SET    PermissionCriteria = '__' + CASE WHEN SUBSTRING(ISNULL(PermissionCriteria, ''), 3, 1) = 'X' THEN 'X' ELSE '_' END,
       ModifiedBy = 'RBAC-RESTORE', ModifiedTS = SYSDATETIME()
WHERE  TRIM('/' FROM ISNULL(HRef, '')) IN ('sys/screens', 'sys/rbac')
  AND  ISNULL(PermissionCriteria, '') COLLATE Latin1_General_BIN
       <> '__' + CASE WHEN SUBSTRING(ISNULL(PermissionCriteria, ''), 3, 1) = 'X' THEN 'X' ELSE '_' END;

UPDATE rp
SET    PermissionLevel = STUFF(rp.PermissionLevel, 2, 1, '_'), ModifiedBy = 'RBAC-RESTORE', ModifiedTS = SYSDATETIME()
FROM   dbo.SYS_RolePermission rp
JOIN   dbo.SYS_Screen s ON s.ScreenCode = rp.ScreenCode
WHERE  TRIM('/' FROM ISNULL(s.HRef, '')) IN ('sys/screens', 'sys/rbac')
  AND  rp.RoleID <> @Admin AND SUBSTRING(rp.PermissionLevel, 2, 1) = 'X';

MERGE dbo.SYS_RolePermission AS t
USING (SELECT ScreenCode, ModuleCode, ProcessCode, SUBSTRING(PermissionCriteria, 3, 1) AS ASlot
       FROM   dbo.SYS_Screen
       WHERE  TRIM('/' FROM ISNULL(HRef, '')) IN ('sys/screens', 'sys/rbac')) AS src
   ON t.RoleID = @Admin AND t.ScreenCode = src.ScreenCode
WHEN MATCHED AND ISNULL(t.PermissionLevel, '') COLLATE Latin1_General_BIN
         <> 'RE' + CASE WHEN src.ASlot = 'X' THEN 'X' WHEN CHARINDEX('A', UPPER(ISNULL(t.PermissionLevel, ''))) > 0 THEN 'A' ELSE '_' END THEN
    UPDATE SET PermissionLevel = 'RE' + CASE WHEN src.ASlot = 'X' THEN 'X' WHEN CHARINDEX('A', UPPER(ISNULL(t.PermissionLevel, ''))) > 0 THEN 'A' ELSE '_' END,
               ModifiedBy = 'RBAC-RESTORE', ModifiedTS = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES (@Admin, @AdminName, src.ModuleCode, src.ProcessCode, src.ScreenCode,
            'RE' + CASE WHEN src.ASlot = 'X' THEN 'X' ELSE '_' END, 0, SYSDATETIME(), 'RBAC-RESTORE', SYSDATETIME());

SELECT N'적용 후' AS [구분], s.ScreenCode, s.HRef, s.PermissionCriteria, rp.PermissionLevel AS AdminLevel
FROM   dbo.SYS_Screen s
LEFT JOIN dbo.SYS_RolePermission rp ON rp.ScreenCode = s.ScreenCode AND rp.RoleID = @Admin
WHERE  TRIM('/' FROM ISNULL(s.HRef, '')) IN ('sys/screens', 'sys/rbac')
ORDER  BY s.ScreenCode;

IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT — 복구됨'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK — 시험 실행, 반영 안 됨'; END;
GO
