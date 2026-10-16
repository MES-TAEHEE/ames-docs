/* ------------------------------------------------------------------
   fix_orphan_mold_refs.sql — 금형 마스터에 없는 금형을 가리키는 행 정리 (10-12 사용자 결정)

   10-03 금형 마스터 재적재(seed_md_mold_master.sql) 뒤에도 옛 금형 ID 를 가리키던 행을 지운다.
     MNT_MoldShotCount  MoldID 가 MD_Mold 에 없는 행(06 월 MNT 데모 시드 mnt-seed 4행)
     사출 LOT           PR_InjLot.MoldID 가 MD_Mold 에 없고, 미확정(RAW)이며 실적(PR_ProductionResult)·불량(PR_DefectDetail)·
                        자식 LOT(tbl_Lot.ParentLotID) 이 없는 LOT — PR_RobotInspection · PR_InjLot · tbl_Lot 의 그 LOT 행을 함께
   조건에서 벗어나는 LOT(확정·실적·불량·자식 있음)은 지우지 않고 목록만 보인다.

   Mode=ROLLBACK 이면 결과만 보여 주고 되돌리며, COMMIT 이면 반영한다.
     sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <pw> -C -f 65001 -I -b -v Mode=ROLLBACK -i dist\fix_orphan_mold_refs.sql
   10-12 개발·로컬 DB 적용(샷 카운트 4 · LOT 1 — A98I10042).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50100, N'Mode 는 ROLLBACK 또는 COMMIT', 1;
PRINT CONCAT(N'대상: ', @@SERVERNAME, N' / ', DB_NAME(), N' / Mode=$(Mode)');

DECLARE @lots TABLE (LotID int PRIMARY KEY);
INSERT @lots
SELECT p.LotID
FROM   dbo.PR_InjLot p
LEFT JOIN dbo.MD_Mold m ON m.MoldID = p.MoldID
WHERE  p.MoldID IS NOT NULL AND m.MoldID IS NULL
  AND  p.ConfirmStatus = 'RAW'
  AND  NOT EXISTS (SELECT 1 FROM dbo.PR_ProductionResult r WHERE r.LotID = p.LotID)
  AND  NOT EXISTS (SELECT 1 FROM dbo.PR_DefectDetail d WHERE d.LotID = p.LotID)
  AND  NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot c WHERE c.ParentLotID = p.LotID);

SELECT N'지울 샷 카운트' AS [구분], c.MoldShotCountID, c.MoldID, c.CurrentShots, c.LifetimeShots, c.CreatedBy
FROM   dbo.MNT_MoldShotCount c LEFT JOIN dbo.MD_Mold m ON m.MoldID = c.MoldID
WHERE  m.MoldID IS NULL;

SELECT N'지울 LOT' AS [구분], p.LotID, l.LotCode, l.ItemNo, p.MoldID, p.EquipID, p.ConfirmStatus,
       (SELECT COUNT(*) FROM dbo.PR_RobotInspection ri WHERE ri.LotID = p.LotID) AS RobotRows
FROM   @lots x JOIN dbo.PR_InjLot p ON p.LotID = x.LotID LEFT JOIN dbo.tbl_Lot l ON l.LotID = p.LotID;

SELECT N'남기는 LOT(조건 밖)' AS [구분], p.LotID, p.MoldID, p.ConfirmStatus
FROM   dbo.PR_InjLot p LEFT JOIN dbo.MD_Mold m ON m.MoldID = p.MoldID
WHERE  p.MoldID IS NOT NULL AND m.MoldID IS NULL AND p.LotID NOT IN (SELECT LotID FROM @lots);

BEGIN TRAN;
DELETE c FROM dbo.MNT_MoldShotCount c LEFT JOIN dbo.MD_Mold m ON m.MoldID = c.MoldID WHERE m.MoldID IS NULL;
PRINT CONCAT(N'샷 카운트 삭제: ', @@ROWCOUNT);
DELETE dbo.PR_RobotInspection WHERE LotID IN (SELECT LotID FROM @lots);
PRINT CONCAT(N'로봇 검사 삭제: ', @@ROWCOUNT);
DELETE dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @lots);
PRINT CONCAT(N'사출 LOT 삭제: ', @@ROWCOUNT);
DELETE dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @lots);
PRINT CONCAT(N'tbl_Lot 삭제: ', @@ROWCOUNT);

IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT — 반영됨'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK — 시험 실행, 반영 안 됨'; END;
GO
