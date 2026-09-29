-- ════════════════════════════════════════════════════════════════════════
--  seed_defect_first_article.sql
--  INJ·IMG 불량 팝업에 '초품' 불량코드 추가 (INJ-D07 · IMG-D07)
--
--  일반 불량과 같이 처리된다 — 등록된 LOT 은 DEFECT 가 되어 REWORK 대기열로 간다.
--  없는 행만 넣으므로 재실행 안전하며, MD-013 에서 고친 값은 덮지 않는다.
--  기본 원인은 MD_DefectCause 에 그 원인이 있을 때만 연결한다(FK 가 없어 고아 방지).
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -i dist/seed_defect_first_article.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

MERGE dbo.MD_DefectCode AS t
USING (
    SELECT v.DefectCode, v.DefectName, v.DefectNameEn, v.ProcessCode, v.DefectCategory, v.SeverityLevel,
           c.CauseCode AS DefaultCauseCode
    FROM (VALUES
        ('INJ-D07', N'초품', N'Sample Defect', 'INJ', 'MOLDING',  'MEDIUM', 'INJ-C01'),
        ('IMG-D07', N'초품', N'Sample Defect', 'IMG', 'WRAPPING', 'MEDIUM', 'IMG-C01')
    ) AS v (DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel, CauseCode)
    LEFT JOIN dbo.MD_DefectCause c ON c.CauseCode = v.CauseCode
) AS s
   ON t.DefectCode = s.DefectCode
WHEN NOT MATCHED THEN
    INSERT (DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel,
            DispositionDefault, DefaultCauseCode, ParetoFlag, CreatedBy, ActiveFlag)
    VALUES (s.DefectCode, s.DefectName, s.DefectNameEn, s.ProcessCode, s.DefectCategory, s.SeverityLevel,
            'REWORK', s.DefaultCauseCode, 0, 'seed', 1);
PRINT CONCAT('MD_DefectCode first-article inserted: ', @@ROWCOUNT);
GO

SELECT DefectCode, DefectName, DefectNameEn, ProcessCode, DefectCategory, SeverityLevel, DefaultCauseCode, ActiveFlag
FROM   dbo.MD_DefectCode
WHERE  DefectCode IN ('INJ-D07', 'IMG-D07')
ORDER  BY DefectCode;
GO
