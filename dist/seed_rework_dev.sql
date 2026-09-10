-- ════════════════════════════════════════════════════════════════════════
--  seed_rework_dev.sql
--  dev: REWORK 판정용 불량원인 4개 + dev 불량코드의 기본 원인 연결
--
--  REWORK 스테이션은 원인코드(MD_DefectCause)가 하나도 없으면 판정 버튼이 비활성이다.
--  운영은 MD-013 화면에서 등록한다. 재실행 안전.
--  전제: migrate_lot_defect_rework.sql. 불량코드는 tools/seed_inj_demo · seed_img_demo 가 만든다(없어도 원인은 들어간다).
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -i dist/seed_rework_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

MERGE dbo.MD_DefectCause AS t
USING (VALUES
    ('INJ-C01', N'성형 조건',   N'Molding Condition', 'INJ', 'PROCESS',  10),
    ('INJ-C02', N'금형 마모',   N'Mold Wear',         'INJ', 'TOOL',     20),
    ('IMG-C01', N'원단 결함',   N'Fabric Defect',     'IMG', 'MATERIAL', 30),
    ('IMG-C02', N'작업 미숙',   N'Operator Error',    'IMG', 'MAN',      40)
) AS s (CauseCode, CauseName, CauseNameEn, ProcessCode, CauseCategory, SortOrder)
   ON t.CauseCode = s.CauseCode
WHEN NOT MATCHED THEN
    INSERT (CauseCode, CauseName, CauseNameEn, ProcessCode, CauseCategory, RootCauseFlag, SortOrder, ActiveFlag, CreatedBy)
    VALUES (s.CauseCode, s.CauseName, s.CauseNameEn, s.ProcessCode, s.CauseCategory, 1, s.SortOrder, 1, 'seed');
PRINT CONCAT('MD_DefectCause merged: ', @@ROWCOUNT);
GO

-- dev 불량코드에 기본 원인 연결 (비어 있는 것만)
UPDATE dbo.MD_DefectCode SET DefaultCauseCode = 'INJ-C01' WHERE ProcessCode = 'INJ' AND DefaultCauseCode IS NULL;
UPDATE dbo.MD_DefectCode SET DefaultCauseCode = 'IMG-C01' WHERE ProcessCode = 'IMG' AND DefaultCauseCode IS NULL;
GO

SELECT CauseCode, CauseName, ProcessCode, ActiveFlag FROM dbo.MD_DefectCause ORDER BY SortOrder;
SELECT DefectCode, ProcessCode, DefaultCauseCode FROM dbo.MD_DefectCode WHERE ProcessCode IN ('INJ','IMG') ORDER BY DefectCode;
GO
