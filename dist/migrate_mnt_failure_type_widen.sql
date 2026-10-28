-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_type_widen.sql
--  MNT_FailureRegister.FailureType varchar(15) → varchar(20) (10-28)
--
--  POP 안돈 보전 호출은 원인 코드(공통코드 ANDON_CAUSE.CodeValue — varchar(20))를 FailureType 에 넣는데
--  칸이 15자라 16자 이상 원인 코드는 예외 없이 잘려 저장됐다(지금 최대 8자라 실제로 잘린 행은 없다).
--  인덱스·제약이 없는 칸이라 넓히기만 한다. 순서 무관·재실행 안전, 구·신 바이너리 모두 그대로 동작.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_failure_type_widen.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.MNT_FailureRegister') AND name = 'FailureType' AND max_length < 20)
BEGIN
    ALTER TABLE dbo.MNT_FailureRegister ALTER COLUMN FailureType VARCHAR(20) NULL;
    PRINT 'MNT_FailureRegister.FailureType widened to varchar(20)';
END
ELSE PRINT 'MNT_FailureRegister.FailureType already varchar(20) or wider';
GO

IF EXISTS (SELECT 1 FROM sys.extended_properties
           WHERE major_id = OBJECT_ID('dbo.MNT_FailureRegister')
             AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MNT_FailureRegister'), 'FailureType', 'ColumnId')
             AND name = N'MS_Description' AND CAST(value AS nvarchar(400)) = N'Failure Type · varchar(15)')
    EXEC sys.sp_updateextendedproperty N'MS_Description', N'Failure Type · varchar(20)',
         N'SCHEMA', N'dbo', N'TABLE', N'MNT_FailureRegister', N'COLUMN', N'FailureType';
GO
