-- ════════════════════════════════════════════════════════════════════════
--  seed_defect_robot_ng.sql
--  사출 '로봇 NG' 불량코드 추가 (INJ-D08) — 10-28 김현진(미해결 1)
--
--  스캔 확정 뒤에 늦게 온 취출 로봇 NG 를 InjAgent 가 이 코드로 불량 등록한다(실적 역분개 + 재작업 대기열).
--  InjAgent 설정 Agent:LateNgDefectCode = INJ-D08 이 이 코드를 가리킨다 — 코드가 없거나 설정을 비우면 경고 로그만 남긴다.
--  POP INJ 불량 팝업에서도 고를 수 있다(ProcessCode INJ). 일반 불량과 같이 DEFECT → REWORK 대기열로 간다.
--  없는 행만 넣으므로 재실행 안전하며, MD-013 에서 고친 값은 덮지 않는다.
--  기본 원인은 MD_DefectCause 에 그 원인이 있을 때만 연결한다(FK 가 없어 고아 방지).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -i dist/seed_defect_robot_ng.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

MERGE dbo.MD_DefectCode AS t
USING (
    SELECT v.DefectCode, v.DefectName, v.DefectNameEn, v.ProcessCode, v.DefectCategory, v.SeverityLevel,
           c.CauseCode AS DefaultCauseCode
    FROM (VALUES
        ('INJ-D08', N'로봇 NG', N'Robot NG', 'INJ', 'MOLDING', 'MEDIUM', 'INJ-C01')
    ) AS v (DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel, CauseCode)
    LEFT JOIN dbo.MD_DefectCause c ON c.CauseCode = v.CauseCode
) AS s
   ON t.DefectCode = s.DefectCode
WHEN NOT MATCHED THEN
    INSERT (DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel,
            DispositionDefault, DefaultCauseCode, ParetoFlag, CreatedBy, ActiveFlag)
    VALUES (s.DefectCode, s.DefectName, s.DefectNameEn, s.ProcessCode, s.DefectCategory, s.SeverityLevel,
            'REWORK', s.DefaultCauseCode, 0, 'seed', 1);
PRINT CONCAT('MD_DefectCode robot NG inserted: ', @@ROWCOUNT);
GO

SELECT DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel, DefaultCauseCode, ActiveFlag
FROM   dbo.MD_DefectCode
WHERE  DefectCode = 'INJ-D08';
GO
