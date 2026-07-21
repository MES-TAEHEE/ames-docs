/* ------------------------------------------------------------------
   migrate_defect_status_rename.sql
   공통코드 그룹 DEFECT_CODE_STATUS → DEFECT_STATUS 로 이름 변경.
   - MD_CodeGroup.GroupCode 변경
   - MD_CodeItem.GroupCode 및 CodeID 접두사(DEFECT_CODE_STATUS_* → DEFECT_STATUS_*) 변경
   가드형: 이미 적용됐거나 대상이 없으면 아무 것도 하지 않음. 반복 실행 안전.
   참고: MD_DefectCode.Status 는 CodeValue(ACTIVE/INACTIVE)를 저장하므로 영향 없음.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

-- 1) 그룹 정의
IF EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'DEFECT_CODE_STATUS')
   AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'DEFECT_STATUS')
BEGIN
    UPDATE dbo.MD_CodeGroup
       SET GroupCode = 'DEFECT_STATUS'
     WHERE GroupCode = 'DEFECT_CODE_STATUS';
    PRINT 'MD_CodeGroup: DEFECT_CODE_STATUS -> DEFECT_STATUS';
END
ELSE
    PRINT 'MD_CodeGroup: 변경 대상 없음(이미 적용 또는 미존재)';

-- 2) 아이템 GroupCode
UPDATE dbo.MD_CodeItem
   SET GroupCode = 'DEFECT_STATUS'
 WHERE GroupCode = 'DEFECT_CODE_STATUS';
PRINT CONCAT('MD_CodeItem GroupCode 갱신 행수: ', @@ROWCOUNT);

-- 3) 아이템 CodeID 접두사 (DEFECT_CODE_STATUS_* -> DEFECT_STATUS_*)
UPDATE dbo.MD_CodeItem
   SET CodeID = 'DEFECT_STATUS' + SUBSTRING(CodeID, LEN('DEFECT_CODE_STATUS') + 1, 4000)
 WHERE CodeID LIKE 'DEFECT_CODE_STATUS[_]%';
PRINT CONCAT('MD_CodeItem CodeID 갱신 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT GroupCode FROM dbo.MD_CodeGroup WHERE GroupCode IN ('DEFECT_CODE_STATUS','DEFECT_STATUS');
SELECT CodeID, GroupCode, CodeValue FROM dbo.MD_CodeItem WHERE GroupCode IN ('DEFECT_CODE_STATUS','DEFECT_STATUS') ORDER BY SortOrder;
