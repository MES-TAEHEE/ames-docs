-- ════════════════════════════════════════════════════════════════════════
--  seed_portal_dev.sql  (개발 전용 — 운영 금지)
--  외부 포탈 테스트 계정 adminExt@ames.local → 협력업체 V1007(EOS Georgia Plant).
--  09-25 부터 외부 사용자는 SCM_PortalVendorUser(SCM-004)라 AspNet 계정을 만들지 않는다.
--  비밀번호는 admin@ames.local 과 같다(Dev2026!) — PasswordHash 를 admin 행에서 복사한다
--  (Identity V3 해시는 솔트가 해시 안에 있어 다른 계정에 붙여도 같은 비밀번호로 검증된다).
--  선행: dist/migrate_scm_portal_user_login.sql. 재실행 안전(이미 있으면 건드리지 않는다).
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/seed_portal_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @hash NVARCHAR(200) = (SELECT PasswordHash FROM dbo.AspNetUsers WHERE NormalizedUserName = 'ADMIN@AMES.LOCAL');
IF @hash IS NULL BEGIN RAISERROR(N'admin@ames.local 이 없다 — seed_admin_permissions.sql 먼저', 16, 1); RETURN; END
IF COL_LENGTH('dbo.SCM_PortalVendorUser', 'PasswordHash') IS NULL BEGIN RAISERROR(N'SCM_PortalVendorUser 가 구 구조 — migrate_scm_portal_user_login.sql 먼저', 16, 1); RETURN; END
IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WHERE VendorID = 'V1007') BEGIN RAISERROR(N'MD_Vendor V1007 이 없다', 16, 1); RETURN; END

IF NOT EXISTS (SELECT 1 FROM dbo.SCM_PortalVendorUser WHERE UserID = 'adminext@ames.local')
BEGIN
    INSERT INTO dbo.SCM_PortalVendorUser (UserID, VendorID, UserName, PasswordHash, FailedLoginCount, LockedFlag, ActiveFlag, CreatedBy, CreatedTS)
    VALUES ('adminext@ames.local', 'V1007', N'외부 테스트', @hash, 0, 0, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SCM_PortalVendorUser adminext@ames.local → V1007 (비밀번호 = admin 과 동일)';
END
ELSE
    PRINT N'· SCM_PortalVendorUser adminext@ames.local 이미 존재';
GO

SELECT UserID, VendorID, UserName, ActiveFlag, LockedFlag FROM dbo.SCM_PortalVendorUser ORDER BY UserID;
GO
