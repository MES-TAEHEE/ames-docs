/* ------------------------------------------------------------------
   migrate_sys_public_holiday.sql  (2026-10-03)

   1) SYS_PublicHoliday — 국가별 공휴일 마스터(관리 화면 없음, SQL 로 관리)
        HolidayDate = 공장이 쉬는 날(토·일이면 연방 규칙으로 금·월 대체), ActualDate = 원래 기념일
        ActiveFlag  = 0 이면 그 날을 공휴일로 쓰지 않는다(공장이 쉬지 않는 공휴일)
      시드: 미국 연방 공휴일 11종 × 2026–2030 (55건, 대체 휴일 포함)
   2) dbo.SP_SYS_FactoryCalendar_Fill — 오늘(DB 날짜)부터 @Months 개월(기본 3, 마지막 날 = 오늘 + 3개월 − 1일)의
      SYS_FactoryCalendar 를 SYS-005 일정 생성 모달과 같은 규칙으로 채운다.
        · 이미 행이 있는 날짜는 건너뛴다(어떤 공장이든) — 화면에서 고친 날은 덮지 않는다
        · 토·일 = WEEKEND 1행 / SYS_PublicHoliday(활성) = HOLIDAY 1행(이름) / 그 밖 = WORKDAY 교대별 1행
        · 교대 = SHIFT_PATTERN(@ShiftPattern, 기본 SHIFT2_SCHEDULE).Attribute1 (없으면 A,B)
          시각 = WORK_SHIFT.Attribute2 'HHMM-HHMM'(없거나 틀리면 A 07:30–16:30 · B 16:30–01:30 · C 02:00–07:00)
          휴게 = A 65 · B 65 · 그 밖 0 분, 순작업 = 시각 차 − 휴게(시간, 소수 2자리) — 화면과 같은 값
        · 공장 = @PlantCode, 없으면 공통코드 PLANT 첫 행
        · 범위 안 연도에 공휴일이 하나도 없으면 아무것도 넣지 않고 오류(50002) — 공휴일이 근무일로 박히지 않게
        · @DryRun = 1 이면 넣을 행만 보여 준다
      SQL Server 에이전트 작업 등록은 dist/setup_job_factory_calendar_fill.sql (sysadmin 이 실행)

   · 재실행 안전(없는 테이블·공휴일 행만 넣고 프로시저는 CREATE OR ALTER). 적용: sqlcmd -f 65001 -I -b
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.SYS_PublicHoliday', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SYS_PublicHoliday (
        CountryCode  varchar(2)    NOT NULL,
        HolidayDate  date          NOT NULL,
        HolidayName  nvarchar(40)  NOT NULL,
        ActualDate   date          NOT NULL,
        ActiveFlag   bit           NOT NULL CONSTRAINT DF_SYS_PublicHoliday_ActiveFlag DEFAULT (1),
        CreatedBy    varchar(20)   NOT NULL,
        CreatedTS    datetime2(7)  NULL     CONSTRAINT DF_SYS_PublicHoliday_CreatedTS DEFAULT (sysdatetime()),
        ModifiedBy   varchar(20)   NULL,
        ModifiedTS   datetime2(7)  NULL,
        CONSTRAINT PK_SYS_PublicHoliday PRIMARY KEY CLUSTERED (CountryCode, HolidayDate)
    );

    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Public holiday master per country — no screen, maintained by SQL. Used by SP_SYS_FactoryCalendar_Fill', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'PK · Country Code (ISO 3166-1 alpha-2) · varchar(2)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'CountryCode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'PK · Holiday Date (day off; observed date when the actual day is a weekend) · date', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'HolidayDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Holiday Name (SYS_FactoryCalendar.HolidayName) · nvarchar(40)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'HolidayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Actual Date (the holiday itself) · date', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'ActualDate';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Active Flag (0 = plant works on this holiday) · bit', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'ActiveFlag';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Created By · varchar(20)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'CreatedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Created TS · datetime2', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'CreatedTS';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Modified By · varchar(20)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'ModifiedBy';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Modified TS · datetime2', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_PublicHoliday', @level2type=N'COLUMN',@level2name=N'ModifiedTS';
END
GO

-- 미국 연방 공휴일 2026–2030 (없는 행만 넣는다 — SQL 로 고친 ActiveFlag·이름은 보존)
MERGE dbo.SYS_PublicHoliday AS t
USING (VALUES
    ('US', '2026-01-01', N'New Year''s Day', '2026-01-01'),
    ('US', '2026-01-19', N'Martin Luther King Jr. Day', '2026-01-19'),
    ('US', '2026-02-16', N'Presidents'' Day', '2026-02-16'),
    ('US', '2026-05-25', N'Memorial Day', '2026-05-25'),
    ('US', '2026-06-19', N'Juneteenth', '2026-06-19'),
    ('US', '2026-07-03', N'Independence Day (Observed)', '2026-07-04'),
    ('US', '2026-09-07', N'Labor Day', '2026-09-07'),
    ('US', '2026-10-12', N'Columbus Day', '2026-10-12'),
    ('US', '2026-11-11', N'Veterans Day', '2026-11-11'),
    ('US', '2026-11-26', N'Thanksgiving Day', '2026-11-26'),
    ('US', '2026-12-25', N'Christmas Day', '2026-12-25'),
    ('US', '2027-01-01', N'New Year''s Day', '2027-01-01'),
    ('US', '2027-01-18', N'Martin Luther King Jr. Day', '2027-01-18'),
    ('US', '2027-02-15', N'Presidents'' Day', '2027-02-15'),
    ('US', '2027-05-31', N'Memorial Day', '2027-05-31'),
    ('US', '2027-06-18', N'Juneteenth (Observed)', '2027-06-19'),
    ('US', '2027-07-05', N'Independence Day (Observed)', '2027-07-04'),
    ('US', '2027-09-06', N'Labor Day', '2027-09-06'),
    ('US', '2027-10-11', N'Columbus Day', '2027-10-11'),
    ('US', '2027-11-11', N'Veterans Day', '2027-11-11'),
    ('US', '2027-11-25', N'Thanksgiving Day', '2027-11-25'),
    ('US', '2027-12-24', N'Christmas Day (Observed)', '2027-12-25'),
    ('US', '2027-12-31', N'New Year''s Day (Observed)', '2028-01-01'),
    ('US', '2028-01-17', N'Martin Luther King Jr. Day', '2028-01-17'),
    ('US', '2028-02-21', N'Presidents'' Day', '2028-02-21'),
    ('US', '2028-05-29', N'Memorial Day', '2028-05-29'),
    ('US', '2028-06-19', N'Juneteenth', '2028-06-19'),
    ('US', '2028-07-04', N'Independence Day', '2028-07-04'),
    ('US', '2028-09-04', N'Labor Day', '2028-09-04'),
    ('US', '2028-10-09', N'Columbus Day', '2028-10-09'),
    ('US', '2028-11-10', N'Veterans Day (Observed)', '2028-11-11'),
    ('US', '2028-11-23', N'Thanksgiving Day', '2028-11-23'),
    ('US', '2028-12-25', N'Christmas Day', '2028-12-25'),
    ('US', '2029-01-01', N'New Year''s Day', '2029-01-01'),
    ('US', '2029-01-15', N'Martin Luther King Jr. Day', '2029-01-15'),
    ('US', '2029-02-19', N'Presidents'' Day', '2029-02-19'),
    ('US', '2029-05-28', N'Memorial Day', '2029-05-28'),
    ('US', '2029-06-19', N'Juneteenth', '2029-06-19'),
    ('US', '2029-07-04', N'Independence Day', '2029-07-04'),
    ('US', '2029-09-03', N'Labor Day', '2029-09-03'),
    ('US', '2029-10-08', N'Columbus Day', '2029-10-08'),
    ('US', '2029-11-12', N'Veterans Day (Observed)', '2029-11-11'),
    ('US', '2029-11-22', N'Thanksgiving Day', '2029-11-22'),
    ('US', '2029-12-25', N'Christmas Day', '2029-12-25'),
    ('US', '2030-01-01', N'New Year''s Day', '2030-01-01'),
    ('US', '2030-01-21', N'Martin Luther King Jr. Day', '2030-01-21'),
    ('US', '2030-02-18', N'Presidents'' Day', '2030-02-18'),
    ('US', '2030-05-27', N'Memorial Day', '2030-05-27'),
    ('US', '2030-06-19', N'Juneteenth', '2030-06-19'),
    ('US', '2030-07-04', N'Independence Day', '2030-07-04'),
    ('US', '2030-09-02', N'Labor Day', '2030-09-02'),
    ('US', '2030-10-14', N'Columbus Day', '2030-10-14'),
    ('US', '2030-11-11', N'Veterans Day', '2030-11-11'),
    ('US', '2030-11-28', N'Thanksgiving Day', '2030-11-28'),
    ('US', '2030-12-25', N'Christmas Day', '2030-12-25')
      ) AS s (CountryCode, HolidayDate, HolidayName, ActualDate)
   ON t.CountryCode = s.CountryCode AND t.HolidayDate = s.HolidayDate
WHEN NOT MATCHED THEN
    INSERT (CountryCode, HolidayDate, HolidayName, ActualDate, ActiveFlag, CreatedBy)
    VALUES (s.CountryCode, s.HolidayDate, s.HolidayName, s.ActualDate, 1, 'system');
GO

CREATE OR ALTER PROCEDURE dbo.SP_SYS_FactoryCalendar_Fill
    @Months       int         = 3,
    @PlantCode    varchar(20) = NULL,
    @ShiftPattern varchar(20) = 'SHIFT2_SCHEDULE',
    @CountryCode  varchar(2)  = 'US',
    @Actor        varchar(20) = 'SQL-AGENT',
    @DryRun       bit         = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Months IS NULL OR @Months < 1 OR @Months > 24
        THROW 50001, N'@Months must be between 1 and 24.', 1;

    DECLARE @From date = CAST(SYSDATETIME() AS date);
    DECLARE @To   date = DATEADD(DAY, -1, DATEADD(MONTH, @Months, @From));

    IF @PlantCode IS NULL
        SELECT TOP (1) @PlantCode = CodeValue
        FROM   dbo.MD_CodeItem
        WHERE  GroupCode = 'PLANT' AND ISNULL(UseFlag, 1) = 1
        ORDER  BY ISNULL(SortOrder, 0), CodeValue;

    -- 범위 안 연도마다 공휴일이 등록돼 있어야 한다(없으면 공휴일이 근무일로 생성되고, 생성된 날은 다시 채우지 않는다)
    DECLARE @MissingYear int;
    WITH y AS (
        SELECT YEAR(@From) AS Yr
        UNION ALL SELECT Yr + 1 FROM y WHERE Yr < YEAR(@To)
    )
    SELECT TOP (1) @MissingYear = y.Yr
    FROM   y
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_PublicHoliday h
                       WHERE h.CountryCode = @CountryCode AND YEAR(h.HolidayDate) = y.Yr)
    ORDER  BY y.Yr;
    IF @MissingYear IS NOT NULL
    BEGIN
        DECLARE @Msg nvarchar(300) = CONCAT(N'SYS_PublicHoliday has no ', @CountryCode, N' holidays for ', @MissingYear,
                                            N'. Register that year before filling the factory calendar.');
        THROW 50002, @Msg, 1;
    END

    -- 교대: 패턴의 교대 목록 × WORK_SHIFT.Attribute2 시각(형식 오류·없음 → 화면과 같은 기본 시각)
    DECLARE @ShiftList nvarchar(200) =
        (SELECT TOP (1) Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode = 'SHIFT_PATTERN' AND CodeValue = @ShiftPattern);
    IF NULLIF(LTRIM(RTRIM(@ShiftList)), N'') IS NULL SET @ShiftList = N'A,B';

    DECLARE @Shift TABLE (ShiftCode varchar(10) PRIMARY KEY, StartTime time(7) NOT NULL, EndTime time(7) NOT NULL,
                          BreakMin int NOT NULL, NetHours decimal(4,2) NOT NULL);
    INSERT @Shift (ShiftCode, StartTime, EndTime, BreakMin, NetHours)
    SELECT c.Code, t.S, t.E, b.BreakMin,
           CAST(ROUND((((DATEDIFF(MINUTE, t.S, t.E) % 1440) + 1440) % 1440 - b.BreakMin) / 60.0, 2) AS decimal(4,2))
    FROM  (SELECT DISTINCT CAST(LTRIM(RTRIM(value)) AS varchar(10)) AS Code
           FROM STRING_SPLIT(@ShiftList, N',') WHERE LTRIM(RTRIM(value)) <> N'') c
    LEFT  JOIN dbo.MD_CodeItem w
           ON w.GroupCode = 'WORK_SHIFT' AND w.CodeValue = c.Code AND ISNULL(w.UseFlag, 1) = 1
    LEFT  JOIN (VALUES ('A', CAST('07:30' AS time(7)), CAST('16:30' AS time(7))),
                       ('B', CAST('16:30' AS time(7)), CAST('01:30' AS time(7))),
                       ('C', CAST('02:00' AS time(7)), CAST('07:00' AS time(7)))) f (Code, S, E)
           ON f.Code = c.Code
    CROSS APPLY (SELECT LTRIM(RTRIM(w.Attribute2)) AS A2) a
    CROSS APPLY (SELECT CASE WHEN CHARINDEX('-', a.A2) > 0 THEN LTRIM(RTRIM(LEFT(a.A2, CHARINDEX('-', a.A2) - 1))) END AS S4,
                        CASE WHEN CHARINDEX('-', a.A2) > 0 THEN LTRIM(RTRIM(SUBSTRING(a.A2, CHARINDEX('-', a.A2) + 1, 20))) END AS E4) p
    CROSS APPLY (SELECT CASE WHEN p.S4 = '2400' THEN CAST('00:00' AS time(7))
                             WHEN p.S4 LIKE '[0-2][0-9][0-5][0-9]' THEN TRY_CONVERT(time(7), STUFF(p.S4, 3, 0, ':')) END AS PS,
                        CASE WHEN p.E4 = '2400' THEN CAST('00:00' AS time(7))
                             WHEN p.E4 LIKE '[0-2][0-9][0-5][0-9]' THEN TRY_CONVERT(time(7), STUFF(p.E4, 3, 0, ':')) END AS PE) q
    CROSS APPLY (SELECT CASE WHEN q.PS IS NOT NULL AND q.PE IS NOT NULL THEN q.PS ELSE f.S END AS S,
                        CASE WHEN q.PS IS NOT NULL AND q.PE IS NOT NULL THEN q.PE ELSE f.E END AS E) t
    CROSS APPLY (SELECT CASE c.Code WHEN 'A' THEN 65 WHEN 'B' THEN 65 ELSE 0 END AS BreakMin) b
    WHERE t.S IS NOT NULL AND t.E IS NOT NULL;

    DECLARE @ShiftCount int = (SELECT COUNT(*) FROM @Shift);

    -- 같은 시각에 두 번 돌아도 한 번만 넣도록 잠근 뒤, 아직 행이 없는 날짜만 고른다
    BEGIN TRAN;
    EXEC sp_getapplock @Resource = 'SP_SYS_FactoryCalendar_Fill', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;

    DECLARE @Day TABLE (CalendarDate date PRIMARY KEY, DayType varchar(10) NOT NULL, HolidayName nvarchar(40) NULL);
    WITH n AS (
        SELECT TOP (DATEDIFF(DAY, @From, @To) + 1) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    )
    INSERT @Day (CalendarDate, DayType, HolidayName)
    SELECT d.D,
           -- 1900-01-01 은 월요일: % 7 이 5·6 이면 토·일 (DATEFIRST 설정과 무관)
           CASE WHEN DATEDIFF(DAY, '19000101', d.D) % 7 >= 5 THEN 'WEEKEND'
                WHEN h.HolidayDate IS NOT NULL               THEN 'HOLIDAY'
                ELSE 'WORKDAY' END,
           CASE WHEN DATEDIFF(DAY, '19000101', d.D) % 7 < 5 THEN h.HolidayName END
    FROM   n
    CROSS  APPLY (SELECT DATEADD(DAY, n.i, @From) AS D) d
    LEFT   JOIN dbo.SYS_PublicHoliday h
           ON h.CountryCode = @CountryCode AND h.HolidayDate = d.D AND h.ActiveFlag = 1
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_FactoryCalendar f WHERE f.CalendarDate = d.D);

    DECLARE @Rows TABLE (CalendarDate date, DayType varchar(10), HolidayName nvarchar(40), ShiftCount int, ShiftCode varchar(10),
                         StartTime time(7), EndTime time(7), BreakMinutes int, NetWorkHours decimal(4,2));
    INSERT @Rows
    SELECT d.CalendarDate, d.DayType, d.HolidayName, NULL, NULL, NULL, NULL, NULL, NULL
    FROM   @Day d
    WHERE  d.DayType <> 'WORKDAY' OR @ShiftCount = 0
    UNION ALL
    SELECT d.CalendarDate, d.DayType, NULL, @ShiftCount, s.ShiftCode, s.StartTime, s.EndTime, s.BreakMin, s.NetHours
    FROM   @Day d CROSS JOIN @Shift s
    WHERE  d.DayType = 'WORKDAY';

    IF @DryRun = 0
        INSERT dbo.SYS_FactoryCalendar (CalendarDate, DayType, HolidayName, ShiftCount, ShiftCode, StartTime, EndTime,
                                        BreakMinutes, NetWorkHours, CalendarYear, PlantCode, CreatedBy)
        SELECT r.CalendarDate, r.DayType, r.HolidayName, r.ShiftCount, r.ShiftCode, r.StartTime, r.EndTime,
               r.BreakMinutes, r.NetWorkHours, YEAR(r.CalendarDate), @PlantCode, @Actor
        FROM   @Rows r
        ORDER  BY r.CalendarDate, r.ShiftCode;

    COMMIT;

    IF @DryRun = 1
        SELECT CalendarDate, DayType, HolidayName, ShiftCount, ShiftCode,
               CONVERT(varchar(5), StartTime) AS StartTime, CONVERT(varchar(5), EndTime) AS EndTime, BreakMinutes, NetWorkHours
        FROM   @Rows ORDER BY CalendarDate, ShiftCode;

    SELECT @From AS FromDate, @To AS ToDate, @PlantCode AS PlantCode, @DryRun AS DryRun,
           (SELECT COUNT(*) FROM @Day)                              AS NewDays,
           (SELECT COUNT(*) FROM @Day WHERE DayType = 'WORKDAY')    AS Workdays,
           (SELECT COUNT(*) FROM @Day WHERE DayType = 'WEEKEND')    AS Weekends,
           (SELECT COUNT(*) FROM @Day WHERE DayType = 'HOLIDAY')    AS Holidays,
           (SELECT COUNT(*) FROM @Rows)                             AS RowsTotal;
END
GO

-- 확인
SELECT YEAR(HolidayDate) AS Yr, COUNT(*) AS Holidays FROM dbo.SYS_PublicHoliday WHERE CountryCode = 'US' GROUP BY YEAR(HolidayDate) ORDER BY 1;
SELECT OBJECT_ID('dbo.SP_SYS_FactoryCalendar_Fill', 'P') AS ProcObjectId;
