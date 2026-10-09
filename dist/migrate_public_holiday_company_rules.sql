/* ------------------------------------------------------------------
   migrate_public_holiday_company_rules.sql
   공휴일을 현대·기아 협력사 기준으로 (2026-10-09 사용자 결정)

   1) SYS_PublicHoliday 2026–2050 을 규칙대로 맞춘다 — 없는 행은 넣고, 있는 행은 ActiveFlag 만 규칙 값으로
      · 연방공휴일 11종 — 쉼(1): New Year's · Memorial · Independence · Labor · Thanksgiving · Christmas
                          일함(0): MLK · Presidents' · Juneteenth · Columbus · Veterans
        (Columbus Day 는 10-09 에 한때 삭제했다가 ActiveFlag 0 으로 다시 둔다 — 공휴일이었다는 기록을 남기려고)
      · 회사 휴일 3종(1): Day after Thanksgiving · Christmas Eve · New Year's Eve
        (Christmas Eve = Christmas 관측일 직전 평일, New Year's Eve = 다음 해 New Year's 관측일 직전 평일)
      · 여름·연말 생산 중단 주간은 넣지 않는다 — 해마다 SYS-005 에서 직접
   2) 이미 만든 SYS_FactoryCalendar(오늘 = DB 날짜 이후만 — 지난 날은 기록이라 건드리지 않는다)를 맞춘다
      · 활성 공휴일인데 WORKDAY 인 날 → 그 날 행을 지우고 HOLIDAY 1행(이름) — SP_SYS_FactoryCalendar_Fill 과 같은 모양
      · HOLIDAY 인데 그 이름이 비활성 공휴일인 날 → 같은 공장의 가장 가까운 근무일 교대 행을 복사해 WORKDAY
        (SYS-005 에서 직접 넣은 휴일은 이름이 공휴일 마스터와 다르므로 건드리지 않는다)
   · 작업지시 생산 마감일·라인 스케줄은 바꾸지 않는다 — 10-09 확인: 바뀌는 날(11-11·11-27·12-24·12-31)에 슬롯·발행·실적이 없고,
     열린 WO 184건은 모두 납기 10-24 이전이라 마감일 계산이 그대로다. 다른 DB 에 적용할 때는 그 DB 에서 다시 확인할 것.
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @Actor varchar(20) = 'HOLIDAY-RULE-1009';

MERGE dbo.SYS_PublicHoliday AS t
USING (VALUES
    ('US', '2026-01-01', N'New Year''s Day', '2026-01-01', 1),
    ('US', '2026-01-19', N'Martin Luther King Jr. Day', '2026-01-19', 0),
    ('US', '2026-02-16', N'Presidents'' Day', '2026-02-16', 0),
    ('US', '2026-05-25', N'Memorial Day', '2026-05-25', 1),
    ('US', '2026-06-19', N'Juneteenth', '2026-06-19', 0),
    ('US', '2026-07-03', N'Independence Day (Observed)', '2026-07-04', 1),
    ('US', '2026-09-07', N'Labor Day', '2026-09-07', 1),
    ('US', '2026-10-12', N'Columbus Day', '2026-10-12', 0),
    ('US', '2026-11-11', N'Veterans Day', '2026-11-11', 0),
    ('US', '2026-11-26', N'Thanksgiving Day', '2026-11-26', 1),
    ('US', '2026-11-27', N'Day after Thanksgiving', '2026-11-27', 1),
    ('US', '2026-12-24', N'Christmas Eve', '2026-12-24', 1),
    ('US', '2026-12-25', N'Christmas Day', '2026-12-25', 1),
    ('US', '2026-12-31', N'New Year''s Eve', '2026-12-31', 1),
    ('US', '2027-01-01', N'New Year''s Day', '2027-01-01', 1),
    ('US', '2027-01-18', N'Martin Luther King Jr. Day', '2027-01-18', 0),
    ('US', '2027-02-15', N'Presidents'' Day', '2027-02-15', 0),
    ('US', '2027-05-31', N'Memorial Day', '2027-05-31', 1),
    ('US', '2027-06-18', N'Juneteenth (Observed)', '2027-06-19', 0),
    ('US', '2027-07-05', N'Independence Day (Observed)', '2027-07-04', 1),
    ('US', '2027-09-06', N'Labor Day', '2027-09-06', 1),
    ('US', '2027-10-11', N'Columbus Day', '2027-10-11', 0),
    ('US', '2027-11-11', N'Veterans Day', '2027-11-11', 0),
    ('US', '2027-11-25', N'Thanksgiving Day', '2027-11-25', 1),
    ('US', '2027-11-26', N'Day after Thanksgiving', '2027-11-26', 1),
    ('US', '2027-12-23', N'Christmas Eve (Observed)', '2027-12-24', 1),
    ('US', '2027-12-24', N'Christmas Day (Observed)', '2027-12-25', 1),
    ('US', '2027-12-30', N'New Year''s Eve (Observed)', '2027-12-31', 1),
    ('US', '2027-12-31', N'New Year''s Day (Observed)', '2028-01-01', 1),
    ('US', '2028-01-17', N'Martin Luther King Jr. Day', '2028-01-17', 0),
    ('US', '2028-02-21', N'Presidents'' Day', '2028-02-21', 0),
    ('US', '2028-05-29', N'Memorial Day', '2028-05-29', 1),
    ('US', '2028-06-19', N'Juneteenth', '2028-06-19', 0),
    ('US', '2028-07-04', N'Independence Day', '2028-07-04', 1),
    ('US', '2028-09-04', N'Labor Day', '2028-09-04', 1),
    ('US', '2028-10-09', N'Columbus Day', '2028-10-09', 0),
    ('US', '2028-11-10', N'Veterans Day (Observed)', '2028-11-11', 0),
    ('US', '2028-11-23', N'Thanksgiving Day', '2028-11-23', 1),
    ('US', '2028-11-24', N'Day after Thanksgiving', '2028-11-24', 1),
    ('US', '2028-12-22', N'Christmas Eve (Observed)', '2028-12-24', 1),
    ('US', '2028-12-25', N'Christmas Day', '2028-12-25', 1),
    ('US', '2028-12-29', N'New Year''s Eve (Observed)', '2028-12-31', 1),
    ('US', '2029-01-01', N'New Year''s Day', '2029-01-01', 1),
    ('US', '2029-01-15', N'Martin Luther King Jr. Day', '2029-01-15', 0),
    ('US', '2029-02-19', N'Presidents'' Day', '2029-02-19', 0),
    ('US', '2029-05-28', N'Memorial Day', '2029-05-28', 1),
    ('US', '2029-06-19', N'Juneteenth', '2029-06-19', 0),
    ('US', '2029-07-04', N'Independence Day', '2029-07-04', 1),
    ('US', '2029-09-03', N'Labor Day', '2029-09-03', 1),
    ('US', '2029-10-08', N'Columbus Day', '2029-10-08', 0),
    ('US', '2029-11-12', N'Veterans Day (Observed)', '2029-11-11', 0),
    ('US', '2029-11-22', N'Thanksgiving Day', '2029-11-22', 1),
    ('US', '2029-11-23', N'Day after Thanksgiving', '2029-11-23', 1),
    ('US', '2029-12-24', N'Christmas Eve', '2029-12-24', 1),
    ('US', '2029-12-25', N'Christmas Day', '2029-12-25', 1),
    ('US', '2029-12-31', N'New Year''s Eve', '2029-12-31', 1),
    ('US', '2030-01-01', N'New Year''s Day', '2030-01-01', 1),
    ('US', '2030-01-21', N'Martin Luther King Jr. Day', '2030-01-21', 0),
    ('US', '2030-02-18', N'Presidents'' Day', '2030-02-18', 0),
    ('US', '2030-05-27', N'Memorial Day', '2030-05-27', 1),
    ('US', '2030-06-19', N'Juneteenth', '2030-06-19', 0),
    ('US', '2030-07-04', N'Independence Day', '2030-07-04', 1),
    ('US', '2030-09-02', N'Labor Day', '2030-09-02', 1),
    ('US', '2030-10-14', N'Columbus Day', '2030-10-14', 0),
    ('US', '2030-11-11', N'Veterans Day', '2030-11-11', 0),
    ('US', '2030-11-28', N'Thanksgiving Day', '2030-11-28', 1),
    ('US', '2030-11-29', N'Day after Thanksgiving', '2030-11-29', 1),
    ('US', '2030-12-24', N'Christmas Eve', '2030-12-24', 1),
    ('US', '2030-12-25', N'Christmas Day', '2030-12-25', 1),
    ('US', '2030-12-31', N'New Year''s Eve', '2030-12-31', 1),
    ('US', '2031-01-01', N'New Year''s Day', '2031-01-01', 1),
    ('US', '2031-01-20', N'Martin Luther King Jr. Day', '2031-01-20', 0),
    ('US', '2031-02-17', N'Presidents'' Day', '2031-02-17', 0),
    ('US', '2031-05-26', N'Memorial Day', '2031-05-26', 1),
    ('US', '2031-06-19', N'Juneteenth', '2031-06-19', 0),
    ('US', '2031-07-04', N'Independence Day', '2031-07-04', 1),
    ('US', '2031-09-01', N'Labor Day', '2031-09-01', 1),
    ('US', '2031-10-13', N'Columbus Day', '2031-10-13', 0),
    ('US', '2031-11-11', N'Veterans Day', '2031-11-11', 0),
    ('US', '2031-11-27', N'Thanksgiving Day', '2031-11-27', 1),
    ('US', '2031-11-28', N'Day after Thanksgiving', '2031-11-28', 1),
    ('US', '2031-12-24', N'Christmas Eve', '2031-12-24', 1),
    ('US', '2031-12-25', N'Christmas Day', '2031-12-25', 1),
    ('US', '2031-12-31', N'New Year''s Eve', '2031-12-31', 1),
    ('US', '2032-01-01', N'New Year''s Day', '2032-01-01', 1),
    ('US', '2032-01-19', N'Martin Luther King Jr. Day', '2032-01-19', 0),
    ('US', '2032-02-16', N'Presidents'' Day', '2032-02-16', 0),
    ('US', '2032-05-31', N'Memorial Day', '2032-05-31', 1),
    ('US', '2032-06-18', N'Juneteenth (Observed)', '2032-06-19', 0),
    ('US', '2032-07-05', N'Independence Day (Observed)', '2032-07-04', 1),
    ('US', '2032-09-06', N'Labor Day', '2032-09-06', 1),
    ('US', '2032-10-11', N'Columbus Day', '2032-10-11', 0),
    ('US', '2032-11-11', N'Veterans Day', '2032-11-11', 0),
    ('US', '2032-11-25', N'Thanksgiving Day', '2032-11-25', 1),
    ('US', '2032-11-26', N'Day after Thanksgiving', '2032-11-26', 1),
    ('US', '2032-12-23', N'Christmas Eve (Observed)', '2032-12-24', 1),
    ('US', '2032-12-24', N'Christmas Day (Observed)', '2032-12-25', 1),
    ('US', '2032-12-30', N'New Year''s Eve (Observed)', '2032-12-31', 1),
    ('US', '2032-12-31', N'New Year''s Day (Observed)', '2033-01-01', 1),
    ('US', '2033-01-17', N'Martin Luther King Jr. Day', '2033-01-17', 0),
    ('US', '2033-02-21', N'Presidents'' Day', '2033-02-21', 0),
    ('US', '2033-05-30', N'Memorial Day', '2033-05-30', 1),
    ('US', '2033-06-20', N'Juneteenth (Observed)', '2033-06-19', 0),
    ('US', '2033-07-04', N'Independence Day', '2033-07-04', 1),
    ('US', '2033-09-05', N'Labor Day', '2033-09-05', 1),
    ('US', '2033-10-10', N'Columbus Day', '2033-10-10', 0),
    ('US', '2033-11-11', N'Veterans Day', '2033-11-11', 0),
    ('US', '2033-11-24', N'Thanksgiving Day', '2033-11-24', 1),
    ('US', '2033-11-25', N'Day after Thanksgiving', '2033-11-25', 1),
    ('US', '2033-12-23', N'Christmas Eve (Observed)', '2033-12-24', 1),
    ('US', '2033-12-26', N'Christmas Day (Observed)', '2033-12-25', 1),
    ('US', '2033-12-30', N'New Year''s Eve (Observed)', '2033-12-31', 1),
    ('US', '2034-01-02', N'New Year''s Day (Observed)', '2034-01-01', 1),
    ('US', '2034-01-16', N'Martin Luther King Jr. Day', '2034-01-16', 0),
    ('US', '2034-02-20', N'Presidents'' Day', '2034-02-20', 0),
    ('US', '2034-05-29', N'Memorial Day', '2034-05-29', 1),
    ('US', '2034-06-19', N'Juneteenth', '2034-06-19', 0),
    ('US', '2034-07-04', N'Independence Day', '2034-07-04', 1),
    ('US', '2034-09-04', N'Labor Day', '2034-09-04', 1),
    ('US', '2034-10-09', N'Columbus Day', '2034-10-09', 0),
    ('US', '2034-11-10', N'Veterans Day (Observed)', '2034-11-11', 0),
    ('US', '2034-11-23', N'Thanksgiving Day', '2034-11-23', 1),
    ('US', '2034-11-24', N'Day after Thanksgiving', '2034-11-24', 1),
    ('US', '2034-12-22', N'Christmas Eve (Observed)', '2034-12-24', 1),
    ('US', '2034-12-25', N'Christmas Day', '2034-12-25', 1),
    ('US', '2034-12-29', N'New Year''s Eve (Observed)', '2034-12-31', 1),
    ('US', '2035-01-01', N'New Year''s Day', '2035-01-01', 1),
    ('US', '2035-01-15', N'Martin Luther King Jr. Day', '2035-01-15', 0),
    ('US', '2035-02-19', N'Presidents'' Day', '2035-02-19', 0),
    ('US', '2035-05-28', N'Memorial Day', '2035-05-28', 1),
    ('US', '2035-06-19', N'Juneteenth', '2035-06-19', 0),
    ('US', '2035-07-04', N'Independence Day', '2035-07-04', 1),
    ('US', '2035-09-03', N'Labor Day', '2035-09-03', 1),
    ('US', '2035-10-08', N'Columbus Day', '2035-10-08', 0),
    ('US', '2035-11-12', N'Veterans Day (Observed)', '2035-11-11', 0),
    ('US', '2035-11-22', N'Thanksgiving Day', '2035-11-22', 1),
    ('US', '2035-11-23', N'Day after Thanksgiving', '2035-11-23', 1),
    ('US', '2035-12-24', N'Christmas Eve', '2035-12-24', 1),
    ('US', '2035-12-25', N'Christmas Day', '2035-12-25', 1),
    ('US', '2035-12-31', N'New Year''s Eve', '2035-12-31', 1),
    ('US', '2036-01-01', N'New Year''s Day', '2036-01-01', 1),
    ('US', '2036-01-21', N'Martin Luther King Jr. Day', '2036-01-21', 0),
    ('US', '2036-02-18', N'Presidents'' Day', '2036-02-18', 0),
    ('US', '2036-05-26', N'Memorial Day', '2036-05-26', 1),
    ('US', '2036-06-19', N'Juneteenth', '2036-06-19', 0),
    ('US', '2036-07-04', N'Independence Day', '2036-07-04', 1),
    ('US', '2036-09-01', N'Labor Day', '2036-09-01', 1),
    ('US', '2036-10-13', N'Columbus Day', '2036-10-13', 0),
    ('US', '2036-11-11', N'Veterans Day', '2036-11-11', 0),
    ('US', '2036-11-27', N'Thanksgiving Day', '2036-11-27', 1),
    ('US', '2036-11-28', N'Day after Thanksgiving', '2036-11-28', 1),
    ('US', '2036-12-24', N'Christmas Eve', '2036-12-24', 1),
    ('US', '2036-12-25', N'Christmas Day', '2036-12-25', 1),
    ('US', '2036-12-31', N'New Year''s Eve', '2036-12-31', 1),
    ('US', '2037-01-01', N'New Year''s Day', '2037-01-01', 1),
    ('US', '2037-01-19', N'Martin Luther King Jr. Day', '2037-01-19', 0),
    ('US', '2037-02-16', N'Presidents'' Day', '2037-02-16', 0),
    ('US', '2037-05-25', N'Memorial Day', '2037-05-25', 1),
    ('US', '2037-06-19', N'Juneteenth', '2037-06-19', 0),
    ('US', '2037-07-03', N'Independence Day (Observed)', '2037-07-04', 1),
    ('US', '2037-09-07', N'Labor Day', '2037-09-07', 1),
    ('US', '2037-10-12', N'Columbus Day', '2037-10-12', 0),
    ('US', '2037-11-11', N'Veterans Day', '2037-11-11', 0),
    ('US', '2037-11-26', N'Thanksgiving Day', '2037-11-26', 1),
    ('US', '2037-11-27', N'Day after Thanksgiving', '2037-11-27', 1),
    ('US', '2037-12-24', N'Christmas Eve', '2037-12-24', 1),
    ('US', '2037-12-25', N'Christmas Day', '2037-12-25', 1),
    ('US', '2037-12-31', N'New Year''s Eve', '2037-12-31', 1),
    ('US', '2038-01-01', N'New Year''s Day', '2038-01-01', 1),
    ('US', '2038-01-18', N'Martin Luther King Jr. Day', '2038-01-18', 0),
    ('US', '2038-02-15', N'Presidents'' Day', '2038-02-15', 0),
    ('US', '2038-05-31', N'Memorial Day', '2038-05-31', 1),
    ('US', '2038-06-18', N'Juneteenth (Observed)', '2038-06-19', 0),
    ('US', '2038-07-05', N'Independence Day (Observed)', '2038-07-04', 1),
    ('US', '2038-09-06', N'Labor Day', '2038-09-06', 1),
    ('US', '2038-10-11', N'Columbus Day', '2038-10-11', 0),
    ('US', '2038-11-11', N'Veterans Day', '2038-11-11', 0),
    ('US', '2038-11-25', N'Thanksgiving Day', '2038-11-25', 1),
    ('US', '2038-11-26', N'Day after Thanksgiving', '2038-11-26', 1),
    ('US', '2038-12-23', N'Christmas Eve (Observed)', '2038-12-24', 1),
    ('US', '2038-12-24', N'Christmas Day (Observed)', '2038-12-25', 1),
    ('US', '2038-12-30', N'New Year''s Eve (Observed)', '2038-12-31', 1),
    ('US', '2038-12-31', N'New Year''s Day (Observed)', '2039-01-01', 1),
    ('US', '2039-01-17', N'Martin Luther King Jr. Day', '2039-01-17', 0),
    ('US', '2039-02-21', N'Presidents'' Day', '2039-02-21', 0),
    ('US', '2039-05-30', N'Memorial Day', '2039-05-30', 1),
    ('US', '2039-06-20', N'Juneteenth (Observed)', '2039-06-19', 0),
    ('US', '2039-07-04', N'Independence Day', '2039-07-04', 1),
    ('US', '2039-09-05', N'Labor Day', '2039-09-05', 1),
    ('US', '2039-10-10', N'Columbus Day', '2039-10-10', 0),
    ('US', '2039-11-11', N'Veterans Day', '2039-11-11', 0),
    ('US', '2039-11-24', N'Thanksgiving Day', '2039-11-24', 1),
    ('US', '2039-11-25', N'Day after Thanksgiving', '2039-11-25', 1),
    ('US', '2039-12-23', N'Christmas Eve (Observed)', '2039-12-24', 1),
    ('US', '2039-12-26', N'Christmas Day (Observed)', '2039-12-25', 1),
    ('US', '2039-12-30', N'New Year''s Eve (Observed)', '2039-12-31', 1),
    ('US', '2040-01-02', N'New Year''s Day (Observed)', '2040-01-01', 1),
    ('US', '2040-01-16', N'Martin Luther King Jr. Day', '2040-01-16', 0),
    ('US', '2040-02-20', N'Presidents'' Day', '2040-02-20', 0),
    ('US', '2040-05-28', N'Memorial Day', '2040-05-28', 1),
    ('US', '2040-06-19', N'Juneteenth', '2040-06-19', 0),
    ('US', '2040-07-04', N'Independence Day', '2040-07-04', 1),
    ('US', '2040-09-03', N'Labor Day', '2040-09-03', 1),
    ('US', '2040-10-08', N'Columbus Day', '2040-10-08', 0),
    ('US', '2040-11-12', N'Veterans Day (Observed)', '2040-11-11', 0),
    ('US', '2040-11-22', N'Thanksgiving Day', '2040-11-22', 1),
    ('US', '2040-11-23', N'Day after Thanksgiving', '2040-11-23', 1),
    ('US', '2040-12-24', N'Christmas Eve', '2040-12-24', 1),
    ('US', '2040-12-25', N'Christmas Day', '2040-12-25', 1),
    ('US', '2040-12-31', N'New Year''s Eve', '2040-12-31', 1),
    ('US', '2041-01-01', N'New Year''s Day', '2041-01-01', 1),
    ('US', '2041-01-21', N'Martin Luther King Jr. Day', '2041-01-21', 0),
    ('US', '2041-02-18', N'Presidents'' Day', '2041-02-18', 0),
    ('US', '2041-05-27', N'Memorial Day', '2041-05-27', 1),
    ('US', '2041-06-19', N'Juneteenth', '2041-06-19', 0),
    ('US', '2041-07-04', N'Independence Day', '2041-07-04', 1),
    ('US', '2041-09-02', N'Labor Day', '2041-09-02', 1),
    ('US', '2041-10-14', N'Columbus Day', '2041-10-14', 0),
    ('US', '2041-11-11', N'Veterans Day', '2041-11-11', 0),
    ('US', '2041-11-28', N'Thanksgiving Day', '2041-11-28', 1),
    ('US', '2041-11-29', N'Day after Thanksgiving', '2041-11-29', 1),
    ('US', '2041-12-24', N'Christmas Eve', '2041-12-24', 1),
    ('US', '2041-12-25', N'Christmas Day', '2041-12-25', 1),
    ('US', '2041-12-31', N'New Year''s Eve', '2041-12-31', 1),
    ('US', '2042-01-01', N'New Year''s Day', '2042-01-01', 1),
    ('US', '2042-01-20', N'Martin Luther King Jr. Day', '2042-01-20', 0),
    ('US', '2042-02-17', N'Presidents'' Day', '2042-02-17', 0),
    ('US', '2042-05-26', N'Memorial Day', '2042-05-26', 1),
    ('US', '2042-06-19', N'Juneteenth', '2042-06-19', 0),
    ('US', '2042-07-04', N'Independence Day', '2042-07-04', 1),
    ('US', '2042-09-01', N'Labor Day', '2042-09-01', 1),
    ('US', '2042-10-13', N'Columbus Day', '2042-10-13', 0),
    ('US', '2042-11-11', N'Veterans Day', '2042-11-11', 0),
    ('US', '2042-11-27', N'Thanksgiving Day', '2042-11-27', 1),
    ('US', '2042-11-28', N'Day after Thanksgiving', '2042-11-28', 1),
    ('US', '2042-12-24', N'Christmas Eve', '2042-12-24', 1),
    ('US', '2042-12-25', N'Christmas Day', '2042-12-25', 1),
    ('US', '2042-12-31', N'New Year''s Eve', '2042-12-31', 1),
    ('US', '2043-01-01', N'New Year''s Day', '2043-01-01', 1),
    ('US', '2043-01-19', N'Martin Luther King Jr. Day', '2043-01-19', 0),
    ('US', '2043-02-16', N'Presidents'' Day', '2043-02-16', 0),
    ('US', '2043-05-25', N'Memorial Day', '2043-05-25', 1),
    ('US', '2043-06-19', N'Juneteenth', '2043-06-19', 0),
    ('US', '2043-07-03', N'Independence Day (Observed)', '2043-07-04', 1),
    ('US', '2043-09-07', N'Labor Day', '2043-09-07', 1),
    ('US', '2043-10-12', N'Columbus Day', '2043-10-12', 0),
    ('US', '2043-11-11', N'Veterans Day', '2043-11-11', 0),
    ('US', '2043-11-26', N'Thanksgiving Day', '2043-11-26', 1),
    ('US', '2043-11-27', N'Day after Thanksgiving', '2043-11-27', 1),
    ('US', '2043-12-24', N'Christmas Eve', '2043-12-24', 1),
    ('US', '2043-12-25', N'Christmas Day', '2043-12-25', 1),
    ('US', '2043-12-31', N'New Year''s Eve', '2043-12-31', 1),
    ('US', '2044-01-01', N'New Year''s Day', '2044-01-01', 1),
    ('US', '2044-01-18', N'Martin Luther King Jr. Day', '2044-01-18', 0),
    ('US', '2044-02-15', N'Presidents'' Day', '2044-02-15', 0),
    ('US', '2044-05-30', N'Memorial Day', '2044-05-30', 1),
    ('US', '2044-06-20', N'Juneteenth (Observed)', '2044-06-19', 0),
    ('US', '2044-07-04', N'Independence Day', '2044-07-04', 1),
    ('US', '2044-09-05', N'Labor Day', '2044-09-05', 1),
    ('US', '2044-10-10', N'Columbus Day', '2044-10-10', 0),
    ('US', '2044-11-11', N'Veterans Day', '2044-11-11', 0),
    ('US', '2044-11-24', N'Thanksgiving Day', '2044-11-24', 1),
    ('US', '2044-11-25', N'Day after Thanksgiving', '2044-11-25', 1),
    ('US', '2044-12-23', N'Christmas Eve (Observed)', '2044-12-24', 1),
    ('US', '2044-12-26', N'Christmas Day (Observed)', '2044-12-25', 1),
    ('US', '2044-12-30', N'New Year''s Eve (Observed)', '2044-12-31', 1),
    ('US', '2045-01-02', N'New Year''s Day (Observed)', '2045-01-01', 1),
    ('US', '2045-01-16', N'Martin Luther King Jr. Day', '2045-01-16', 0),
    ('US', '2045-02-20', N'Presidents'' Day', '2045-02-20', 0),
    ('US', '2045-05-29', N'Memorial Day', '2045-05-29', 1),
    ('US', '2045-06-19', N'Juneteenth', '2045-06-19', 0),
    ('US', '2045-07-04', N'Independence Day', '2045-07-04', 1),
    ('US', '2045-09-04', N'Labor Day', '2045-09-04', 1),
    ('US', '2045-10-09', N'Columbus Day', '2045-10-09', 0),
    ('US', '2045-11-10', N'Veterans Day (Observed)', '2045-11-11', 0),
    ('US', '2045-11-23', N'Thanksgiving Day', '2045-11-23', 1),
    ('US', '2045-11-24', N'Day after Thanksgiving', '2045-11-24', 1),
    ('US', '2045-12-22', N'Christmas Eve (Observed)', '2045-12-24', 1),
    ('US', '2045-12-25', N'Christmas Day', '2045-12-25', 1),
    ('US', '2045-12-29', N'New Year''s Eve (Observed)', '2045-12-31', 1),
    ('US', '2046-01-01', N'New Year''s Day', '2046-01-01', 1),
    ('US', '2046-01-15', N'Martin Luther King Jr. Day', '2046-01-15', 0),
    ('US', '2046-02-19', N'Presidents'' Day', '2046-02-19', 0),
    ('US', '2046-05-28', N'Memorial Day', '2046-05-28', 1),
    ('US', '2046-06-19', N'Juneteenth', '2046-06-19', 0),
    ('US', '2046-07-04', N'Independence Day', '2046-07-04', 1),
    ('US', '2046-09-03', N'Labor Day', '2046-09-03', 1),
    ('US', '2046-10-08', N'Columbus Day', '2046-10-08', 0),
    ('US', '2046-11-12', N'Veterans Day (Observed)', '2046-11-11', 0),
    ('US', '2046-11-22', N'Thanksgiving Day', '2046-11-22', 1),
    ('US', '2046-11-23', N'Day after Thanksgiving', '2046-11-23', 1),
    ('US', '2046-12-24', N'Christmas Eve', '2046-12-24', 1),
    ('US', '2046-12-25', N'Christmas Day', '2046-12-25', 1),
    ('US', '2046-12-31', N'New Year''s Eve', '2046-12-31', 1),
    ('US', '2047-01-01', N'New Year''s Day', '2047-01-01', 1),
    ('US', '2047-01-21', N'Martin Luther King Jr. Day', '2047-01-21', 0),
    ('US', '2047-02-18', N'Presidents'' Day', '2047-02-18', 0),
    ('US', '2047-05-27', N'Memorial Day', '2047-05-27', 1),
    ('US', '2047-06-19', N'Juneteenth', '2047-06-19', 0),
    ('US', '2047-07-04', N'Independence Day', '2047-07-04', 1),
    ('US', '2047-09-02', N'Labor Day', '2047-09-02', 1),
    ('US', '2047-10-14', N'Columbus Day', '2047-10-14', 0),
    ('US', '2047-11-11', N'Veterans Day', '2047-11-11', 0),
    ('US', '2047-11-28', N'Thanksgiving Day', '2047-11-28', 1),
    ('US', '2047-11-29', N'Day after Thanksgiving', '2047-11-29', 1),
    ('US', '2047-12-24', N'Christmas Eve', '2047-12-24', 1),
    ('US', '2047-12-25', N'Christmas Day', '2047-12-25', 1),
    ('US', '2047-12-31', N'New Year''s Eve', '2047-12-31', 1),
    ('US', '2048-01-01', N'New Year''s Day', '2048-01-01', 1),
    ('US', '2048-01-20', N'Martin Luther King Jr. Day', '2048-01-20', 0),
    ('US', '2048-02-17', N'Presidents'' Day', '2048-02-17', 0),
    ('US', '2048-05-25', N'Memorial Day', '2048-05-25', 1),
    ('US', '2048-06-19', N'Juneteenth', '2048-06-19', 0),
    ('US', '2048-07-03', N'Independence Day (Observed)', '2048-07-04', 1),
    ('US', '2048-09-07', N'Labor Day', '2048-09-07', 1),
    ('US', '2048-10-12', N'Columbus Day', '2048-10-12', 0),
    ('US', '2048-11-11', N'Veterans Day', '2048-11-11', 0),
    ('US', '2048-11-26', N'Thanksgiving Day', '2048-11-26', 1),
    ('US', '2048-11-27', N'Day after Thanksgiving', '2048-11-27', 1),
    ('US', '2048-12-24', N'Christmas Eve', '2048-12-24', 1),
    ('US', '2048-12-25', N'Christmas Day', '2048-12-25', 1),
    ('US', '2048-12-31', N'New Year''s Eve', '2048-12-31', 1),
    ('US', '2049-01-01', N'New Year''s Day', '2049-01-01', 1),
    ('US', '2049-01-18', N'Martin Luther King Jr. Day', '2049-01-18', 0),
    ('US', '2049-02-15', N'Presidents'' Day', '2049-02-15', 0),
    ('US', '2049-05-31', N'Memorial Day', '2049-05-31', 1),
    ('US', '2049-06-18', N'Juneteenth (Observed)', '2049-06-19', 0),
    ('US', '2049-07-05', N'Independence Day (Observed)', '2049-07-04', 1),
    ('US', '2049-09-06', N'Labor Day', '2049-09-06', 1),
    ('US', '2049-10-11', N'Columbus Day', '2049-10-11', 0),
    ('US', '2049-11-11', N'Veterans Day', '2049-11-11', 0),
    ('US', '2049-11-25', N'Thanksgiving Day', '2049-11-25', 1),
    ('US', '2049-11-26', N'Day after Thanksgiving', '2049-11-26', 1),
    ('US', '2049-12-23', N'Christmas Eve (Observed)', '2049-12-24', 1),
    ('US', '2049-12-24', N'Christmas Day (Observed)', '2049-12-25', 1),
    ('US', '2049-12-30', N'New Year''s Eve (Observed)', '2049-12-31', 1),
    ('US', '2049-12-31', N'New Year''s Day (Observed)', '2050-01-01', 1),
    ('US', '2050-01-17', N'Martin Luther King Jr. Day', '2050-01-17', 0),
    ('US', '2050-02-21', N'Presidents'' Day', '2050-02-21', 0),
    ('US', '2050-05-30', N'Memorial Day', '2050-05-30', 1),
    ('US', '2050-06-20', N'Juneteenth (Observed)', '2050-06-19', 0),
    ('US', '2050-07-04', N'Independence Day', '2050-07-04', 1),
    ('US', '2050-09-05', N'Labor Day', '2050-09-05', 1),
    ('US', '2050-10-10', N'Columbus Day', '2050-10-10', 0),
    ('US', '2050-11-11', N'Veterans Day', '2050-11-11', 0),
    ('US', '2050-11-24', N'Thanksgiving Day', '2050-11-24', 1),
    ('US', '2050-11-25', N'Day after Thanksgiving', '2050-11-25', 1),
    ('US', '2050-12-23', N'Christmas Eve (Observed)', '2050-12-24', 1),
    ('US', '2050-12-26', N'Christmas Day (Observed)', '2050-12-25', 1),
    ('US', '2050-12-30', N'New Year''s Eve (Observed)', '2050-12-31', 1)
      ) AS s (CountryCode, HolidayDate, HolidayName, ActualDate, ActiveFlag)
   ON t.CountryCode = s.CountryCode AND t.HolidayDate = s.HolidayDate
WHEN MATCHED AND t.ActiveFlag <> s.ActiveFlag THEN
    UPDATE SET ActiveFlag = s.ActiveFlag, ModifiedBy = @Actor, ModifiedTS = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (CountryCode, HolidayDate, HolidayName, ActualDate, ActiveFlag, CreatedBy)
    VALUES (s.CountryCode, s.HolidayDate, s.HolidayName, s.ActualDate, s.ActiveFlag, @Actor);
PRINT CONCAT(N'SYS_PublicHoliday 추가·변경: ', @@ROWCOUNT, N' 건');

DECLARE @Today date = CAST(SYSDATETIME() AS date);

-- ① 활성 공휴일인데 근무일인 날 → HOLIDAY
DECLARE @ToHoliday TABLE (CalendarDate date, PlantCode varchar(20), HolidayName nvarchar(40));
INSERT @ToHoliday
SELECT DISTINCT c.CalendarDate, c.PlantCode, h.HolidayName
FROM   dbo.SYS_FactoryCalendar c
JOIN   dbo.SYS_PublicHoliday h ON h.CountryCode = 'US' AND h.HolidayDate = c.CalendarDate AND h.ActiveFlag = 1
WHERE  c.CalendarDate >= @Today AND c.DayType = 'WORKDAY';
DELETE c FROM dbo.SYS_FactoryCalendar c
JOIN   @ToHoliday t ON t.CalendarDate = c.CalendarDate AND ISNULL(t.PlantCode, '') = ISNULL(c.PlantCode, '')
WHERE  c.DayType = 'WORKDAY';
PRINT CONCAT(N'근무일 → 휴일 전환으로 지운 교대 행: ', @@ROWCOUNT, N' 건');
INSERT dbo.SYS_FactoryCalendar (CalendarDate, DayType, HolidayName, CalendarYear, PlantCode, CreatedBy)
SELECT CalendarDate, 'HOLIDAY', HolidayName, YEAR(CalendarDate), PlantCode, @Actor FROM @ToHoliday;
PRINT CONCAT(N'휴일 행 추가: ', @@ROWCOUNT, N' 건');

-- ② 비활성 공휴일 이름의 HOLIDAY → WORKDAY (가장 가까운 근무일 교대 행 복사, 앞쪽 우선)
DECLARE @ToWork TABLE (CalendarDate date, PlantCode varchar(20), TemplateDate date);
INSERT @ToWork (CalendarDate, PlantCode)
SELECT DISTINCT c.CalendarDate, c.PlantCode
FROM   dbo.SYS_FactoryCalendar c
JOIN   dbo.SYS_PublicHoliday h ON h.CountryCode = 'US' AND h.HolidayDate = c.CalendarDate AND h.ActiveFlag = 0
                              AND h.HolidayName = c.HolidayName
WHERE  c.CalendarDate >= @Today AND c.DayType = 'HOLIDAY';
UPDATE t SET TemplateDate = COALESCE(
        (SELECT MAX(w.CalendarDate) FROM dbo.SYS_FactoryCalendar w
         WHERE w.DayType = 'WORKDAY' AND w.ShiftCode IS NOT NULL AND w.CalendarDate < t.CalendarDate
           AND ISNULL(w.PlantCode, '') = ISNULL(t.PlantCode, '')),
        (SELECT MIN(w.CalendarDate) FROM dbo.SYS_FactoryCalendar w
         WHERE w.DayType = 'WORKDAY' AND w.ShiftCode IS NOT NULL AND w.CalendarDate > t.CalendarDate
           AND ISNULL(w.PlantCode, '') = ISNULL(t.PlantCode, '')))
FROM @ToWork t;
DECLARE @NoTemplate nvarchar(max) = (SELECT STRING_AGG(CONVERT(char(10), CalendarDate, 23), ', ') FROM @ToWork WHERE TemplateDate IS NULL);
IF @NoTemplate IS NOT NULL PRINT CONCAT(N'이웃 근무일이 없어 바꾸지 않은 날: ', @NoTemplate, N' — SYS-005 에서 근무일로 수정하세요.');
DELETE c FROM dbo.SYS_FactoryCalendar c
JOIN   @ToWork t ON t.CalendarDate = c.CalendarDate AND ISNULL(t.PlantCode, '') = ISNULL(c.PlantCode, '')
WHERE  t.TemplateDate IS NOT NULL AND c.DayType = 'HOLIDAY';
PRINT CONCAT(N'휴일 → 근무일 전환으로 지운 휴일 행: ', @@ROWCOUNT, N' 건');
INSERT dbo.SYS_FactoryCalendar (CalendarDate, DayType, HolidayName, ShiftCount, ShiftCode, StartTime, EndTime,
                                BreakMinutes, NetWorkHours, CalendarYear, PlantCode, CreatedBy)
SELECT t.CalendarDate, 'WORKDAY', NULL, w.ShiftCount, w.ShiftCode, w.StartTime, w.EndTime,
       w.BreakMinutes, w.NetWorkHours, YEAR(t.CalendarDate), t.PlantCode, @Actor
FROM   @ToWork t
JOIN   dbo.SYS_FactoryCalendar w ON w.CalendarDate = t.TemplateDate AND ISNULL(w.PlantCode, '') = ISNULL(t.PlantCode, '')
                                AND w.DayType = 'WORKDAY' AND w.ShiftCode IS NOT NULL
WHERE  t.TemplateDate IS NOT NULL
ORDER  BY t.CalendarDate, w.ShiftCode;
PRINT CONCAT(N'근무일 교대 행 추가: ', @@ROWCOUNT, N' 건');

COMMIT;
GO

SELECT REPLACE(HolidayName, N' (Observed)', N'') AS Holiday, ActiveFlag, COUNT(*) AS N
FROM   dbo.SYS_PublicHoliday WHERE CountryCode = 'US'
GROUP  BY REPLACE(HolidayName, N' (Observed)', N''), ActiveFlag
ORDER  BY ActiveFlag DESC, Holiday;
SELECT CONVERT(char(10), c.CalendarDate, 23) AS CalendarDate, c.DayType, c.HolidayName, c.ShiftCode, c.CreatedBy
FROM   dbo.SYS_FactoryCalendar c
WHERE  c.CreatedBy IN ('HOLIDAY-RULE-1009', 'COLUMBUS-1009')
ORDER  BY c.CalendarDate, c.ShiftCode;
