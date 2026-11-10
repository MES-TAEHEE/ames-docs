-- 내부 계정의 외부 포탈 사용 (11-10 사용자 결정)
-- SYS-001 사용자 관리에서 Admin 이 「외부 포탈 사용」을 켜고 협력업체 하나를 고르면 그 계정 이메일로
-- SCM_PortalVendorUser 행을 만들고 InternalUserID 에 내부 계정 ID(AspNetUsers.Id)를 둔다.
-- 그 행은 내부 계정 비밀번호·상태로 /portal/login 에 로그인하고, 포탈의 업체 범위는 외부 사용자와 같다.
-- NULL = SCM 외부 사용자 관리에서 등록한 진짜 외부 사용자(종전 그대로).
--
-- 적용: sqlcmd -S <서버> -d <DB> -f 65001 -I -b -i dist\migrate_scm_portal_internal_link.sql
-- 재실행 안전. 컬럼이 감사 컬럼 뒤에 붙으므로 적용 뒤 dist\migrate_audit_columns_last.sql 을 다시 돌린다(순서만, 동작 무관).
-- 배포: 이 마이그레이션 → 신 Web. 신 Web 은 이 컬럼을 읽으므로 없으면 포탈 로그인·SCM 외부 사용자 관리·SYS-001 목록이 예외다.
-- 구 Web 은 이 컬럼을 읽지 않아 적용 뒤에도 그대로 동작한다.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF COL_LENGTH(N'dbo.SCM_PortalVendorUser', N'InternalUserID') IS NULL
BEGIN
    ALTER TABLE dbo.SCM_PortalVendorUser ADD InternalUserID nvarchar(450) COLLATE DATABASE_DEFAULT NULL;
    PRINT 'SCM_PortalVendorUser.InternalUserID added';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SCM_PortalVendorUser') AND name = N'IX_SCM_PortalVendorUser_InternalUser')
    CREATE NONCLUSTERED INDEX IX_SCM_PortalVendorUser_InternalUser ON dbo.SCM_PortalVendorUser (InternalUserID);
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID(N'dbo.SCM_PortalVendorUser') AND name = N'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.SCM_PortalVendorUser'), N'InternalUserID', 'ColumnId'))
    EXEC sys.sp_addextendedproperty N'MS_Description', N'내부 계정 연결(AspNetUsers.Id) — SYS-001 에서 외부 포탈 사용을 켠 내부 사용자. NULL = 외부 사용자',
        N'SCHEMA', N'dbo', N'TABLE', N'SCM_PortalVendorUser', N'COLUMN', N'InternalUserID';
GO

SELECT COUNT(*) AS LinkedInternalUsers FROM dbo.SCM_PortalVendorUser WHERE InternalUserID IS NOT NULL;
GO
