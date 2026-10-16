/* ------------------------------------------------------------------
   migrate_wh_code_groups.sql
   공통코드 그룹 WAREHOUSE_AISLE / WAREHOUSE_BAY / WAREHOUSE_SLOT / WAREHOUSE_ZONE
   → WH_AISLE / WH_BAY / WH_SLOT / WH_ZONE 로 이름 변경 (다른 WH_ 그룹과 접두사 통일).
   - MD_CodeGroup.GroupCode 변경
   - MD_CodeItem.GroupCode 및 CodeID 접두사(WAREHOUSE_*_xx → WH_*_xx) 변경
   가드형: 이미 적용됐거나 대상이 없으면 아무 것도 하지 않음. 반복 실행 안전.
   참고: 위치 마스터(MD-018 md/fd/location)는 CodeValue(A~I, 01~09)만 저장하므로
         그룹/CodeID 변경이 기존 데이터에 영향 없음. 화면은 ListCodeItems("WH_*") 로 함께 변경.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

DECLARE @map TABLE (OldCode VARCHAR(30), NewCode VARCHAR(30));
INSERT @map VALUES ('WAREHOUSE_AISLE','WH_AISLE'), ('WAREHOUSE_BAY','WH_BAY'),
                   ('WAREHOUSE_SLOT','WH_SLOT'),   ('WAREHOUSE_ZONE','WH_ZONE');

DECLARE @old VARCHAR(30), @new VARCHAR(30), @n INT;
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT OldCode, NewCode FROM @map;
OPEN cur; FETCH NEXT FROM cur INTO @old, @new;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = @old)
       AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = @new)
    BEGIN
        UPDATE dbo.MD_CodeGroup SET GroupCode = @new, ModifiedBy = 'migrate_wh_code_groups', ModifiedTS = SYSDATETIME()
         WHERE GroupCode = @old;
        PRINT CONCAT('MD_CodeGroup: ', @old, ' -> ', @new);

        UPDATE dbo.MD_CodeItem
           SET GroupCode = @new,
               CodeID    = @new + SUBSTRING(CodeID, LEN(@old) + 1, 4000),
               ModifiedBy = 'migrate_wh_code_groups', ModifiedTS = SYSDATETIME()
         WHERE GroupCode = @old AND CodeID LIKE @old + '[_]%';
        SET @n = @@ROWCOUNT;
        PRINT CONCAT('  MD_CodeItem 갱신 행수: ', @n);
    END
    ELSE
        PRINT CONCAT(@old, ': 변경 대상 없음(이미 적용 또는 미존재)');
    FETCH NEXT FROM cur INTO @old, @new;
END
CLOSE cur; DEALLOCATE cur;

COMMIT;

-- 확인
SELECT GroupCode, GroupName FROM dbo.MD_CodeGroup WHERE GroupCode LIKE 'WAREHOUSE[_]%' OR GroupCode IN ('WH_AISLE','WH_BAY','WH_SLOT','WH_ZONE') ORDER BY GroupCode;
SELECT GroupCode, COUNT(*) AS Items, MIN(CodeID) AS FirstCodeID FROM dbo.MD_CodeItem WHERE GroupCode LIKE 'WAREHOUSE[_]%' OR GroupCode IN ('WH_AISLE','WH_BAY','WH_SLOT','WH_ZONE') GROUP BY GroupCode ORDER BY GroupCode;
