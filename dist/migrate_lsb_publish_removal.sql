-- ════════════════════════════════════════════════════════════════════════
--  migrate_lsb_publish_removal.sql
--  PP-LSB 라인 스케줄 「발행」 폐지 (10-14 사용자 결정)
--
--  발행은 (라인, 일자) 슬롯을 PUBLISHED 로 바꾸고 그날 분 단위 가동/구간을
--  PP_ProductionCalendarOverride 에 저장했지만, 결과를 읽는 곳이 PP-LSB 화면·쓰지 않는
--  Api /api/pp/calendar·MD 삭제 사용처 검사뿐이라 기능째 없앤다. 「적용」(DRAFT 저장)이
--  유일한 저장이 되고, POP·APS·PP-003·보고서·OEE 는 원래 상태를 보지 않아 변화가 없다.
--
--   §1 PP_LineSchedule — Status PUBLISHED → DRAFT, PublishedAt·PublishedBy 컬럼 삭제
--   §2 PP_ProductionCalendarOverride 테이블 삭제(발행 기록 — 개발 DB 는 10-08 시험 발행 1행)
--   §3 공통코드 SCHEDULE_STATUS — PUBLISHED 항목 삭제, DRAFT 이름 미게시 → 적용됨
--   §4 PP-LSB 화면 기본 권한 승인 자리 X(승인 기능 없음) + 역할 권한 승인 자리 X
--
--  재실행 안전. 신 Web 과 같이 반영한다 — 이 스크립트만 먼저 돌리면 구 Web 의 PP-LSB 조회가
--  PublishedAt 컬럼이 없어 실패하고, 신 Web 만 먼저 올리면 문제 없다(구 컬럼·테이블을 안 읽음).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_lsb_publish_removal.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- §1 PP_LineSchedule
IF COL_LENGTH('dbo.PP_LineSchedule', 'Status') IS NOT NULL
BEGIN
    DECLARE @n int;
    UPDATE dbo.PP_LineSchedule SET Status = 'DRAFT' WHERE Status = 'PUBLISHED';
    SET @n = @@ROWCOUNT;
    PRINT CONCAT('PP_LineSchedule PUBLISHED → DRAFT : ', @n, ' rows');
END
GO

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE dbo.PP_LineSchedule DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
FROM   sys.default_constraints dc
JOIN   sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE  dc.parent_object_id = OBJECT_ID('dbo.PP_LineSchedule') AND c.name IN ('PublishedAt', 'PublishedBy');
IF @sql <> N'' EXEC sp_executesql @sql;

IF COL_LENGTH('dbo.PP_LineSchedule', 'PublishedAt') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule DROP COLUMN PublishedAt;
    PRINT 'PP_LineSchedule.PublishedAt dropped';
END
IF COL_LENGTH('dbo.PP_LineSchedule', 'PublishedBy') IS NOT NULL
BEGIN
    ALTER TABLE dbo.PP_LineSchedule DROP COLUMN PublishedBy;
    PRINT 'PP_LineSchedule.PublishedBy dropped';
END
GO

-- §2 발행 기록 테이블
IF OBJECT_ID('dbo.PP_ProductionCalendarOverride', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.PP_ProductionCalendarOverride;
    PRINT 'PP_ProductionCalendarOverride dropped';
END
ELSE PRINT 'PP_ProductionCalendarOverride already absent';
GO

-- §3 공통코드 SCHEDULE_STATUS
DELETE dbo.MD_CodeItem WHERE GroupCode = 'SCHEDULE_STATUS' AND CodeValue = 'PUBLISHED';
IF @@ROWCOUNT > 0 PRINT 'SCHEDULE_STATUS.PUBLISHED deleted';
UPDATE dbo.MD_CodeItem
SET    CodeName = N'적용됨', CodeNameEn = N'Applied', ModifiedBy = 'migrate', ModifiedTS = SYSDATETIME()
WHERE  GroupCode = 'SCHEDULE_STATUS' AND CodeValue = 'DRAFT' AND CodeName = N'미게시';
IF @@ROWCOUNT > 0 PRINT 'SCHEDULE_STATUS.DRAFT renamed (미게시 → 적용됨)';
GO

-- §4 PP-LSB 승인 기능 없음 — 화면 기본 권한·역할 권한의 셋째 자리(A)를 X 로
IF COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria') IS NOT NULL
    EXEC sp_executesql N'
        UPDATE dbo.SYS_Screen
        SET    PermissionCriteria = LEFT(ISNULL(PermissionCriteria, ''___'') + ''___'', 2) + ''X''
        WHERE  ScreenCode = ''PP-LSB'' AND SUBSTRING(ISNULL(PermissionCriteria, ''___''), 3, 1) <> ''X'';
        IF @@ROWCOUNT > 0 PRINT ''SYS_Screen PP-LSB PermissionCriteria → __X'';';

UPDATE dbo.SYS_RolePermission
SET    PermissionLevel = LEFT(PermissionLevel + '___', 2) + 'X', ModifiedBy = 'migrate', ModifiedTS = SYSDATETIME()
WHERE  ScreenCode = 'PP-LSB' AND LEN(PermissionLevel) >= 1 AND SUBSTRING(PermissionLevel + '___', 3, 1) <> 'X';
DECLARE @r int = @@ROWCOUNT;
IF @r > 0 PRINT CONCAT('SYS_RolePermission PP-LSB approve slot → X : ', @r, ' rows');
GO

SELECT 'PP_LineSchedule' AS Obj, Status AS Val, COUNT(*) AS Cnt FROM dbo.PP_LineSchedule GROUP BY Status
UNION ALL SELECT 'SCHEDULE_STATUS', CodeValue + ' ' + CodeName, 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'SCHEDULE_STATUS'
UNION ALL SELECT 'PP-LSB role', RoleID + ' ' + PermissionLevel, 1 FROM dbo.SYS_RolePermission WHERE ScreenCode = 'PP-LSB';
SELECT COL_LENGTH('dbo.PP_LineSchedule', 'PublishedAt') AS PublishedAt, COL_LENGTH('dbo.PP_LineSchedule', 'PublishedBy') AS PublishedBy,
       OBJECT_ID('dbo.PP_ProductionCalendarOverride', 'U') AS OverrideTable;
GO
