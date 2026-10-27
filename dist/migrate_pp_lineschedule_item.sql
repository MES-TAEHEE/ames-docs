-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_lineschedule_item.sql
--  PP_LineSchedule.ItemNo — 슬롯이 실제로 만드는 품번 (10-27)
--
--  APS BOM 규칙 WO 는 INJ 단계에 자식(코어·레일 등)마다 슬롯을 놓지만 슬롯에 품번 칸이 없어, 읽는 쪽이 WO 헤더(완제품)
--  품번으로 보고 QtyPer 를 한 번 더 곱했다(이미 자식 단위인 수량 — APS 잠긴 칸·부하 과대, WO 생성의 기존 슬롯 차감 누락).
--  또 단계 품번 칸은 첫 자식 하나라 다른 자식은 POP 에서 PLAN 0·"WO 없음" 이었다.
--  NULL = 종전대로(그 라인 단계 품번 → WO 품번). 읽는 쪽 규칙: COALESCE(s.ItemNo, 단계 품번, WO 품번).
--  쓰는 쪽: PP-APS WO 생성의 사출 슬롯(대표·형제 0분 슬롯)이 자식 품번을 넣고, PP-LSB 「적용」은 지우기 전 값을 이어 받는다.
--
--  새 컬럼 — 마이그레이션 → migrate_pop_stored_procedures.sql → 신 Web·Pop 순서(구 바이너리는 이 컬럼을 모른 채 NULL 로 넣어 그대로 동작).
--  ALTER ADD 라 감사 컬럼 뒤에 붙는다 — 적용 뒤 migrate_audit_columns_last.sql 을 다시 돌린다. 재실행 안전.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pp_lineschedule_item.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PP_LineSchedule', 'ItemNo') IS NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule ADD ItemNo VARCHAR(20) NULL;
    PRINT 'PP_LineSchedule.ItemNo added';
END
ELSE PRINT 'PP_LineSchedule.ItemNo already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.PP_LineSchedule') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.PP_LineSchedule'), 'ItemNo', 'ColumnId')
                 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty N'MS_Description', N'슬롯이 만드는 품번 · varchar(20) — NULL 이면 그 라인 단계 품번 → WO 품번 (APS BOM 규칙 사출 슬롯은 자식 품번)',
         N'SCHEMA', N'dbo', N'TABLE', N'PP_LineSchedule', N'COLUMN', N'ItemNo';
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.PP_LineSchedule') AND c.name = 'ItemNo';
GO
