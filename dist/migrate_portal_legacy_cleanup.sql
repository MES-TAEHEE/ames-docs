/* ------------------------------------------------------------------
   migrate_portal_legacy_cleanup.sql
   구 방식 외부 포탈 데이터 정리 (2026-09-25)

   외부 사용자는 SCM_PortalVendorUser(SCM-004)로 따로 관리하므로 아래는 더 이상 쓰지 않는다.
     ① 포탈 화면(ProcessCode PORTAL)의 SYS_RolePermission 행 — 포탈은 RBAC 대상이 아니다
     ② 역할 ExternalCustomer 의 SYS_RolePermission 행
     ③ 역할이 ExternalCustomer 하나뿐인 Identity 계정(구 외부 계정 adminExt·testExt 등)과
        그 SYS_UserProfile·AspNetUserClaims/Logins/Tokens/Roles
     ④ 다른 역할도 가진 계정은 ExternalCustomer 역할 연결만 끊는다
     ⑤ 역할 ExternalCustomer 와 그 AspNetRoleClaims
   · SYS_AuditLog 등 이력은 건드리지 않는다. Identity 테이블을 참조하는 FK 는 없다(09-25 확인).
   · 재실행 안전. 다른 스크립트가 포탈 RBAC 행·역할을 다시 만들어도 이 스크립트를 다시 돌리면 정리된다.
   · 적용: sqlcmd -f 65001 -I -b
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @role nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE NormalizedName = 'EXTERNALCUSTOMER');

-- ① ② 권한 행
DELETE p
FROM   dbo.SYS_RolePermission p
WHERE  p.RoleName = 'ExternalCustomer'
   OR  (@role IS NOT NULL AND p.RoleID = @role)
   OR  EXISTS (SELECT 1 FROM dbo.SYS_Screen s WHERE s.ScreenCode = p.ScreenCode AND s.ModuleCode = 'WEB' AND s.ProcessCode = 'PORTAL');
PRINT CONCAT(N'SYS_RolePermission 포탈·ExternalCustomer 행 삭제: ', @@ROWCOUNT, N' 건');

-- ③ 역할이 ExternalCustomer 하나뿐인 구 외부 계정
DECLARE @users TABLE (Id nvarchar(450) PRIMARY KEY, UserName nvarchar(256));
IF @role IS NOT NULL
    INSERT @users (Id, UserName)
    SELECT u.Id, u.UserName
    FROM   dbo.AspNetUsers u
    WHERE  EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId = @role)
      AND  NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id AND ur.RoleId <> @role);

DECLARE @names nvarchar(max) = (SELECT STRING_AGG(UserName, N', ') FROM @users);
DELETE p  FROM dbo.SYS_UserProfile  p  JOIN @users x ON x.Id = p.UserID;   PRINT CONCAT(N'SYS_UserProfile 삭제: ', @@ROWCOUNT, N' 건');
DELETE c  FROM dbo.AspNetUserClaims c  JOIN @users x ON x.Id = c.UserId;
DELETE l  FROM dbo.AspNetUserLogins l  JOIN @users x ON x.Id = l.UserId;
DELETE t  FROM dbo.AspNetUserTokens t  JOIN @users x ON x.Id = t.UserId;
DELETE ur FROM dbo.AspNetUserRoles ur  JOIN @users x ON x.Id = ur.UserId;
DELETE u  FROM dbo.AspNetUsers u       JOIN @users x ON x.Id = u.Id;        PRINT CONCAT(N'AspNetUsers(구 외부 계정) 삭제: ', @@ROWCOUNT, N' 건 ', ISNULL(@names, N''));

-- ④ ⑤ 남은 역할 연결과 역할
IF @role IS NOT NULL
BEGIN
    DELETE FROM dbo.AspNetUserRoles  WHERE RoleId = @role;  PRINT CONCAT(N'AspNetUserRoles ExternalCustomer 연결 해제: ', @@ROWCOUNT, N' 건');
    DELETE FROM dbo.AspNetRoleClaims WHERE RoleId = @role;
    DELETE FROM dbo.AspNetRoles      WHERE Id = @role;      PRINT CONCAT(N'AspNetRoles ExternalCustomer 삭제: ', @@ROWCOUNT, N' 건');
END
ELSE
    PRINT N'· AspNetRoles ExternalCustomer 이미 없음';

COMMIT;
GO

SELECT (SELECT COUNT(*) FROM dbo.AspNetRoles WHERE NormalizedName = 'EXTERNALCUSTOMER') AS ExternalRole,
       (SELECT COUNT(*) FROM dbo.SYS_RolePermission p JOIN dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode WHERE s.ProcessCode = 'PORTAL') AS PortalPermissionRows,
       (SELECT COUNT(*) FROM dbo.SCM_PortalVendorUser) AS PortalUsers;
GO
