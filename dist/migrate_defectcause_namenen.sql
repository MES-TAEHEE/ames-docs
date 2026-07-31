/* ============================================================
   migrate_defectcause_namenen.sql
   MD_DefectCause 에 CauseNameEn(영문명) 컬럼 추가
   - 가드형(재실행 안전)
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_defectcause_namenen.sql
   ============================================================ */
SET NOCOUNT ON;

IF COL_LENGTH('dbo.MD_DefectCause', 'CauseNameEn') IS NULL
BEGIN
    ALTER TABLE dbo.MD_DefectCause ADD CauseNameEn NVARCHAR(60) NULL;
    PRINT 'MD_DefectCause.CauseNameEn 컬럼 추가';
END
ELSE
    PRINT 'MD_DefectCause.CauseNameEn 이미 존재 — 스킵';
GO
