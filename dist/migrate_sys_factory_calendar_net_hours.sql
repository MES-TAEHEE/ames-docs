/* ------------------------------------------------------------------
   migrate_sys_factory_calendar_net_hours.sql  (2026-10-03)

   SYS_FactoryCalendar.NetWorkHours  decimal(4,1) → decimal(4,2)  (순작업 시간 소수 2자리, 예 475분 = 7.92)
     · ALTER COLUMN 이라 컬럼 위치는 그대로다. 기존 값은 그대로(7.9 → 7.90) — 다시 계산하지 않는다
     · 최댓값 99.99 — 하루 순작업 시간(≤ 24)에 충분하다
     · 같이 바뀐 곳: SYS-005(Sys/Calendar.razor) 순작업 계산 반올림·표시 2자리,
       dbo.SP_SYS_FactoryCalendar_Fill(dist/migrate_sys_public_holiday.sql) 계산 2자리

   · 재실행 안전(소수 자릿수가 2가 아닐 때만 바꾼다). 적용: sqlcmd -f 65001 -I -b
   · 구 Web 과 함께 써도 예외는 없다 — 구 화면은 1자리로 반올림한 값을 넣을 뿐이다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.SYS_FactoryCalendar') AND name = 'NetWorkHours'
             AND (TYPE_NAME(user_type_id) <> 'decimal' OR precision <> 4 OR scale <> 2))
BEGIN
    ALTER TABLE dbo.SYS_FactoryCalendar ALTER COLUMN NetWorkHours decimal(4,2) NULL;
END

IF EXISTS (SELECT 1 FROM sys.extended_properties
           WHERE major_id = OBJECT_ID('dbo.SYS_FactoryCalendar') AND name = N'MS_Description'
             AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.SYS_FactoryCalendar'), 'NetWorkHours', 'ColumnId'))
    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Net Work Hours · decimal(4,2)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_FactoryCalendar', @level2type=N'COLUMN',@level2name=N'NetWorkHours';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Net Work Hours · decimal(4,2)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'SYS_FactoryCalendar', @level2type=N'COLUMN',@level2name=N'NetWorkHours';
GO

-- 확인
SELECT c.name, TYPE_NAME(c.user_type_id) AS type_name, c.precision, c.scale, c.column_id
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.SYS_FactoryCalendar') AND c.name = 'NetWorkHours';
