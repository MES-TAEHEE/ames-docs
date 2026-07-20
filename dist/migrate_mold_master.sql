-- ════════════════════════════════════════════════════════════════════════
-- migrate_mold_master.sql — 사출 금형 마스터를 SIS(Oracle) 구조 기준으로 정합화 (안 2)
--
--   MD_Mold      = APM2110 + APM2114 흡수(신설 CumulativeShots BIGINT — CurrentShots 와 분리:
--                  CurrentShots 는 "장착 후 타수"로 Inj06 금형교체 시 0 리셋되므로 수명 누적을 담을 수 없다)
--   MD_MoldColor = APM2111 (신설)
--   MD_MoldItem  = APM2120 (신설, AMES 확장 CavitySeq/CavityPos 포함) — 구 MD_MoldItemMap 대체
--   MD_MoldLine  = APM2130 (신설)
--
-- 비파괴: DROP/DELETE 없음. (구 MD_MoldItemMap·CompatItemsJSON 제거는 dist/migrate_mold_cleanup.sql)
-- 재실행 가능(idempotent). 적용 (오류 시 중단을 위해 -b 권장):
--   sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_mold_master.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;   -- 계산 컬럼 인덱스 생성에 필수
SET ANSI_NULLS ON;
GO

-- ── 0. 사전 점검 ────────────────────────────────────────────────────────
-- dash 제거 후 동일해지는 MoldID 쌍 → MoldCodeClean 유니크 인덱스 생성 불가
IF EXISTS (SELECT REPLACE(MoldID,'-','') FROM dbo.MD_Mold
           GROUP BY REPLACE(MoldID,'-','') HAVING COUNT(*) > 1)
    THROW 50001, N'MoldCodeClean 충돌: dash 제거 후 동일해지는 MoldID 쌍이 존재합니다. 먼저 정리하세요.', 1;
GO

-- ── 1. dev 시드 MoldID 재매핑: 합성 대리키(MOLD-*) → SIS 자연키(MOLDNO) ──
-- 영향: MD_Mold 4행 + 참조값 보유 테이블(PR_InjLot/PR_ShotCount/
--       PR_MoldChange/PR_ProductionResult/PP_WorkOrder/MNT_MoldShotCount/MNT_EquipmentStatus).
--       실 FK 제약이 없어 값 UPDATE 만으로 안전. 이미 재매핑된 DB 에서는 0행 갱신(no-op).
DECLARE @map TABLE (OldID VARCHAR(20) PRIMARY KEY, NewID VARCHAR(20) NOT NULL);
INSERT INTO @map VALUES
  ('MOLD-LQ2-DTMD',  'LQ2-DTMD'),
  ('MOLD-LQ2-DTRU',  'LQ2-DTRU'),
  ('MOLD-MEA-DTRCT', 'MEA-DTRCT'),
  ('MOLD-NEA-FUC',   'NEA-FUC');

UPDATE t SET t.MoldID = m.NewID FROM dbo.MD_Mold t          JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.MoldID = m.NewID FROM dbo.PR_InjLot t        JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.MoldID = m.NewID FROM dbo.PR_ShotCount t     JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.MoldID = m.NewID FROM dbo.PR_ProductionResult t JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.MoldID = m.NewID FROM dbo.PP_WorkOrder t     JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.MoldID = m.NewID FROM dbo.MNT_MoldShotCount t JOIN @map m ON m.OldID = t.MoldID;
UPDATE t SET t.OldMoldID = m.NewID FROM dbo.PR_MoldChange t JOIN @map m ON m.OldID = t.OldMoldID;
UPDATE t SET t.NewMoldID = m.NewID FROM dbo.PR_MoldChange t JOIN @map m ON m.OldID = t.NewMoldID;
UPDATE t SET t.MountedMoldID = m.NewID FROM dbo.MNT_EquipmentStatus t JOIN @map m ON m.OldID = t.MountedMoldID;
GO

-- ── 2. MD_Mold 확장 (APM2110 누락 컬럼 + APM2114 흡수 + 실시간 조회용 계산 컬럼) ──
IF COL_LENGTH(N'dbo.MD_Mold', N'CumulativeShots') IS NULL
  ALTER TABLE dbo.MD_Mold ADD [CumulativeShots] BIGINT NOT NULL
      CONSTRAINT DF_MD_Mold_CumulativeShots DEFAULT 0;   -- APM2114.CUM_SHOTS (수명 누적, 리셋 금지)
IF COL_LENGTH(N'dbo.MD_Mold', N'ShotsUpdatedTS') IS NULL
  ALTER TABLE dbo.MD_Mold ADD [ShotsUpdatedTS] DATETIME2 NULL;         -- APM2114.LAST_UPDATED
IF COL_LENGTH(N'dbo.MD_Mold', N'CarType') IS NULL
  ALTER TABLE dbo.MD_Mold ADD [CarType] VARCHAR(20) NULL;              -- APM2110.VINCD (MD_Item.CarType 과 통일)
IF COL_LENGTH(N'dbo.MD_Mold', N'RefCode') IS NULL
  ALTER TABLE dbo.MD_Mold ADD [RefCode] VARCHAR(20) NULL;              -- APM2110.REFCD
IF COL_LENGTH(N'dbo.MD_Mold', N'AssyInjResultFlag') IS NULL
  ALTER TABLE dbo.MD_Mold ADD [AssyInjResultFlag] BIT NOT NULL
      CONSTRAINT DF_MD_Mold_AssyInjResultFlag DEFAULT 0;               -- APM2110.ASSY_INJ_RSLT_YN ('Y'→1)
IF COL_LENGTH(N'dbo.MD_Mold', N'MoldCodeClean') IS NULL
  -- CAST 필수: REPLACE 만 쓰면 varchar(8000) 으로 추론되어 인덱스 키 경고 발생
  ALTER TABLE dbo.MD_Mold ADD [MoldCodeClean] AS CAST(REPLACE(MoldID,'-','') AS VARCHAR(20)) PERSISTED;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MD_Mold_MoldCodeClean'
               AND object_id = OBJECT_ID(N'dbo.MD_Mold'))
  CREATE UNIQUE NONCLUSTERED INDEX UX_MD_Mold_MoldCodeClean ON dbo.MD_Mold([MoldCodeClean]);
GO
-- 수명 누적 시작값: 아직 0 인데 장착 후 타수가 있으면 그 값으로 1회 초기화
UPDATE dbo.MD_Mold SET CumulativeShots = CurrentShots, ShotsUpdatedTS = SYSDATETIME()
WHERE CumulativeShots = 0 AND ISNULL(CurrentShots,0) > 0;
GO

-- ── 3. MD_MoldColor (APM2111) ───────────────────────────────────────────
IF OBJECT_ID(N'dbo.MD_MoldColor', N'U') IS NULL
CREATE TABLE dbo.MD_MoldColor (
  [MoldID]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Mold.MoldID
  [Color]                     VARCHAR(10)          NOT NULL,  -- APM2111.COLOR (수지컬러)
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_MoldColor PRIMARY KEY CLUSTERED ([MoldID], [Color])
);
GO

-- ── 4. MD_MoldItem (APM2120 + AMES 확장) ────────────────────────────────
--   CavitySeq/CavityPos = AMES 확장(SIS 에 없음): 에이전트의 캐비티 슬롯 고정·로봇 주소
--   매핑(1st/2nd)에 필수. 런타임 캐비티 수 = (MoldID, Color) 조회 행 수.
--   CavityCount = SIS 타수 분모(금형 총 캐비티 수 규약 — LH/RH 2캐비티 금형이면 두 행 모두 2).
--   타수의 진실원본은 실시간 카운트(MD_Mold.CurrentShots/CumulativeShots)이며 이 컬럼은
--   SIS 호환·검증용이다.
IF OBJECT_ID(N'dbo.MD_MoldItem', N'U') IS NULL
CREATE TABLE dbo.MD_MoldItem (
  [MoldID]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Mold.MoldID (=MOLDNO)
  [ItemNo]                    VARCHAR(20)          NOT NULL,  -- APM2120.PARTNO, FK -> MD_Item.ItemNo
  [Color]                     VARCHAR(10)              NULL,  -- APM2120.COLOR
  [CavitySeq]                 INT                  NOT NULL DEFAULT 1,  -- AMES: 캐비티 순번(1=1st/LH)
  [CavityPos]                 VARCHAR(4)               NULL,  -- AMES: LH / RH
  [Usage]                     DECIMAL(18,4)            NULL,  -- APM2120.USAGE (UPH·CT 계산용)
  [ResinItemNo]               VARCHAR(20)              NULL,  -- APM2120.RESIN_PARTNO
  [ResinUsage]                DECIMAL(18,4)            NULL,  -- APM2120.RESIN_USAGE (수지 소모량)
  [CavityCount]               INT                  NOT NULL DEFAULT 1,  -- APM2120.CAVITY (타수 분모)
  [MoldCategory]              VARCHAR(20)              NULL,  -- APM2120.MOLD_CATEGORY
  [ActiveFlag]                BIT                  NOT NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_MoldItem PRIMARY KEY CLUSTERED ([MoldID], [ItemNo])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MD_MoldItem_MoldColor'
               AND object_id = OBJECT_ID(N'dbo.MD_MoldItem'))
  CREATE INDEX IX_MD_MoldItem_MoldColor ON dbo.MD_MoldItem([MoldID], [Color])
      INCLUDE([ItemNo], [CavitySeq], [CavityPos]);           -- 실시간 파트 식별 (흐름 B)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MD_MoldItem_ItemNo'
               AND object_id = OBJECT_ID(N'dbo.MD_MoldItem'))
  CREATE INDEX IX_MD_MoldItem_ItemNo ON dbo.MD_MoldItem([ItemNo]);  -- 파트 조인 (흐름 D)
GO

-- ── 5. MD_MoldLine (APM2130) ────────────────────────────────────────────
IF OBJECT_ID(N'dbo.MD_MoldLine', N'U') IS NULL
CREATE TABLE dbo.MD_MoldLine (
  [LineCode]                  VARCHAR(20)          NOT NULL,  -- APM2130.LINECD, FK -> MD_Line.LineID
  [MoldID]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Mold.MoldID
  [UPH]                       DECIMAL(18,4)            NULL,  -- APM2130.UPH
  [PrepTime]                  DECIMAL(18,4)            NULL,  -- APM2130.PREP_TIME (준비교체시간)
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_MoldLine PRIMARY KEY CLUSTERED ([LineCode], [MoldID])
);
GO

-- ── 6. FK 3종 (자식 → MD_Mold) ──────────────────────────────────────────
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

-- ── 7. dev 시드: 시뮬레이터 검증 금형 4종의 금형→품번 매핑 ──────────────
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

-- ── 8. MD_MoldColor 파생 (MD_MoldItem 의 사용 색상) ─────────────────────
INSERT INTO dbo.MD_MoldColor (MoldID, Color, CreatedBy)
SELECT DISTINCT i.MoldID, i.Color, 'MIGRATE'
FROM   dbo.MD_MoldItem i
WHERE  i.Color IS NOT NULL
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_MoldColor c
                   WHERE c.MoldID = i.MoldID AND c.Color = i.Color);
GO

-- ── 9. dev 시드: 라인별 금형 배정 (APM2130) ─────────────────────────────
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

-- ── 10. dev 추정치 시드: MD_MoldItem 의 Usage / Resin (APM2120) ──────────
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

PRINT N'✓ migrate_mold_master.sql applied';
GO
