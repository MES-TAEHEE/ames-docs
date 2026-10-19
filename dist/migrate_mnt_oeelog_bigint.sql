-- ============================================================================
-- MNT_OEELog.OEELogID int → bigint (10-19 사용자 요청)
--   · IDENTITY·현재 값은 그대로 이어진다(ALTER COLUMN 은 identity 속성을 유지)
--   · 기본키 PK_MNT_OEELog(클러스터) 는 컬럼을 바꾸려고 떨어뜨렸다가 같은 이름으로 다시 만든다
--   · 이 테이블을 참조하는 컬럼·프로시저는 없다(10-19 개발·로컬 DB 확인)
--   · 재실행 안전 — 이미 bigint 면 아무것도 하지 않는다
--   · 배포: 신 Web(OEELogID 를 int·bigint 둘 다 읽음) → 이 마이그레이션
--     구 Web 은 (int) 로 읽어 적용 뒤 MNT-003·RPT-006 이 InvalidCastException
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비번> -C -f 65001 -I -b -i dist\migrate_mnt_oeelog_bigint.sql
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MNT_OEELog', N'U') IS NULL
    THROW 51000, N'MNT_OEELog 테이블이 없습니다.', 1;
GO

IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID(N'dbo.MNT_OEELog') AND c.name = N'OEELogID' AND t.name = N'int')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID(N'dbo.MNT_OEELog'))
        THROW 51001, N'MNT_OEELog 를 참조하는 외래키가 있어 중단합니다.', 1;

    BEGIN TRAN;
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.MNT_OEELog') AND name = N'PK_MNT_OEELog')
        ALTER TABLE dbo.MNT_OEELog DROP CONSTRAINT PK_MNT_OEELog;
    ALTER TABLE dbo.MNT_OEELog ALTER COLUMN OEELogID bigint NOT NULL;
    ALTER TABLE dbo.MNT_OEELog ADD CONSTRAINT PK_MNT_OEELog PRIMARY KEY CLUSTERED (OEELogID);
    COMMIT;
    PRINT N'MNT_OEELog.OEELogID → bigint';
END
ELSE
    PRINT N'MNT_OEELog.OEELogID 는 이미 bigint — 건너뜀';
GO

IF EXISTS (SELECT 1 FROM sys.extended_properties
           WHERE major_id = OBJECT_ID(N'dbo.MNT_OEELog') AND name = N'MS_Description'
             AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.MNT_OEELog'), N'OEELogID', 'ColumnId')
             AND CAST(value AS nvarchar(200)) = N'PK · OEE Log ID · int')
    EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = N'PK · OEE Log ID · bigint',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MNT_OEELog',
         @level2type = N'COLUMN', @level2name = N'OEELogID';
GO

-- 확인
SELECT c.name AS ColumnName, t.name AS TypeName, c.is_identity AS IsIdentity,
       IDENT_CURRENT(N'dbo.MNT_OEELog') AS IdentCurrent,
       (SELECT COUNT(*) FROM dbo.MNT_OEELog) AS RowCnt
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.MNT_OEELog') AND c.name = N'OEELogID';
GO
