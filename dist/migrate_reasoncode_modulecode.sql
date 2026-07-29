/* ============================================================
   migrate_reasoncode_modulecode.sql
   MD_ReasonCode.AppliesToModule → ModuleCode 컬럼 리네임
   - 가드형(재실행 안전), 수동 배포용
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_reasoncode_modulecode.sql
   ============================================================ */
SET NOCOUNT ON;

IF  COL_LENGTH('dbo.MD_ReasonCode', 'AppliesToModule') IS NOT NULL
AND COL_LENGTH('dbo.MD_ReasonCode', 'ModuleCode')      IS NULL
BEGIN
    EXEC sp_rename 'dbo.MD_ReasonCode.AppliesToModule', 'ModuleCode', 'COLUMN';
    PRINT 'MD_ReasonCode.AppliesToModule → ModuleCode 리네임 완료';
END
ELSE IF COL_LENGTH('dbo.MD_ReasonCode', 'ModuleCode') IS NOT NULL
    PRINT 'MD_ReasonCode.ModuleCode 이미 존재 — 스킵';
ELSE
    PRINT 'MD_ReasonCode.AppliesToModule 컬럼 없음 — 확인 필요';
GO
