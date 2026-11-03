-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_downtime.sql
--  고장 ↔ 라인 비가동 연결 (10-16 사용자 결정)
--
--  ① PP_LineDowntimeLog.FailureID — 그 비가동을 연 고장(MNT_FailureRegister). 웹 등록 고장의 「라인 정지」가
--     비가동을 새로 만들면 채운다. 고장 → 비가동 연결 자체는 기존 MNT_FailureRegister.DowntimeID(안돈이 이미 채움)이고,
--     이 컬럼은 "그 고장이 만든 행"인지 가리는 소유 표시다 — 고장 해결·종료 때 닫고, 고장 삭제 때 지우고,
--     PP-DTL 에서는 안돈 행처럼 원인·비고만 고친다. 안돈 행·PP-DTL 웹 행은 NULL. FK 없음(원칙).
--  ② MNT_WorkOrder.DowntimeID 삭제 — 작업지시 → 고장 → 비가동 순으로 따라간다.
--     기존 값이 있으면 어떤 스키마도 바꾸기 전에 중단한다. MNT_FailureRegister.DowntimeID 는 유지한다.
--
--  ALTER ADD 라 감사 컬럼 뒤에 붙는다 — 적용 뒤 dist/migrate_audit_columns_last.sql 을 다시 돌린다
--  (rebuild_db.sh 는 그 스크립트가 맨 끝이라 따로 할 일 없음).
--  재실행 안전, 순서 무관. 사전 검사와 DDL 을 한 트랜잭션에서 실행하며 실패 시 모두 되돌린다.
--  신 Web 과 함께 적용한다(구 Web 은 두 컬럼을 읽지 않아 먼저 적용해도 동작한다).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_failure_downtime.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

-- 외부 트랜잭션까지 롤백하지 않도록 단독 실행만 허용한다(RAISERROR 는 XACT_ABORT 를 적용하지 않음).
IF @@TRANCOUNT <> 0
BEGIN
    RAISERROR ('Run this migration without an existing transaction.', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.MNT_WorkOrder', 'U') IS NULL OR OBJECT_ID('dbo.PP_LineDowntimeLog', 'U') IS NULL
        THROW 51002, 'Required MNT_WorkOrder or PP_LineDowntimeLog table is missing or not visible.', 1;

    DECLARE @hasDowntime bit = CASE WHEN COL_LENGTH('dbo.MNT_WorkOrder', 'DowntimeID') IS NULL THEN 0 ELSE 1 END;
    IF @hasDowntime = 1
    BEGIN
        -- 삭제된 컬럼은 동적 SQL 로 검사한다. 검사부터 DROP 까지 기존 값의 추가 저장도 차단한다.
        DECLARE @withValue bigint;
        EXEC sys.sp_executesql
            N'SELECT @n = COUNT_BIG(*) FROM dbo.MNT_WorkOrder WITH (TABLOCKX, HOLDLOCK) WHERE DowntimeID IS NOT NULL;',
            N'@n bigint OUTPUT', @n = @withValue OUTPUT;
        IF @withValue > 0
            THROW 51000, 'MNT_WorkOrder.DowntimeID has values - check before dropping.', 1;
    END;

    IF COL_LENGTH('dbo.PP_LineDowntimeLog', 'FailureID') IS NULL
        EXEC sys.sp_executesql N'ALTER TABLE dbo.PP_LineDowntimeLog ADD FailureID INT NULL;';

    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
                   WHERE class = 1 AND major_id = OBJECT_ID('dbo.PP_LineDowntimeLog') AND name = 'MS_Description'
                     AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_LineDowntimeLog'), 'FailureID', 'ColumnId'))
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'이 비가동을 연 고장 ID (MNT_FailureRegister — 웹 고장의 라인 정지, 안돈·PP-DTL 행은 NULL) · int',
             @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'PP_LineDowntimeLog',
             @level2type=N'COLUMN',@level2name=N'FailureID';

    IF @hasDowntime = 1
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.extended_properties
                   WHERE class = 1 AND major_id = OBJECT_ID('dbo.MNT_WorkOrder') AND name = 'MS_Description'
                     AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MNT_WorkOrder'), 'DowntimeID', 'ColumnId'))
            EXEC sys.sp_dropextendedproperty @name=N'MS_Description',
                 @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'MNT_WorkOrder',
                 @level2type=N'COLUMN',@level2name=N'DowntimeID';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.MNT_WorkOrder DROP COLUMN DowntimeID;';
    END;

    COMMIT TRANSACTION;
    PRINT 'Failure downtime migration complete (existing values are preserved).';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT c.column_id AS Ord, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable AS Nullable
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.PP_LineDowntimeLog')
ORDER  BY c.column_id;
GO
