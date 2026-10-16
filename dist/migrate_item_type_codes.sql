/* ------------------------------------------------------------------
   migrate_item_type_codes.sql
   MD_CodeItem GroupCode='ITEM_TYPE' 에 ASSY / SUB / MATERIAL 추가.

   배경: MD-003(/md/fd/items) 의 품목유형 드롭다운은 MD_CodeItem 의
         ITEM_TYPE 그룹을 읽는데, 여기에는 FINISHED/FABRIC/POWDER/RAW
         4개만 있었다. 반면 실제 MD_Item 데이터는 ASSY/SUB/MATERIAL 이
         대부분(1,189/1,191)이라 기존 품목 대다수가 드롭다운에서
         선택·필터되지 않았다. MD_Item.ItemType 에는 CHECK 제약이 없어
         저장은 되지만 화면에서만 값이 비어 보인다.

   CodeID 규칙: GroupCode + '_' + CodeValue (migrate_codeid_rule_fix.sql)
   SortOrder  : 기존 행을 건드리지 않도록 50/60/70 으로 뒤에 붙인다.
   가드형: 이미 있으면 아무 것도 하지 않음. 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO dbo.MD_CodeItem
       (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
SELECT  v.CodeID, v.GroupCode, v.CodeValue, v.CodeName, v.CodeNameEn, NULL, v.SortOrder, NULL, 1, v.Description, 'admin@ames.local'
FROM   (VALUES
  ('ITEM_TYPE_ASSY',     'ITEM_TYPE', 'ASSY',     N'조립품',   N'ASSY',     50, N'완성차사 납품 완제품 (SAP 평가클래스 1F7920/1F3100)'),
  ('ITEM_TYPE_SUB',      'ITEM_TYPE', 'SUB',      N'반제품',   N'SUB',      60, N'사내 공정 중간품 (SAP 평가클래스 1F7900)'),
  ('ITEM_TYPE_MATERIAL', 'ITEM_TYPE', 'MATERIAL', N'구매자재', N'MATERIAL', 70, N'외부 조달 자재·모듈 (SAP 평가클래스 1F3000)')
) v (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, Description)
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.CodeID = v.CodeID);

PRINT CONCAT(N'ITEM_TYPE 코드 추가: ', @@ROWCOUNT, N' 건');

COMMIT;
GO

-- 확인 1) ITEM_TYPE 코드 목록
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag
FROM   dbo.MD_CodeItem
WHERE  GroupCode = 'ITEM_TYPE'
ORDER  BY SortOrder;

-- 확인 2) MD_Item 에 쓰이는 ItemType 중 코드마스터에 없는 값 (0건이어야 정상)
SELECT i.ItemType, COUNT(*) AS 품목수
FROM   dbo.MD_Item i
WHERE  i.ItemType IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c
                   WHERE c.GroupCode = 'ITEM_TYPE' AND c.CodeValue = i.ItemType)
GROUP  BY i.ItemType
ORDER  BY 품목수 DESC;
