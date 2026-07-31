/* ============================================================
   migrate_drop_legacy_calendar_pattern.sql
   레거시/미사용 테이블 정리 — MD_Calendar, MD_LinePattern DROP
   - MD_Calendar   : 애플리케이션 미사용(SYS_FactoryCalendar와 중복). 0행.
   - MD_LinePattern: MD_LineTimePattern/Segment로 대체된 구 패턴 테이블.
                     앱 쓰기 경로 없음, DowntimeMonitor의 GetPattern 폴백만 참조했으나 제거됨.
                     (LineScheduleRepository가 자동 생성하던 코드도 함께 제거)
   - 가드형(재실행 안전), 참조 FK 없음
   - 접속: sqlcmd -S "localhost\MSSQLSERVER01" -U ames_app -P '!Dev2026' -d AMES_DEV -i dist\migrate_drop_legacy_calendar_pattern.sql
   ============================================================ */
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MD_Calendar', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.MD_Calendar;
    PRINT 'MD_Calendar 제거';
END
ELSE
    PRINT 'MD_Calendar 없음 — 스킵';

IF OBJECT_ID(N'dbo.MD_LinePattern', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.MD_LinePattern;
    PRINT 'MD_LinePattern 제거';
END
ELSE
    PRINT 'MD_LinePattern 없음 — 스킵';
GO
