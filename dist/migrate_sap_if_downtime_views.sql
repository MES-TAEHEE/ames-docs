/* =====================================================================
   SAP 연동 — 비가동 시간 송신용 뷰 V_SAP_Downtime (11-23)
   한 행 = 설비(=라인) 한 대의 정지 구간 하나, 시작·종료 시각 포함. 날짜(자정)를 넘는 구간은 날짜별로 나눠 한 행이 하루 안에 들어간다
   → 설비별·일별 합계는 SUM(DurationMin) GROUP BY EquipCode, WorkDate 로 낸다.
   - StopType='ACTUAL' : 실제 비가동(PP_LineDowntimeLog — 안돈·보전 라인정지·PP-DTL 수동 입력), 종료된 건만
   - StopType='PLAN'   : 정규 일정 정지(PP_LineStateLog 의 PLAN-STOP 연속 분 구간 — 휴게·식사·교대 사이·PM), 구간이 끝난 것만
   읽기 전용 뷰 — 아무것도 쓰지 않는다. 재실행 안전(CREATE OR ALTER).
   주의: PP_LineStateLog 는 보관 기간(기본 7일)이 지나면 지워지므로 정규 일정 쪽은 그 안에 송신해야 한다.
         실제 비가동이 정규 일정 정지 시간과 겹치면 두 행 모두 나온다(같은 시간이 두 구간에 들어감).
   실행: sqlcmd -f 65001 -I -b -i migrate_sap_if_downtime_views.sql
   ===================================================================== */
SET NOCOUNT ON;
GO

-- 먼저 만들었던 뷰(건별·일별 분리안)가 있으면 지운다.
DROP VIEW IF EXISTS dbo.V_SAP_DowntimeEvent;
DROP VIEW IF EXISTS dbo.V_SAP_DowntimeDaily;
GO

CREATE OR ALTER VIEW dbo.V_SAP_Downtime
AS
WITH plan_min AS (
    -- 같은 라인에서 연속된 분은 (분 시각 − 행 번호) 가 같다 → 한 구간
    SELECT s.LineID, s.MinuteTS,
           DATEADD(MINUTE, -CAST(ROW_NUMBER() OVER (PARTITION BY s.LineID ORDER BY s.MinuteTS) AS int), s.MinuteTS) AS Grp
    FROM   dbo.PP_LineStateLog s
    WHERE  s.PlanState = 'PLAN-STOP' AND s.LineID IS NOT NULL
),
plan_span AS (
    SELECT p.LineID, MIN(p.MinuteTS) AS StartTS, DATEADD(MINUTE, 1, MAX(p.MinuteTS)) AS EndTS
    FROM   plan_min p
    GROUP  BY p.LineID, p.Grp
),
src AS (
    SELECT CAST('D' + CAST(d.DowntimeID AS varchar(12)) AS varchar(40)) AS BaseKey,
           d.LineID AS EquipCode, CAST(d.StartTS AS datetime2(0)) AS StartTS, CAST(d.EndTS AS datetime2(0)) AS EndTS,
           CAST('ACTUAL' AS varchar(10)) AS StopType, d.ReasonCode, d.CauseCode, w.WoNumber AS MES_WO_NO, d.Comment
    FROM   dbo.PP_LineDowntimeLog d
    LEFT   JOIN dbo.PP_WorkOrder w ON w.WoID = d.WoID
    WHERE  d.LineID IS NOT NULL AND d.StartTS IS NOT NULL AND d.EndTS IS NOT NULL AND d.EndTS > d.StartTS
    UNION ALL
    SELECT CAST('P' + ps.LineID + '-' + FORMAT(ps.StartTS, 'yyMMddHHmm') AS varchar(40)),
           ps.LineID, CAST(ps.StartTS AS datetime2(0)), CAST(ps.EndTS AS datetime2(0)),
           'PLAN', 'SCHEDULE', NULL, NULL, NULL
    FROM   plan_span ps
    WHERE  EXISTS (SELECT 1 FROM dbo.PP_LineStateLog n          -- 구간이 끝났다(뒤에 정지가 아닌 분이 분류돼 있다)
                    WHERE n.LineID = ps.LineID AND n.MinuteTS >= ps.EndTS AND n.PlanState <> 'PLAN-STOP')
),
split AS (
    -- 날짜별 조각 (한 구간이 자정을 넘으면 날짜마다 한 행)
    SELECT r.*, CAST(r.StartTS AS date) AS D FROM src r
    UNION ALL
    SELECT r.BaseKey, r.EquipCode, r.StartTS, r.EndTS, r.StopType, r.ReasonCode, r.CauseCode, r.MES_WO_NO, r.Comment,
           DATEADD(DAY, 1, x.D)
    FROM   split x JOIN src r ON r.BaseKey = x.BaseKey
    WHERE  DATEADD(DAY, 1, x.D) < r.EndTS
)
SELECT CAST(x.BaseKey + '-' + FORMAT(x.D, 'yyMMdd') AS varchar(50))                         AS EventKey,
       x.EquipCode,
       x.D                                                                                  AS WorkDate,
       CASE WHEN x.StartTS > CAST(x.D AS datetime2(0)) THEN x.StartTS ELSE CAST(x.D AS datetime2(0)) END AS StartTS,
       CASE WHEN x.EndTS < DATEADD(DAY, 1, CAST(x.D AS datetime2(0))) THEN x.EndTS ELSE DATEADD(DAY, 1, CAST(x.D AS datetime2(0))) END AS EndTS,
       CAST(DATEDIFF(MINUTE,
            CASE WHEN x.StartTS > CAST(x.D AS datetime2(0)) THEN x.StartTS ELSE CAST(x.D AS datetime2(0)) END,
            CASE WHEN x.EndTS < DATEADD(DAY, 1, CAST(x.D AS datetime2(0))) THEN x.EndTS ELSE DATEADD(DAY, 1, CAST(x.D AS datetime2(0))) END) AS int) AS DurationMin,
       x.StopType, x.ReasonCode, x.CauseCode, x.MES_WO_NO, x.Comment
FROM   split x;
GO
