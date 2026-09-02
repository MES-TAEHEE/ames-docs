-- ════════════════════════════════════════════════════════════════════════
--  seed_md_bop_inj_dev.sql
--  dev: INJ 스테이션 BOP 시드
--
--  INJ-MAIN 좌측 품번 패널은 MD_Bop.StationCode 로 "이 스테이션에서 만드는 품번"을
--  고른다. dev DB 의 MD_Bop 은 사실상 비어 있어(2행) 화면이 빈다. 금형 마스터
--  (MD_MoldLine → MD_MoldItem) 에 이미 라인별 품번이 있으므로 그걸 BOP 로 옮긴다.
--
--  · 대상 라인의 MD_Station 마다 (품번, 스테이션) 한 행. RoutingType 'A', StepSeq 10.
--  · 이미 (품번, 스테이션) 이 있으면 건너뛴다 — 재실행 안전.
--  · MD_Item 에 없는 품번은 넣지 않는다.
--  · 운영 DB 용이 아니다. 운영 BOP 는 MD-005 화면에서 등록한다.
--
--  적용:  sqlcmd -S 192.168.2.137,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_md_bop_inj_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

;WITH src AS (
    SELECT DISTINCT mi.ItemNo, s.StationCode
    FROM   dbo.MD_MoldLine ml
    JOIN   dbo.MD_MoldItem mi ON mi.MoldID = ml.MoldID AND mi.ActiveFlag = 1
    JOIN   dbo.MD_Station  s  ON s.LineID  = ml.LineCode
    JOIN   dbo.MD_Item     i  ON i.ItemNo  = mi.ItemNo
    WHERE  ml.LineCode = 'LINE-INJ-01'
)
INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
SELECT 'P' + LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 23),
       src.ItemNo, 'A', 10, src.StationCode, 1, 'seed'
FROM   src
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Bop b
                   WHERE  b.ItemNo = src.ItemNo AND b.StationCode = src.StationCode);

PRINT CONCAT('MD_Bop rows inserted: ', @@ROWCOUNT);
GO

SELECT b.StationCode, b.ItemNo, b.RoutingType, b.StepSeq, b.CreatedBy
FROM   dbo.MD_Bop b
WHERE  b.StationCode LIKE 'ST-INJ-%'
ORDER  BY b.StationCode, b.ItemNo;
GO
