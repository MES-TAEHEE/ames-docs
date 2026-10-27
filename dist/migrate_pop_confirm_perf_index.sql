-- ════════════════════════════════════════════════════════════════════════
--  migrate_pop_confirm_perf_index.sql
--  POP 확정 경로 인덱스 — 실적 LotID · 사출 LOT 설비×캐비티×LotID · QC 번호 (10-27)
--
--  ① IX_PR_ProductionResult_LotID — 확정 LOT 의 불량 역분개(SP_POP_LOT_ReverseConfirmedResult_
--     GetProductionResult)가 LOT 잠금을 쥔 채 LotID 로 마지막 양품 실적을 찾는다. 인덱스가
--     없으면 실적 전체를 훑어 운영 기간에 비례해 느려진다.
--  ② IX_PR_InjLot_Equip_Pos_Lot — 사출 확정(SP_POP_INJ_ConfirmByLotCode_GetLot2)이 같은 설비·
--     캐비티의 직전 LOT 을 LotID 내림차순 TOP 1 로 찾는다. 전에는 그 설비 LOT 이력 전체에서
--     MAX(CreatedTS) 를 구했다.
--
--  스키마 변경 없음(인덱스만) — 적용 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pop_confirm_perf_index.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_ProductionResult_LotID' AND object_id = OBJECT_ID('dbo.PR_ProductionResult'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PR_ProductionResult_LotID ON dbo.PR_ProductionResult (LotID) INCLUDE (GoodQty);
    PRINT 'IX_PR_ProductionResult_LotID created';
END
ELSE PRINT 'IX_PR_ProductionResult_LotID already exists';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_InjLot_Equip_Pos_Lot' AND object_id = OBJECT_ID('dbo.PR_InjLot'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PR_InjLot_Equip_Pos_Lot ON dbo.PR_InjLot (EquipID, CavityPos, LotID);
    PRINT 'IX_PR_InjLot_Equip_Pos_Lot created';
END
ELSE PRINT 'IX_PR_InjLot_Equip_Pos_Lot already exists';

-- ③ QC 검사·NCR 번호 — SP_POP_QC_CreateInspection·CreateNcr 가 그 분 번호를 잠가 -2, -3 … 을 붙인다(10-27).
--    인덱스가 없으면 그 잠금이 표 전체에 걸린다. 일반 인덱스(유니크 아님 — 옛 중복 번호가 남아 있을 수 있다)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QC_Inspection_InspectionNo' AND object_id = OBJECT_ID('dbo.QC_Inspection'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_QC_Inspection_InspectionNo ON dbo.QC_Inspection (InspectionNo);
    PRINT 'IX_QC_Inspection_InspectionNo created';
END
ELSE PRINT 'IX_QC_Inspection_InspectionNo already exists';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QC_NCR_NcrNumber' AND object_id = OBJECT_ID('dbo.QC_NCR'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_QC_NCR_NcrNumber ON dbo.QC_NCR (NcrNumber);
    PRINT 'IX_QC_NCR_NcrNumber created';
END
ELSE PRINT 'IX_QC_NCR_NcrNumber already exists';
GO

SELECT OBJECT_NAME(i.object_id) AS TableName, i.name AS IndexName, c.name AS ColumnName, ic.is_included_column AS Included
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.name IN ('IX_PR_ProductionResult_LotID', 'IX_PR_InjLot_Equip_Pos_Lot', 'IX_QC_Inspection_InspectionNo', 'IX_QC_NCR_NcrNumber')
ORDER  BY TableName, ic.is_included_column, ic.key_ordinal;
GO
