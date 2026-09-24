/* ------------------------------------------------------------------
   migrate_scm_portal_user_login.sql
   외부 포탈 로그인을 AspNet 계정이 아니라 SCM_PortalVendorUser 로 (2026-09-25)

   · SCM_PortalVendorUser 를 로그인 계정 테이블로 바꾼다 — 행 하나 = 외부 사용자 하나 = 협력업체 하나.
       UserID           nvarchar(256) PK   로그인 ID = 이메일(소문자로 저장)
       VendorID         varchar(20)        MD_Vendor 참조 — SCM 포탈 화면은 이 업체의 발주만 본다
       UserName         nvarchar(50) NULL  담당자명(선택)
       PasswordHash     nvarchar(200)      ASP.NET PasswordHasher(V3) 해시
       FailedLoginCount int                로그인 실패 수 — 5회면 LockedFlag=1
       LockedFlag       bit                잠금 — 내부 사용자가 SCM-004 에서 해제
       LastLoginTS      datetime2 NULL
       ActiveFlag       bit                사용 여부
     AspNetUsers 로 가던 FK 는 없앤다. 구 구조(UserID=AspNetUsers.Id, 비밀번호 없음)에 행이 남아 있으면
     비밀번호를 만들 수 없으므로 중단한다(09-25 기준 개발·로컬 모두 0행).
   · 포탈 화면(PORTAL-*)은 RBAC 대상이 아니다 — SYS_RolePermission 의 PORTAL 화면 행을 지운다.
     화면 등록(SYS_Screen)은 그대로 둔다(포탈 메뉴가 읽는다).
   · SCM-004 외부 사용자 관리(scm/portal-users) 화면 등록 + Admin REA.
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 신 AMES.Web 은 이 컬럼으로 포탈 로그인을 처리하므로 이 마이그레이션 없이 올리면 포탈 로그인·SCM-004 가 예외다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.SCM_PortalVendorUser', 'U') IS NOT NULL AND COL_LENGTH('dbo.SCM_PortalVendorUser', 'PasswordHash') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.SCM_PortalVendorUser)
    BEGIN
        RAISERROR(N'SCM_PortalVendorUser 에 구 구조 행이 있다 — 비밀번호가 없어 옮길 수 없다. 행을 정리한 뒤 다시 실행', 16, 1);
        RETURN;
    END
    DROP TABLE dbo.SCM_PortalVendorUser;
    PRINT N'✓ 구 SCM_PortalVendorUser(AspNetUsers 참조) 삭제';
END
GO

IF OBJECT_ID('dbo.SCM_PortalVendorUser', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_PortalVendorUser (
        UserID           nvarchar(256) COLLATE Korean_Wansung_CI_AS NOT NULL,
        VendorID         varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        UserName         nvarchar(50)  COLLATE Korean_Wansung_CI_AS NULL,
        PasswordHash     nvarchar(200) COLLATE Korean_Wansung_CI_AS NOT NULL,
        FailedLoginCount int           NOT NULL CONSTRAINT DF_SCM_PortalVendorUser_Failed DEFAULT (0),
        LockedFlag       bit           NOT NULL CONSTRAINT DF_SCM_PortalVendorUser_Locked DEFAULT (0),
        LastLoginTS      datetime2(7)  NULL,
        ActiveFlag       bit           NOT NULL CONSTRAINT DF_SCM_PortalVendorUser_Active DEFAULT (1),
        CreatedBy        varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS        datetime2(7)  NOT NULL CONSTRAINT DF_SCM_PortalVendorUser_Created DEFAULT (SYSDATETIME()),
        ModifiedBy       varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        ModifiedTS       datetime2(7)  NULL,
        CONSTRAINT PK_SCM_PortalVendorUser PRIMARY KEY CLUSTERED (UserID),
        CONSTRAINT FK_SCM_PortalVendorUser_Vendor FOREIGN KEY (VendorID) REFERENCES dbo.MD_Vendor (VendorID)
    );
    CREATE INDEX IX_SCM_PortalVendorUser_Vendor ON dbo.SCM_PortalVendorUser (VendorID, ActiveFlag);
    PRINT N'✓ SCM_PortalVendorUser (외부 사용자 로그인 테이블)';
END
ELSE
    PRINT N'· SCM_PortalVendorUser 이미 로그인 구조';
GO

-- 포탈 화면은 RBAC 로 관리하지 않는다
DELETE p
FROM   dbo.SYS_RolePermission p
JOIN   dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode
WHERE  s.ModuleCode = 'WEB' AND s.ProcessCode = 'PORTAL';
PRINT CONCAT(N'SYS_RolePermission 포탈 화면 행 삭제: ', @@ROWCOUNT, N' 건');
GO

-- SCM-004 외부 사용자 관리
MERGE dbo.SYS_Screen AS t
USING (SELECT 'SCM-004' AS ScreenCode) s ON t.ScreenCode = s.ScreenCode
WHEN NOT MATCHED THEN
    INSERT (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES ('SCM-004', 'WEB', 'SCM', NULL, N'외부 사용자 관리', N'Portal Users', 'scm/portal-users', 'SCM-004', 4, 1, 'migrate', SYSDATETIME());
PRINT CONCAT(N'SYS_Screen SCM-004: ', @@ROWCOUNT, N' 건');

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'SCM-004')
    INSERT INTO dbo.SYS_RolePermission (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    SELECT Id, 'Admin', 'WEB', 'SCM', 'SCM-004', 'REA', 1, SYSDATETIME(), 'migrate', SYSDATETIME()
    FROM   dbo.AspNetRoles WHERE Name = 'Admin';
PRINT CONCAT(N'SYS_RolePermission Admin/SCM-004: ', @@ROWCOUNT, N' 건');
GO

SELECT c.column_id, c.name, TYPE_NAME(c.user_type_id) AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c WHERE c.object_id = OBJECT_ID('dbo.SCM_PortalVendorUser') ORDER BY c.column_id;
SELECT ScreenCode, HRef, SortOrder FROM dbo.SYS_Screen WHERE ProcessCode = 'SCM' ORDER BY SortOrder;
SELECT COUNT(*) AS PortalPermissionRows FROM dbo.SYS_RolePermission p JOIN dbo.SYS_Screen s ON s.ScreenCode = p.ScreenCode WHERE s.ProcessCode = 'PORTAL';
GO
