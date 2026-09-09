-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_severity.sql
--  MNT_FailureRegister.Urgency → Severity 컬럼명 변경
--
--  MNT-002 고장 등록의 심각도는 공통코드 DEFECT_SEVERITY(MINOR/MAJOR/CRITICAL)를
--  기준으로 표시한다. 기존 값(URGENT/HIGH/MED/LOW)은 변환하지 않고 그대로 둔다.
--
--  스키마 변경: 컬럼 이름만 변경(sp_rename). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_mnt_failure_severity.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MNT_FailureRegister', 'Urgency') IS NOT NULL AND COL_LENGTH('dbo.MNT_FailureRegister', 'Severity') IS NULL
BEGIN
    EXEC sp_rename 'dbo.MNT_FailureRegister.Urgency', 'Severity', 'COLUMN';
    PRINT 'MNT_FailureRegister.Urgency renamed to Severity';
END
ELSE IF COL_LENGTH('dbo.MNT_FailureRegister', 'Severity') IS NOT NULL
    PRINT 'MNT_FailureRegister.Severity already exists';
ELSE
    RAISERROR('MNT_FailureRegister has neither Urgency nor Severity', 16, 1);
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MNT_FailureRegister') AND c.name IN ('Urgency', 'Severity');
GO
