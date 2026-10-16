/* ------------------------------------------------------------------
   migrate_item_category_codes.sql
   공통코드 ITEM_CATEGORY(품목 카테고리) 신설 — ITEM_TYPE 의 FABRIC / POWDER / RAW 를
   ITEM_CATEGORY 로 옮긴다(SortOrder 10 / 20 / 30). 옮긴 뒤 ITEM_TYPE 에서는 삭제.

   배경: 원단·분말·원재료는 품목 "유형"(ASSY/SUB/MATERIAL — SAP 평가클래스)이 아니라
         "카테고리"로 쓴다. MD-003(/md/fd/items) 의 카테고리 입력란(MD_Item.ItemCategory)이
         자유 입력에서 ITEM_CATEGORY 공통코드 콤보로 바뀐다.
   · 명칭·설명·Attribute1·UseFlag 는 ITEM_TYPE 행에서 그대로 복사한다.
   · MD_Item.ItemType 이 이 세 값인 품목이 있으면 ITEM_TYPE 행은 지우지 않고 경고만 남긴다.
   · MD_Item.ItemCategory 의 기존 자유 입력 값은 건드리지 않는다(화면은 원문 그대로 보여 준다).
   CodeID 규칙: GroupCode + '_' + CodeValue. 가드형, 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'ITEM_CATEGORY')
BEGIN
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES ('ITEM_CATEGORY', N'품목 카테고리', N'Item category', NULL, 1, 'admin@ames.local', SYSDATETIME());
    PRINT N'✓ MD_CodeGroup ITEM_CATEGORY';
END
ELSE
    PRINT N'· MD_CodeGroup ITEM_CATEGORY 이미 존재';

INSERT INTO dbo.MD_CodeItem
       (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy, CreatedTS)
SELECT 'ITEM_CATEGORY_' + t.CodeValue, 'ITEM_CATEGORY', t.CodeValue, t.CodeName, t.CodeNameEn, NULL, v.SortOrder,
       t.Attribute1, t.UseFlag, t.Description, 'admin@ames.local', SYSDATETIME()
FROM   (VALUES ('FABRIC', 10), ('POWDER', 20), ('RAW', 30)) v (CodeValue, SortOrder)
JOIN   dbo.MD_CodeItem t ON t.GroupCode = 'ITEM_TYPE' AND t.CodeValue = v.CodeValue
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.CodeID = 'ITEM_CATEGORY_' + v.CodeValue);
PRINT CONCAT(N'ITEM_CATEGORY 코드 추가: ', @@ROWCOUNT, N' 건');

IF EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemType IN ('FABRIC', 'POWDER', 'RAW'))
    PRINT N'⚠ MD_Item.ItemType 에 FABRIC/POWDER/RAW 가 남아 있어 ITEM_TYPE 행을 지우지 않았다 — 품목 유형을 먼저 바꿀 것';
ELSE
BEGIN
    DELETE t
    FROM   dbo.MD_CodeItem t
    WHERE  t.GroupCode = 'ITEM_TYPE' AND t.CodeValue IN ('FABRIC', 'POWDER', 'RAW')
      AND  EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.CodeID = 'ITEM_CATEGORY_' + t.CodeValue);
    PRINT CONCAT(N'ITEM_TYPE 코드 삭제: ', @@ROWCOUNT, N' 건');
END

COMMIT;
GO

SELECT GroupCode, CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag
FROM   dbo.MD_CodeItem
WHERE  GroupCode IN ('ITEM_TYPE', 'ITEM_CATEGORY')
ORDER  BY GroupCode, SortOrder;
GO
