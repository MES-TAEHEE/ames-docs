/*
  MD_Bop.InspStdID — BOP 단계의 QC 검사 기준(MD_InspectionStandard.InspStdID, 논리 참조 · FK 없음).
  MD-005 BOP 단계 창에서 QC 필수를 켜면 검사 기준을 고르고, 검사 기준(MD-017)은 BOP 단계가 쓰고 있으면 삭제되지 않는다.
  재실행 안전. 새 컬럼이라 적용 뒤 migrate_audit_columns_last.sql 을 다시 돌려 감사 컬럼을 맨 뒤로 정리한다.
  적용: sqlcmd -f 65001 -I -b -d <DB> -i dist/migrate_md_bop_insp_std.sql
*/
SET NOCOUNT ON;
IF COL_LENGTH('dbo.MD_Bop', 'InspStdID') IS NULL
    ALTER TABLE dbo.MD_Bop ADD InspStdID varchar(20) COLLATE DATABASE_DEFAULT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MD_Bop') AND name = N'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Bop'), 'InspStdID', 'ColumnId'))
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'QC 검사 기준 ID (MD_InspectionStandard) · varchar(20)',
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Bop',
         @level2type = N'COLUMN', @level2name = N'InspStdID';
GO
