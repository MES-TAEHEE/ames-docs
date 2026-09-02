-- ════════════════════════════════════════════════════════════════════════
-- migrate_mold_master.sql — 금형 마스터 시드 + FK 전용 (구조 DDL 은 AMES_Schema.sql 로 이관)
--
--   MD_MoldColor / MD_MoldItem / MD_MoldLine 테이블과 MD_Mold 확장 컬럼
--   (CumulativeShots·ShotsUpdatedTS·CarType·RefCode·AssyInjResultFlag·계산컬럼
--    MoldCodeClean + UX_MD_Mold_MoldCodeClean)의 CREATE/ALTER 는 2026-07-20 부터
--   dist/AMES_Schema.sql 에 포함된다. 이 스크립트는 이제 다음만 수행한다:
--     1) MD_Mold.CumulativeShots 초기화 (장착 후 타수 → 수명 누적 시작값)
--     2) 자식 3종 → MD_Mold FK 3종 (스키마는 FK 를 주석 처리하므로 여기서 실제 생성)
--     3) dev 시드 — 금형→품번 매핑(MD_MoldItem)·색상(MD_MoldColor)·라인배정(MD_MoldLine)·수지정보
--
-- 선행: AMES_Schema.sql(구조) → migrate_inj_agent.sql(MD_Mold 4종 시드).
-- 비파괴·재실행 가능(idempotent). 적용 (오류 시 중단을 위해 -b 권장):
--   sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_mold_master.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1. MD_Mold 수명 누적 시작값 ─────────────────────────────────────────
-- 아직 0 인데 장착 후 타수(CurrentShots)가 있으면 그 값으로 1회 초기화.
-- (CurrentShots = Inj06 교체 시 0 리셋 / CumulativeShots = 수명 누적, 리셋 금지)
UPDATE dbo.MD_Mold SET CumulativeShots = CurrentShots, ShotsUpdatedTS = SYSDATETIME()
WHERE CumulativeShots = 0 AND ISNULL(CurrentShots,0) > 0;
GO

-- ── 2. FK 3종 (자식 → MD_Mold) ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldColor_Mold')
  ALTER TABLE dbo.MD_MoldColor ADD CONSTRAINT FK_MD_MoldColor_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldItem_Mold')
  ALTER TABLE dbo.MD_MoldItem ADD CONSTRAINT FK_MD_MoldItem_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldLine_Mold')
  ALTER TABLE dbo.MD_MoldLine ADD CONSTRAINT FK_MD_MoldLine_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
GO

-- ── 3. dev 시드: 시뮬레이터 검증 금형 4종의 금형→품번 매핑 ──────────────
--   (금형 자체는 migrate_inj_agent.sql 이 시드. CavityCount = 금형 총 캐비티 규약.)
INSERT INTO dbo.MD_MoldItem
    (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
SELECT v.MoldID, v.ItemNo, v.Color, v.CavitySeq, v.CavityPos,
       ISNULL(m.CavityCount, 1), 'INJECTION', 1, 'admin'
FROM  (VALUES
        ('LQ2-DTMD',  'CBK', 1, 'LH', '83335-P8000RBQ'),
        ('LQ2-DTMD',  'CBK', 2, 'RH', '83345-P8000RBQ'),
        ('LQ2-DTRU',  'CBK', 1, 'LH', 'M83371-P8000RBQ'),
        ('LQ2-DTRU',  'CBK', 2, 'RH', 'M83381-P8000RBQ'),
        ('MEA-DTRCT', 'NNB', 1, 'LH', '83314-P8000'),
        ('NEA-FUC',   'NNB', 1, 'LH', '83314-P8010')
      ) v (MoldID, Color, CavitySeq, CavityPos, ItemNo)
JOIN   dbo.MD_Mold m ON m.MoldID = v.MoldID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_MoldItem i
                   WHERE i.MoldID = v.MoldID AND i.ItemNo = v.ItemNo);
GO

-- ── 4. MD_MoldColor 파생 (MD_MoldItem 의 사용 색상) ─────────────────────
INSERT INTO dbo.MD_MoldColor (MoldID, Color, CreatedBy)
SELECT DISTINCT i.MoldID, i.Color, 'MIGRATE'
FROM   dbo.MD_MoldItem i
WHERE  i.Color IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_MoldColor c
                   WHERE c.MoldID = i.MoldID AND c.Color = i.Color);
GO

-- ── 5. dev 시드: 라인별 금형 배정 (APM2130) ─────────────────────────────
--   금형 4종(전부 650T급)은 사출 2개 라인 모두에서 생산 가능.
--   UPH = 시간당 생산수(사이클 ~60초 기준: 2캐비티=120, 1캐비티=60),
--   PrepTime(분) 은 850T 대형기가 준비교체가 더 긺.
INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy)
SELECT v.LineCode, v.MoldID, v.UPH, v.PrepTime, 'admin'
FROM  (VALUES
        ('LINE-INJ-01', 'LQ2-DTMD',  120, 40),
        ('LINE-INJ-01', 'LQ2-DTRU',  120, 40),
        ('LINE-INJ-01', 'MEA-DTRCT',  60, 30),
        ('LINE-INJ-01', 'NEA-FUC',    60, 30),
        ('LINE-INJ-02', 'LQ2-DTMD',  120, 45),
        ('LINE-INJ-02', 'LQ2-DTRU',  120, 45),
        ('LINE-INJ-02', 'MEA-DTRCT',  60, 35),
        ('LINE-INJ-02', 'NEA-FUC',    60, 35)
      ) v (LineCode, MoldID, UPH, PrepTime)
JOIN   dbo.MD_Line l ON l.LineID = v.LineCode
JOIN   dbo.MD_Mold m ON m.MoldID = v.MoldID
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_MoldLine ml
                   WHERE ml.LineCode = v.LineCode AND ml.MoldID = v.MoldID);
GO

-- ── 6. dev 추정치 시드: MD_MoldItem 의 Usage / Resin (APM2120) ──────────
--   실측 SIS 데이터 이관 전까지의 dev 추정치.
--   Usage = 차량당 사용수량(도어트림류 = 1), ResinUsage = 부품 1개당 수지 소모량(g).
--   수지 품번은 색상별 PP+TD20 펠릿 2종을 MD_Item(MATERIAL) 에 시드해서 참조.
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, DefaultUOM, ActiveFlag, CreatedBy)
SELECT v.ItemNo, v.ItemName, 'MATERIAL', 'RESIN', 'KG', 1, 'admin'
FROM  (VALUES
        ('RESIN-PP-TD20BK', N'PP+TD20 RESIN PELLET, BLACK'),
        ('RESIN-PP-TD20NB', N'PP+TD20 RESIN PELLET, NAT BEIGE')
      ) v (ItemNo, ItemName)
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Item i WHERE i.ItemNo = v.ItemNo);
GO
-- NULL 인 행만 채운다 — 화면에서 수동 입력한 값은 덮어쓰지 않는다.
UPDATE mi SET mi.[Usage]     = COALESCE(mi.[Usage],     v.Usage),
              mi.ResinItemNo = COALESCE(mi.ResinItemNo, v.ResinItemNo),
              mi.ResinUsage  = COALESCE(mi.ResinUsage,  v.ResinUsage),
              mi.ModifiedTS  = SYSDATETIME(),
              mi.ModifiedBy  = 'seed'
FROM   dbo.MD_MoldItem mi
JOIN  (VALUES
        ('LQ2-DTMD',  '83335-P8000RBQ',  1, 'RESIN-PP-TD20BK', 650),
        ('LQ2-DTMD',  '83345-P8000RBQ',  1, 'RESIN-PP-TD20BK', 650),
        ('LQ2-DTRU',  'M83371-P8000RBQ', 1, 'RESIN-PP-TD20BK', 520),
        ('LQ2-DTRU',  'M83381-P8000RBQ', 1, 'RESIN-PP-TD20BK', 520),
        ('MEA-DTRCT', '83314-P8000',     1, 'RESIN-PP-TD20NB', 480),
        ('NEA-FUC',   '83314-P8010',     1, 'RESIN-PP-TD20NB', 350)
      ) v (MoldID, ItemNo, Usage, ResinItemNo, ResinUsage)
  ON   v.MoldID = mi.MoldID AND v.ItemNo = mi.ItemNo
WHERE  mi.[Usage] IS NULL OR mi.ResinItemNo IS NULL OR mi.ResinUsage IS NULL;
GO

PRINT N'✓ migrate_mold_master.sql (seed+FK) applied';
GO
