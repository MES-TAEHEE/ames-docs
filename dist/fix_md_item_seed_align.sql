/* ------------------------------------------------------------------
   fix_md_item_seed_align.sql
   MD_Item 중 reseed_md_item_partmaster.sql 와 ItemType 이 어긋난 2건을 맞춘다.
   (1,191건 전수 대조 결과 불일치는 아래 2건뿐)

   ① 85725-PI000NNB  TRIM ASSY-PARTITION LWR
      웹(MD-003)에서 수기 등록돼 ItemType='FINISHED' 이고
      DefaultUOM/SafetyStock/PGN/ALC 가 전부 비어 있었다.
      SEMS 기준(ACD0020.ESTI_CLASS=1F3100, ZCD1010 PGN/PAC)으로 시드 값에 맞춘다.

   ② 81710-PI000NNB  TRIM ASSY-TAIL GATE, LWR
      시드에는 ASSY 인데 2026-07-17 웹에서 FINISHED 로 바뀌었다.
      ItemType 만 되돌린다 — 같이 손으로 넣은 ItemCategory='DOOR' 와
      ItemNameEN 은 시드에 없는 값이라 그대로 둔다.

   RoutingType 은 두 건 다 건드리지 않는다. 시드가 관리하지 않는 컬럼이고,
   두 품번 모두 참조하는 PP_WorkOrder 가 있다(85725: 3건, 81710: 1건).

   가드형: 이미 맞으면 0건 갱신. 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 변경 전
SELECT N'before' AS Phase, ItemNo, ItemType, DefaultUOM, SafetyStock, PGN, ALC, RoutingType, ItemCategory
FROM   dbo.MD_Item WHERE ItemNo IN ('85725-PI000NNB', '81710-PI000NNB') ORDER BY ItemNo;

BEGIN TRAN;

-- ① 85725: 비어 있던 컬럼까지 시드 값으로 채운다
UPDATE dbo.MD_Item
   SET ItemType    = 'ASSY',
       DefaultUOM  = 'EA',
       SafetyStock = 0,
       PGN         = 'Q100',
       ALC         = 'N00B',
       ModifiedBy  = N'admin',
       ModifiedTS  = SYSDATETIME()
WHERE  ItemNo = '85725-PI000NNB'
  AND (ISNULL(ItemType, '')    <> 'ASSY'
    OR ISNULL(DefaultUOM, '')  <> 'EA'
    OR ISNULL(SafetyStock, -1) <> 0
    OR ISNULL(PGN, '')         <> 'Q100'
    OR ISNULL(ALC, '')         <> 'N00B');
PRINT CONCAT(N'85725-PI000NNB 갱신: ', @@ROWCOUNT, N' 건');

-- ② 81710: ItemType 만 되돌린다 (나머지 컬럼은 이미 시드와 같거나 수기 입력값 보존)
UPDATE dbo.MD_Item
   SET ItemType   = 'ASSY',
       ModifiedBy = N'admin',
       ModifiedTS = SYSDATETIME()
WHERE  ItemNo = '81710-PI000NNB'
  AND  ISNULL(ItemType, '') <> 'ASSY';
PRINT CONCAT(N'81710-PI000NNB 갱신: ', @@ROWCOUNT, N' 건');

COMMIT;
GO

-- 변경 후
SELECT N'after' AS Phase, ItemNo, ItemType, DefaultUOM, SafetyStock, PGN, ALC, RoutingType, ItemCategory
FROM   dbo.MD_Item WHERE ItemNo IN ('85725-PI000NNB', '81710-PI000NNB') ORDER BY ItemNo;

-- ItemType 분포 (FINISHED 0건이어야 정상)
SELECT ItemType, COUNT(*) AS 품목수 FROM dbo.MD_Item GROUP BY ItemType ORDER BY 품목수 DESC;
