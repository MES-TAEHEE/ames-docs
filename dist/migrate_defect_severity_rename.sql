/* ------------------------------------------------------------------
   migrate_defect_severity_rename.sql
   공통코드 그룹 DEFECT_CODE_SEVERITY → DEFECT_SEVERITY 로 이름 변경.
   - MD_CodeGroup.GroupCode 변경
   - MD_CodeItem.GroupCode 및 CodeID 접두사(DEFECT_CODE_SEVERITY_* → DEFECT_SEVERITY_*) 변경
   가드형: 이미 적용됐거나 대상이 없으면 아무 것도 하지 않음. 반복 실행 안전.
   참고: MD_DefectCode.SeverityLevel 은 CodeValue(MINOR/MAJOR/CRITICAL)를 저장하므로 영향 없음.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

-- 1) 그룹 정의
IF EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'DEFECT_CODE_SEVERITY')
   AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'DEFECT_SEVERITY')
BEGIN
    UPDATE dbo.MD_CodeGroup
       SET GroupCode = 'DEFECT_SEVERITY'
     WHERE GroupCode = 'DEFECT_CODE_SEVERITY';
    PRINT 'MD_CodeGroup: DEFECT_CODE_SEVERITY -> DEFECT_SEVERITY';
END
ELSE
    PRINT 'MD_CodeGroup: 변경 대상 없음(이미 적용 또는 미존재)';

-- 2) 아이템 GroupCode
UPDATE dbo.MD_CodeItem
   SET GroupCode = 'DEFECT_SEVERITY'
 WHERE GroupCode = 'DEFECT_CODE_SEVERITY';
PRINT CONCAT('MD_CodeItem GroupCode 갱신 행수: ', @@ROWCOUNT);

-- 3) 아이템 CodeID 접두사 (DEFECT_CODE_SEVERITY_* -> DEFECT_SEVERITY_*)
UPDATE dbo.MD_CodeItem
   SET CodeID = 'DEFECT_SEVERITY' + SUBSTRING(CodeID, LEN('DEFECT_CODE_SEVERITY') + 1, 4000)
 WHERE CodeID LIKE 'DEFECT_CODE_SEVERITY[_]%';
PRINT CONCAT('MD_CodeItem CodeID 갱신 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT GroupCode FROM dbo.MD_CodeGroup WHERE GroupCode IN ('DEFECT_CODE_SEVERITY','DEFECT_SEVERITY');
SELECT CodeID, GroupCode, CodeValue FROM dbo.MD_CodeItem WHERE GroupCode IN ('DEFECT_CODE_SEVERITY','DEFECT_SEVERITY') ORDER BY SortOrder;
