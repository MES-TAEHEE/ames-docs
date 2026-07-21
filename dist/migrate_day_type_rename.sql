/* ------------------------------------------------------------------
   migrate_day_type_rename.sql
   공통코드 그룹 CALENDAR_DAY_TYPE → DAY_TYPE 로 이름 변경.
   - MD_CodeGroup.GroupCode 변경
   - MD_CodeItem.GroupCode 및 CodeID 접두사(CALENDAR_DAY_TYPE_* → DAY_TYPE_*) 변경
   가드형: 이미 적용됐거나 대상이 없으면 아무 것도 하지 않음. 반복 실행 안전.
   참고: DayType 을 저장하는 테이블(MD_LineTimePattern, MD_Calendar, SYS_FactoryCalendar,
         PP_ProductionCalendarOverride)은 CodeValue(WORKDAY/WEEKEND/HOLIDAY/SPECIAL/OFF)를
         저장하므로 그룹/CodeID 변경 영향 없음.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

-- 1) 그룹 정의
IF EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'CALENDAR_DAY_TYPE')
   AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'DAY_TYPE')
BEGIN
    UPDATE dbo.MD_CodeGroup
       SET GroupCode = 'DAY_TYPE'
     WHERE GroupCode = 'CALENDAR_DAY_TYPE';
    PRINT 'MD_CodeGroup: CALENDAR_DAY_TYPE -> DAY_TYPE';
END
ELSE
    PRINT 'MD_CodeGroup: 변경 대상 없음(이미 적용 또는 미존재)';

-- 2) 아이템 GroupCode
UPDATE dbo.MD_CodeItem
   SET GroupCode = 'DAY_TYPE'
 WHERE GroupCode = 'CALENDAR_DAY_TYPE';
PRINT CONCAT('MD_CodeItem GroupCode 갱신 행수: ', @@ROWCOUNT);

-- 3) 아이템 CodeID 접두사 (CALENDAR_DAY_TYPE_* -> DAY_TYPE_*)
UPDATE dbo.MD_CodeItem
   SET CodeID = 'DAY_TYPE' + SUBSTRING(CodeID, LEN('CALENDAR_DAY_TYPE') + 1, 4000)
 WHERE CodeID LIKE 'CALENDAR_DAY_TYPE[_]%';
PRINT CONCAT('MD_CodeItem CodeID 갱신 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT GroupCode FROM dbo.MD_CodeGroup WHERE GroupCode IN ('CALENDAR_DAY_TYPE','DAY_TYPE');
SELECT CodeID, GroupCode, CodeValue FROM dbo.MD_CodeItem WHERE GroupCode IN ('CALENDAR_DAY_TYPE','DAY_TYPE') ORDER BY SortOrder;
