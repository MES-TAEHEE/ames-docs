/* ------------------------------------------------------------------
   cleanup_legacy_item_data_dev.sql  (개발 DB 전용)
   MD_Item 에 없는 품번(BOM Master List 재적재로 사라진 SEMS 추출분·데모 품번)을 참조하는
   생산계획·실적 데이터를 지운다. 창고·FG 재고 LOT(WH/LOCAL/CKD/RELEASE)·MNT·감사 로그는 건드리지 않는다.
   테스트 잔재(CreatedBy='ITEST' WO)도 같이 지운다 — 통합 테스트가 실패한 채 남긴 행이 POP 화면에 섞인다.

   전체가 트랜잭션 하나. Mode=ROLLBACK 이면 건수만 보고 되돌리고, COMMIT 이면 반영한다.
   실행:
     sqlcmd -S 192.168.1.100 -d AMES_DEV -U ames_app -P <pw> -f 65001 -I -b -v Mode=ROLLBACK -i dist\cleanup_legacy_item_data_dev.sql
     → 건수 확인 후 -v Mode=COMMIT
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50100, N'Mode 는 ROLLBACK 또는 COMMIT', 1;
PRINT CONCAT(N'대상: ', @@SERVERNAME, N' / ', DB_NAME(), N' / Mode=$(Mode)');
BEGIN TRAN;

-- 1) 대상 WO: 품번이 MD_Item 에 없거나 테스트 잔재
DECLARE @wo TABLE (WoID int PRIMARY KEY);
INSERT INTO @wo
SELECT w.WoID FROM dbo.PP_WorkOrder w
LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
WHERE i.ItemNo IS NULL OR w.CreatedBy = 'ITEST';
DECLARE @nWo int = (SELECT COUNT(*) FROM @wo);
PRINT CONCAT(N'§1 대상 WO: ', @nWo, N' 건');

-- 2) 대상 LOT: 대상 WO 의 LOT, 또는 INJ/IMG/FINAL 공정의 구 품번 LOT (창고 LOT 제외)
DECLARE @lot TABLE (LotID int PRIMARY KEY);
INSERT INTO @lot
SELECT l.LotID FROM dbo.tbl_Lot l
LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo
WHERE l.WoID IN (SELECT WoID FROM @wo)
   OR (l.ProcessCode IN ('INJ','IMG','FINAL') AND i.ItemNo IS NULL);
DECLARE @nLot int = (SELECT COUNT(*) FROM @lot);
PRINT CONCAT(N'§2 대상 LOT: ', @nLot, N' 건');

-- 3) LOT 자식 → LOT
DELETE FROM dbo.PR_RobotInspection WHERE LotID IN (SELECT LotID FROM @lot);   PRINT CONCAT(N'§3 PR_RobotInspection: ', @@ROWCOUNT);
DELETE FROM dbo.FG_CustomerReturn  WHERE LotID IN (SELECT LotID FROM @lot);   PRINT CONCAT(N'§3 FG_CustomerReturn: ', @@ROWCOUNT);
DELETE FROM dbo.PR_DefectDetail    WHERE LotID IN (SELECT LotID FROM @lot) OR WoID IN (SELECT WoID FROM @wo);
                                                                              PRINT CONCAT(N'§3 PR_DefectDetail: ', @@ROWCOUNT);
DELETE FROM dbo.PR_ProductionResult WHERE LotID IN (SELECT LotID FROM @lot) OR WoID IN (SELECT WoID FROM @wo);
                                                                              PRINT CONCAT(N'§3 PR_ProductionResult: ', @@ROWCOUNT);
DELETE FROM dbo.PR_ImgLot WHERE LotID IN (SELECT LotID FROM @lot);            PRINT CONCAT(N'§3 PR_ImgLot: ', @@ROWCOUNT);
DELETE FROM dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @lot);            PRINT CONCAT(N'§3 PR_InjLot: ', @@ROWCOUNT);
UPDATE dbo.tbl_Lot SET ParentLotID = NULL WHERE ParentLotID IN (SELECT LotID FROM @lot) AND LotID NOT IN (SELECT LotID FROM @lot);
                                                                              PRINT CONCAT(N'§3 tbl_Lot 부모 해제: ', @@ROWCOUNT);
DELETE FROM dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @lot);              PRINT CONCAT(N'§3 tbl_Lot: ', @@ROWCOUNT);

-- 4) WO 자식 → WO
DELETE FROM dbo.PP_LineSchedule WHERE WoID IN (SELECT WoID FROM @wo)
   OR (EntryType = 'MC' AND RefType = 'WO' AND RefID IN (SELECT WoID FROM @wo));
                                                                              PRINT CONCAT(N'§4 PP_LineSchedule: ', @@ROWCOUNT);
DELETE FROM dbo.PP_MRPResultWo      WHERE WoID IN (SELECT WoID FROM @wo);     PRINT CONCAT(N'§4 PP_MRPResultWo: ', @@ROWCOUNT);
DELETE FROM dbo.PR_WoAcceptance     WHERE WoID IN (SELECT WoID FROM @wo);     PRINT CONCAT(N'§4 PR_WoAcceptance: ', @@ROWCOUNT);
DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID IN (SELECT WoID FROM @wo);     PRINT CONCAT(N'§4 PP_WorkOrderRouting: ', @@ROWCOUNT);
DELETE FROM dbo.PP_WorkOrder        WHERE WoID IN (SELECT WoID FROM @wo);     PRINT CONCAT(N'§4 PP_WorkOrder: ', @@ROWCOUNT);

-- 5) 구 품번 수주 (PO-SYNC 가 받은 구매 자재 품번 8건은 MD_Item 에 있으므로 남는다)
DELETE c FROM dbo.PP_CustomerOrder c LEFT JOIN dbo.MD_Item i ON i.ItemNo = c.ItemNo WHERE i.ItemNo IS NULL;
                                                                              PRINT CONCAT(N'§5 PP_CustomerOrder: ', @@ROWCOUNT);

-- 6) 구 품번 BOP·금형 매핑 (금형 자체는 남긴다 — 설비 상태·PM 이력이 참조할 수 있다)
DELETE b FROM dbo.MD_Bop      b LEFT JOIN dbo.MD_Item i ON i.ItemNo = b.ItemNo WHERE i.ItemNo IS NULL;
                                                                              PRINT CONCAT(N'§6 MD_Bop: ', @@ROWCOUNT);
DELETE m FROM dbo.MD_MoldItem m LEFT JOIN dbo.MD_Item i ON i.ItemNo = m.ItemNo WHERE i.ItemNo IS NULL;
                                                                              PRINT CONCAT(N'§6 MD_MoldItem: ', @@ROWCOUNT);

-- 7) 잔여 고아 확인 — 전부 0 이어야 한다
SELECT 'PP_CustomerOrder' AS Tbl, COUNT(*) AS Orphans FROM dbo.PP_CustomerOrder c LEFT JOIN dbo.MD_Item i ON i.ItemNo = c.ItemNo WHERE i.ItemNo IS NULL
UNION ALL SELECT 'PP_WorkOrder',   COUNT(*) FROM dbo.PP_WorkOrder w LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo WHERE i.ItemNo IS NULL
UNION ALL SELECT 'tbl_Lot(INJ/IMG/FINAL)', COUNT(*) FROM dbo.tbl_Lot l LEFT JOIN dbo.MD_Item i ON i.ItemNo = l.ItemNo WHERE l.ProcessCode IN ('INJ','IMG','FINAL') AND i.ItemNo IS NULL
UNION ALL SELECT 'MD_Bop',         COUNT(*) FROM dbo.MD_Bop b LEFT JOIN dbo.MD_Item i ON i.ItemNo = b.ItemNo WHERE i.ItemNo IS NULL
UNION ALL SELECT 'MD_MoldItem',    COUNT(*) FROM dbo.MD_MoldItem m LEFT JOIN dbo.MD_Item i ON i.ItemNo = m.ItemNo WHERE i.ItemNo IS NULL;

IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT — 반영됨'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK — 시험 실행, 반영 안 됨'; END;
GO
