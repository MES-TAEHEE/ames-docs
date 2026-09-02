-- ════════════════════════════════════════════════════════════════════════
-- migrate_inj_lot_print_claim.sql — 라벨 발행 선점 컬럼 (Pop 디스패처용)
--
--   라벨 발행 주체가 InjAgent → Pop 으로 이전되면서, 같은 라인에 터미널이
--   여러 대 있어도 한 장만 나가도록 DB 에서 원자적으로 선점해야 한다.
--   PrintedCount 를 선점 플래그로 쓰지 않는 이유: 출력 전에 올리면
--   프린터 장애 시 "카운트는 1인데 실물 라벨은 없는" 거짓 상태가 남는다.
--
-- 선행: dist/migrate_inj_agent.sql (PR_InjLot 생성).
-- 비파괴·재실행 가능(idempotent). 적용 (가드가 뒤 배치까지 멈추려면 -b 필수):
--   sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_inj_lot_print_claim.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 선행 스크립트 가드. 이게 없으면 OBJECT_ID 가 NULL → sys.columns 조회가 0행 →
-- NOT EXISTS 가 참이 되어 ALTER TABLE 이 "Invalid object name" 으로 죽는다.
-- (RETURN 은 이 배치만 끝내므로, 뒤 배치까지 멈추려면 sqlcmd -b 가 필요하다.)
IF OBJECT_ID(N'dbo.PR_InjLot', N'U') IS NULL
BEGIN
  RAISERROR(N'PR_InjLot 없음 — dist/migrate_inj_agent.sql 를 먼저 적용하세요.', 16, 1);
  RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'PrintClaimTS')
  ALTER TABLE dbo.PR_InjLot ADD [PrintClaimTS] DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'PrintClaimStation')
  ALTER TABLE dbo.PR_InjLot ADD [PrintClaimStation] VARCHAR(20) NULL;
GO

-- 클레임 쿼리는 (PrintedCount, LotID) 로 좁힌 뒤 PrintClaimTS 를 본다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'IX_PR_InjLot_PrintClaim')
  CREATE INDEX IX_PR_InjLot_PrintClaim
      ON dbo.PR_InjLot([PrintedCount], [LotID]) INCLUDE([PrintClaimTS]);
GO

-- 클레임 조인이 라인으로 좁혀지지 않으면, Pop 이 꺼진 라인의 미출력 LOT 을
-- 살아있는 다른 라인 터미널이 1초마다 전부 스캔한 뒤 버린다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.tbl_Lot') AND name = N'IX_tbl_Lot_Line')
  CREATE INDEX IX_tbl_Lot_Line ON dbo.tbl_Lot([LineID], [LotID]);
GO

PRINT N'✓ migrate_inj_lot_print_claim.sql applied';
GO
