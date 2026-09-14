-- ════════════════════════════════════════════════════════════════════════
--  migrate_lot_defect_rework.sql
--  LOT 단위 불량 등록 + REWORK 스테이션
--
--  · PR_DefectDetail 을 "불량 LOT 1건 = 1행" 으로 쓴다. Disposition NULL = 재작업 대기,
--    REWORKED / SCRAPPED = 판정 완료, LEGACY = 구 수량 입력 행(LotID 없음) 봉인.
--  · PR_InjLot / PR_ImgLot.ConfirmStatus 에 DEFECT · SCRAPPED 추가. NG_CONFIRMED 는 폐지 → SCRAPPED.
--  · REWORK 마스터: 공정코드 RWK, WC-RWK, LINE-RWK-01, ST-RWK-01.
--
--  순서 무관, 재실행 안전. 이 마이그레이션 없이 신 Pop 을 올리면 불량 등록·REWORK 대기열 조회가 예외다.
--  -I (QUOTED_IDENTIFIER ON) 필수 — tbl_Lot 필터 인덱스와 UX_PR_DefectDetail_OpenLot 때문에 없으면 Msg 1934
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -i dist/migrate_lot_defect_rework.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- §1 컬럼
IF COL_LENGTH('dbo.PR_DefectDetail', 'CauseCode') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD [CauseCode] VARCHAR(16) NULL;          -- MD_DefectCause.CauseCode (판정 시 필수)
IF COL_LENGTH('dbo.PR_DefectDetail', 'DispositionBy') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD [DispositionBy] VARCHAR(50) NULL;      -- 판정자 사번
IF COL_LENGTH('dbo.PR_DefectDetail', 'DispositionAt') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD [DispositionAt] DATETIME2 NULL;
IF COL_LENGTH('dbo.PR_DefectDetail', 'PriorStatus') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD [PriorStatus] VARCHAR(16) NULL;        -- 등록 당시 LOT 상태 RAW/CONFIRMED/NG_BLOCKED
IF COL_LENGTH('dbo.PR_DefectDetail', 'ReversalResultID') IS NULL
    ALTER TABLE dbo.PR_DefectDetail ADD [ReversalResultID] INT NULL;           -- 확정 후 LOT 역분개 실적 ID
PRINT 'PR_DefectDetail columns ensured';
GO

-- §2 구 수량 행 봉인 — LOT 없는 불량은 재작업 대상이 아니다
-- 구 시드가 LotID 를 NULL 대신 0 으로 남겼다 — 0 은 실제 LOT 이 아니므로 NULL 로 정규화해야 필터 유니크 인덱스와 충돌하지 않는다
UPDATE dbo.PR_DefectDetail
SET    LotID = NULL, ModifiedBy = 'MIGRATE', ModifiedTS = SYSDATETIME()
WHERE  LotID = 0;
PRINT CONCAT('LotID=0 sentinel rows normalized: ', @@ROWCOUNT);

UPDATE dbo.PR_DefectDetail
SET    Disposition = 'LEGACY', ModifiedBy = 'MIGRATE', ModifiedTS = SYSDATETIME()
WHERE  LotID IS NULL AND Disposition IS NULL;
PRINT CONCAT('legacy quantity rows sealed: ', @@ROWCOUNT);
GO

-- §3 NG_CONFIRMED → SCRAPPED (이미 종결된 건)
UPDATE d
SET    d.Disposition   = 'SCRAPPED',
       d.PriorStatus   = 'NG_BLOCKED',
       d.DispositionAt = e.ConfirmedAt,
       d.DispositionBy = LEFT(e.ConfirmedBy, 50),
       d.ModifiedBy    = 'MIGRATE', d.ModifiedTS = SYSDATETIME()
FROM   dbo.PR_DefectDetail d
JOIN   dbo.PR_InjLot e ON e.LotID = d.LotID
WHERE  e.ConfirmStatus = 'NG_CONFIRMED' AND d.Disposition IS NULL;
PRINT CONCAT('NG_CONFIRMED defect rows closed: ', @@ROWCOUNT);

UPDATE dbo.PR_InjLot
SET    ConfirmStatus = 'SCRAPPED', ModifiedBy = 'MIGRATE', ModifiedTS = SYSDATETIME()
WHERE  ConfirmStatus = 'NG_CONFIRMED';
PRINT CONCAT('PR_InjLot NG_CONFIRMED -> SCRAPPED: ', @@ROWCOUNT);

UPDATE l
SET    l.Status = 'SCRAP', l.QualityFlag = 'NG'
FROM   dbo.tbl_Lot l
JOIN   dbo.PR_InjLot e ON e.LotID = l.LotID
WHERE  e.ConfirmStatus = 'SCRAPPED' AND ISNULL(l.Status,'') <> 'SCRAP';
GO

-- §4 인덱스 — 대기열 중복 방지(필터 유니크) + 대기열 조회
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PR_DefectDetail_OpenLot' AND object_id = OBJECT_ID('dbo.PR_DefectDetail'))
    CREATE UNIQUE INDEX UX_PR_DefectDetail_OpenLot ON dbo.PR_DefectDetail(LotID) WHERE Disposition IS NULL AND LotID IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_DefectDetail_Open' AND object_id = OBJECT_ID('dbo.PR_DefectDetail'))
    CREATE INDEX IX_PR_DefectDetail_Open ON dbo.PR_DefectDetail(Disposition, DetectedAt) INCLUDE (LotID, ProcessCode, DefectCode);
-- REWORK 화면이 5초마다 Disposition + DispositionAt 으로 오늘 판정분을 센다 — DetectedAt 키로는 안 걸린다
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_DefectDetail_Decided' AND object_id = OBJECT_ID('dbo.PR_DefectDetail'))
    CREATE INDEX IX_PR_DefectDetail_Decided ON dbo.PR_DefectDetail(Disposition, DispositionAt);
PRINT 'PR_DefectDetail indexes ensured';
GO

-- §5 REWORK 마스터
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE CodeID = 'PROCESS_RWK')
    INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES ('PROCESS_RWK', 'PROCESS', 'RWK', N'재작업', N'Rework', NULL, 90, NULL, 1, N'POP REWORK 스테이션 전용 — 라우팅 단계 아님', 'MIGRATE');

UPDATE dbo.MD_CodeItem SET SortOrder = 90 WHERE CodeID = 'PROCESS_RWK' AND SortOrder = 80;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_WorkCenter WHERE WCID = 'WC-RWK')
    INSERT INTO dbo.MD_WorkCenter (WCID, WCName, ProcessCode, ActiveFlag, CreatedBy)
    VALUES ('WC-RWK', N'Rework Work Center', 'RWK', 1, 'MIGRATE');

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Line WHERE LineID = 'LINE-RWK-01')
    INSERT INTO dbo.MD_Line (LineID, LineName, LineNameEn, WCID, PlantCode, DailyCap, ShiftPattern, LotPrefix, RfidEnabledFlag, Status, CreatedBy)
    VALUES ('LINE-RWK-01', N'재작업 1', N'Rework 1', 'WC-RWK', 'SAV', NULL, '2-SHIFT', NULL, 0, 'ACTIVE', 'MIGRATE');

IF NOT EXISTS (SELECT 1 FROM dbo.MD_Station WHERE StationCode = 'ST-RWK-01')
    INSERT INTO dbo.MD_Station (StationCode, StationName, StationNameEn, LineID, OrderSeq, Status, CreatedBy)
    VALUES ('ST-RWK-01', N'재작업 스테이션 1', N'Rework Station 1', 'LINE-RWK-01', 10, 'ACTIVE', 'MIGRATE');
PRINT 'REWORK master ensured';
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.PR_DefectDetail')
  AND  c.name IN ('CauseCode','DispositionBy','DispositionAt','PriorStatus','ReversalResultID')
ORDER  BY c.column_id;
SELECT ConfirmStatus, COUNT(*) AS Cnt FROM dbo.PR_InjLot GROUP BY ConfirmStatus;
SELECT LineID, WCID, Status FROM dbo.MD_Line WHERE LineID = 'LINE-RWK-01';
GO
