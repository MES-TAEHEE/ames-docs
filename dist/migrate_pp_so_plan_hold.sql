/* ------------------------------------------------------------------
   migrate_pp_so_plan_hold.sql  (2026-11-17)

   PP-003 계획 확정 목록에서 「지금은 WO 를 만들지 않을 수주」를 검토 대기로 보류한다(사용자 결정 B).
     PlanHold        1 = 보류(검토 대기) — PP-003 기본 목록에서 숨기고 「검토 대기 보기」에서만 보인다. 기본 0.
     PlanHoldReason  보류 사유(선택, 200자)
     PlanHoldBy      보류한 사번
     PlanHoldAt      보류한 시각
   · 수주 상태(Status)는 그대로다 — 계획 확정 목록에만 영향을 준다. 수량·납기가 바뀌어 다시 들어오면(PpRepository.UpsertCustomerOrders) 자동으로 풀린다.
   · 새 컬럼이라 적용 뒤 migrate_audit_columns_last.sql 을 다시 돌린다. 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 마이그레이션 → 신 Web·Api 순서(신 UpsertCustomerOrders 가 이 컬럼을 쓴다 — 없으면 PO Sync·업로드가 Invalid column name 으로 실패).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
GO
IF COL_LENGTH('dbo.PP_CustomerOrder', 'PlanHold') IS NULL
    ALTER TABLE dbo.PP_CustomerOrder ADD PlanHold bit NOT NULL CONSTRAINT DF_PP_CustomerOrder_PlanHold DEFAULT (0);
IF COL_LENGTH('dbo.PP_CustomerOrder', 'PlanHoldReason') IS NULL
    ALTER TABLE dbo.PP_CustomerOrder ADD PlanHoldReason nvarchar(200) COLLATE DATABASE_DEFAULT NULL;
IF COL_LENGTH('dbo.PP_CustomerOrder', 'PlanHoldBy') IS NULL
    ALTER TABLE dbo.PP_CustomerOrder ADD PlanHoldBy varchar(20) COLLATE DATABASE_DEFAULT NULL;
IF COL_LENGTH('dbo.PP_CustomerOrder', 'PlanHoldAt') IS NULL
    ALTER TABLE dbo.PP_CustomerOrder ADD PlanHoldAt datetime2(7) NULL;
GO
DECLARE @cols TABLE (c sysname, d nvarchar(200));
INSERT @cols VALUES
 (N'PlanHold',       N'PP-003 계획 확정 보류(검토 대기) · bit · 1 = 보류'),
 (N'PlanHoldReason', N'보류 사유(선택) · nvarchar(200)'),
 (N'PlanHoldBy',     N'보류한 사번 · varchar(20)'),
 (N'PlanHoldAt',     N'보류한 시각 · datetime2(7)');
DECLARE @c sysname, @d nvarchar(200);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT c, d FROM @cols;
OPEN cur; FETCH NEXT FROM cur INTO @c, @d;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.PP_CustomerOrder') AND name = N'MS_Description'
                   AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_CustomerOrder'), @c, 'ColumnId'))
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @d,
             @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_CustomerOrder', @level2type = N'COLUMN', @level2name = @c;
    FETCH NEXT FROM cur INTO @c, @d;
END
CLOSE cur; DEALLOCATE cur;
GO
SELECT PlanHold, COUNT(*) AS Orders FROM dbo.PP_CustomerOrder GROUP BY PlanHold;
GO
