-- ════════════════════════════════════════════════════════════════════════
-- migrate_inj_agent.sql — 사출 PLC 자동수집 (AMES.InjAgent)
-- PR_InjLot(원천 LOT 확장) / MD_MoldItemMap / MD_InjCondItem /
-- PR_InjCondLog / PR_RobotInspection + 시뮬레이터 금형 시드
-- 적용: sqlcmd -S localhost,1433 -U sa -P AmesDev!2026Sa -d AMES_DEV -f 65001 -i dist/migrate_inj_agent.sql
-- ════════════════════════════════════════════════════════════════════════

-- ── PR_InjLot  (tbl_Lot 1:1 확장 — 사출 원천 LOT 속성)
IF OBJECT_ID(N'dbo.PR_InjLot', N'U') IS NOT NULL DROP TABLE dbo.PR_InjLot;
GO
CREATE TABLE dbo.PR_InjLot (
  [LotID]                     INT                  NOT NULL,  -- PK & FK -> tbl_Lot.LotID (1:1)
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [MoldCode]                  VARCHAR(20)              NULL,  -- PLC 원문 기준 금형코드 (색상 제외)
  [ColorCode]                 VARCHAR(10)              NULL,
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [CavityNo]                  INT                      NULL,  -- 1 / 2
  [CavityPos]                 VARCHAR(4)               NULL,  -- LH / RH
  [PressType]                 VARCHAR(2)               NULL,  -- 1~5 / M(에이전트)
  [MachineShotCount]          BIGINT                   NULL,  -- PLC 샷카운터 값
  [ConfirmStatus]             VARCHAR(16)          NOT NULL DEFAULT 'RAW',  -- RAW/CONFIRMED/NG_BLOCKED/NG_CONFIRMED
  [ConfirmedAt]               DATETIME2                NULL,
  [ConfirmedBy]               NVARCHAR(450)            NULL,
  [ConfirmedSessionID]        INT                      NULL,
  [PrintedCount]              INT                  NOT NULL DEFAULT 0,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_InjLot PRIMARY KEY CLUSTERED ([LotID]),
  CONSTRAINT FK_PR_InjLot_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID])
);
GO
CREATE INDEX IX_PR_InjLot_Status ON dbo.PR_InjLot([ConfirmStatus], [EquipID]);
GO
CREATE INDEX IX_PR_InjLot_Equip ON dbo.PR_InjLot([EquipID]) INCLUDE([CavityPos], [MachineShotCount]);
GO

-- ── MD_MoldItemMap  (금형코드+색상 → 품번·캐비티, SEOYON APM2120 대응)
IF OBJECT_ID(N'dbo.MD_MoldItemMap', N'U') IS NOT NULL DROP TABLE dbo.MD_MoldItemMap;
GO
CREATE TABLE dbo.MD_MoldItemMap (
  [MapID]                     INT IDENTITY         NOT NULL,
  [MoldCode]                  VARCHAR(20)          NOT NULL,
  [ColorCode]                 VARCHAR(10)          NOT NULL,
  [CavityNo]                  INT                  NOT NULL,  -- 1 / 2
  [CavityPos]                 VARCHAR(4)           NOT NULL,  -- LH / RH
  [ItemNo]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Item.ItemNo
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [ActiveFlag]                BIT                  NOT NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_MoldItemMap PRIMARY KEY CLUSTERED ([MapID]),
  CONSTRAINT UQ_MD_MoldItemMap UNIQUE ([MoldCode], [ColorCode], [CavityNo])
);
GO

-- ── MD_InjCondItem  (사출조건 항목 마스터, SEOYON ZINJ0150 대응)
IF OBJECT_ID(N'dbo.MD_InjCondItem', N'U') IS NOT NULL DROP TABLE dbo.MD_InjCondItem;
GO
CREATE TABLE dbo.MD_InjCondItem (
  [CondItemID]                INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Line.LineID
  [ItemCode]                  VARCHAR(20)          NOT NULL,
  [ItemName]                  NVARCHAR(50)             NULL,
  [SetAddress]                INT                      NULL,  -- 세팅값 Modbus 주소
  [ActualAddress]             INT                      NULL,  -- 실제값 Modbus 주소
  [DataType]                  VARCHAR(8)           NOT NULL,  -- LONG / FLOAT
  [Enabled]                   BIT                  NOT NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_InjCondItem PRIMARY KEY CLUSTERED ([CondItemID]),
  CONSTRAINT UQ_MD_InjCondItem UNIQUE ([LineID], [ItemCode])
);
GO

-- ── PR_InjCondLog  (샷별 사출조건 이력, SEOYON ZINJ0160 대응)
IF OBJECT_ID(N'dbo.PR_InjCondLog', N'U') IS NOT NULL DROP TABLE dbo.PR_InjCondLog;
GO
CREATE TABLE dbo.PR_InjCondLog (
  [CondLogID]                 BIGINT IDENTITY      NOT NULL,
  [LineID]                    VARCHAR(20)          NOT NULL,
  [ItemCode]                  VARCHAR(20)          NOT NULL,
  [ShotSeq]                   BIGINT                   NULL,  -- PLC 샷카운터 값
  [SetValue]                  DECIMAL(18,4)            NULL,
  [ActualValue]               DECIMAL(18,4)            NULL,
  [CollectedAt]               DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_InjCondLog PRIMARY KEY CLUSTERED ([CondLogID])
);
GO
CREATE INDEX IX_PR_InjCondLog_Line ON dbo.PR_InjCondLog([LineID], [CollectedAt]);
GO

-- ── PR_RobotInspection  (취출로봇 검사 판정 수신)
IF OBJECT_ID(N'dbo.PR_RobotInspection', N'U') IS NOT NULL DROP TABLE dbo.PR_RobotInspection;
GO
CREATE TABLE dbo.PR_RobotInspection (
  [InspectionID]              BIGINT IDENTITY      NOT NULL,
  [LotID]                     INT                  NOT NULL,  -- FK -> tbl_Lot.LotID
  [EquipID]                   VARCHAR(20)              NULL,
  [CavityPos]                 VARCHAR(4)               NULL,
  [ShortMold]                 VARCHAR(4)               NULL,  -- OK/NG/PASS (미성형)
  [WeldLine]                  VARCHAR(4)               NULL,
  [Gas]                       VARCHAR(4)               NULL,
  [Weight]                    VARCHAR(4)               NULL,
  [OverallNg]                 BIT                  NOT NULL DEFAULT 0,
  [ReceivedAt]                DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_RobotInspection PRIMARY KEY CLUSTERED ([InspectionID]),
  CONSTRAINT FK_PR_RobotInspection_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID])
);
GO
CREATE INDEX IX_PR_RobotInspection_Lot ON dbo.PR_RobotInspection([LotID]);
GO

-- ════════════════════════════════════════════════════════════════════════
-- 시드: 시뮬레이터 검증 금형코드 4종 (MEADTRCTNNB / NEAFUCNNB / LQ2DTMDCBK / LQ2DTRUCBK)
--   색상코드 = 뒤 3자리, 금형코드 = 나머지 (원본 Main.cs 규칙)
--   품번은 AMES_Schema.sql 시드의 실존 MD_Item 사용
-- ════════════════════════════════════════════════════════════════════════
DELETE FROM dbo.MD_Mold WHERE MoldID IN ('MOLD-LQ2-DTMD','MOLD-LQ2-DTRU','MOLD-MEA-DTRCT','MOLD-NEA-FUC');
GO
INSERT INTO dbo.MD_Mold (MoldID, MoldName, RatedShots, CurrentShots, CavityCount, Tonnage, Status, CreatedBy, CreatedTS) VALUES
  ('MOLD-LQ2-DTMD',  N'LQ2 Door Trim MD (2-cav)',  500000, 0, 2, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-LQ2-DTRU',  N'LQ2 Door Trim RU (2-cav)',  500000, 0, 2, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-MEA-DTRCT', N'MEA Door Trim CT (1-cav)',  300000, 0, 1, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-NEA-FUC',   N'NEA FUC (1-cav)',           300000, 0, 1, 650, 'ACTIVE', 'admin', SYSDATETIME());
GO
INSERT INTO dbo.MD_MoldItemMap (MoldCode, ColorCode, CavityNo, CavityPos, ItemNo, MoldID, CreatedBy) VALUES
  ('LQ2DTMD',  'CBK', 1, 'LH', '83335-P8000RBQ',  'MOLD-LQ2-DTMD',  'admin'),
  ('LQ2DTMD',  'CBK', 2, 'RH', '83345-P8000RBQ',  'MOLD-LQ2-DTMD',  'admin'),
  ('LQ2DTRU',  'CBK', 1, 'LH', 'M83371-P8000RBQ', 'MOLD-LQ2-DTRU',  'admin'),
  ('LQ2DTRU',  'CBK', 2, 'RH', 'M83381-P8000RBQ', 'MOLD-LQ2-DTRU',  'admin'),
  ('MEADTRCT', 'NNB', 1, 'LH', '83314-P8000',     'MOLD-MEA-DTRCT', 'admin'),
  ('NEAFUC',   'NNB', 1, 'LH', '83314-P8010',     'MOLD-NEA-FUC',   'admin');
GO
INSERT INTO dbo.MD_InjCondItem (LineID, ItemCode, ItemName, SetAddress, ActualAddress, DataType, CreatedBy) VALUES
  ('LINE-INJ-01', 'TEMP',  N'배럴온도', 5400, 5404, 'FLOAT', 'admin'),
  ('LINE-INJ-01', 'PRESS', N'사출압력', 5410, 5414, 'LONG',  'admin');
GO
-- ── tbl_Lot.LotCode 유니크 (스캔 확정 seek + 채번 중복 방어; tbl_Lot 자체는 이 스크립트가 드롭하지 않으므로 가드)
SET QUOTED_IDENTIFIER ON;  -- 필터드 인덱스 필수 (sqlcmd 기본값 OFF)
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tbl_Lot_LotCode')
  CREATE UNIQUE NONCLUSTERED INDEX UX_tbl_Lot_LotCode ON dbo.tbl_Lot([LotCode]) WHERE [LotCode] IS NOT NULL;
GO
PRINT '✓ migrate_inj_agent.sql applied';
GO
