/* ------------------------------------------------------------------
   migrate_spare_part_zone_slot.sql  — migrate_spare_part_image.sql 다음에 적용
   ① 공통코드 MNT_ZONE(보전 위치) · MNT_SLOT(보전 랙 층) 그룹·항목 — 없을 때만 생성(개발 DB 의 pda-seed 값과 동일).
   ② MD_SparePart 에서만 관리하는 보관 구역·칸 컬럼을 SparePartImage 다음 위치에 추가:
      ZoneCode VARCHAR(20) NULL (공통코드 MNT_ZONE) · Slot VARCHAR(5) NULL (공통코드 MNT_SLOT).
      컬럼 순서를 지키기 위해 테이블을 재생성한다(행·PK·고유 인덱스·기본값 보존, MD_SparePart 를 참조하는 FK 없음).
   ③ 자유 입력 보관위치 StorageLoc 은 구역·칸으로 대체되므로 삭제(가드형 DROP COLUMN).
   가드형·재실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- ── ① 공통코드 ─────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'MNT_ZONE')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('MNT_ZONE', N'보전 위치', N'Maintenance Zone', NULL, 1, 'migrate_spare_part_zone_slot');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'MNT_SLOT')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('MNT_SLOT', N'보전 랙 층', N'Maintenance Rack Level', NULL, 1, 'migrate_spare_part_zone_slot');

INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy)
SELECT v.CodeID, v.GroupCode, v.CodeValue, v.CodeName, v.CodeNameEn, v.SortOrder, 1, 'migrate_spare_part_zone_slot'
FROM (VALUES
    ('MNT_SLOT_01', 'MNT_SLOT', '01', N'1층', N'Level 1', 10),
    ('MNT_SLOT_02', 'MNT_SLOT', '02', N'2층', N'Level 2', 20),
    ('MNT_SLOT_03', 'MNT_SLOT', '03', N'3층', N'Level 3', 30),
    ('MNT_SLOT_04', 'MNT_SLOT', '04', N'4층', N'Level 4', 40),
    ('MNT_SLOT_05', 'MNT_SLOT', '05', N'5층', N'Level 5', 50),
    ('MNT_ZONE_SP_CAB1',      'MNT_ZONE', 'SP_CAB1',      N'CAB1',      N'CAB1',      10),
    ('MNT_ZONE_SP_CAB2',      'MNT_ZONE', 'SP_CAB2',      N'CAB2',      N'CAB2',      20),
    ('MNT_ZONE_SP_A1',        'MNT_ZONE', 'SP_A1',        N'A1',        N'A1',        30),
    ('MNT_ZONE_SP_A2',        'MNT_ZONE', 'SP_A2',        N'A2',        N'A2',        40),
    ('MNT_ZONE_SP_A3',        'MNT_ZONE', 'SP_A3',        N'A3',        N'A3',        50),
    ('MNT_ZONE_SP_B1',        'MNT_ZONE', 'SP_B1',        N'B1',        N'B1',        60),
    ('MNT_ZONE_SP_B2',        'MNT_ZONE', 'SP_B2',        N'B2',        N'B2',        70),
    ('MNT_ZONE_SP_B3',        'MNT_ZONE', 'SP_B3',        N'B3',        N'B3',        80),
    ('MNT_ZONE_SP_C1',        'MNT_ZONE', 'SP_C1',        N'C1',        N'C1',        90),
    ('MNT_ZONE_SP_C2',        'MNT_ZONE', 'SP_C2',        N'C2',        N'C2',        100),
    ('MNT_ZONE_SP_C3',        'MNT_ZONE', 'SP_C3',        N'C3',        N'C3',        110),
    ('MNT_ZONE_SP_D1',        'MNT_ZONE', 'SP_D1',        N'D1',        N'D1',        120),
    ('MNT_ZONE_SP_D2',        'MNT_ZONE', 'SP_D2',        N'D2',        N'D2',        130),
    ('MNT_ZONE_SP_D3',        'MNT_ZONE', 'SP_D3',        N'D3',        N'D3',        140),
    ('MNT_ZONE_SP_E1',        'MNT_ZONE', 'SP_E1',        N'E1',        N'E1',        150),
    ('MNT_ZONE_SP_E2',        'MNT_ZONE', 'SP_E2',        N'E2',        N'E2',        160),
    ('MNT_ZONE_SP_E3',        'MNT_ZONE', 'SP_E3',        N'E3',        N'E3',        170),
    ('MNT_ZONE_SP_F1',        'MNT_ZONE', 'SP_F1',        N'F1',        N'F1',        180),
    ('MNT_ZONE_SP_F2',        'MNT_ZONE', 'SP_F2',        N'F2',        N'F2',        190),
    ('MNT_ZONE_SP_F3',        'MNT_ZONE', 'SP_F3',        N'F3',        N'F3',        200),
    ('MNT_ZONE_SP_R_RACKS_1', 'MNT_ZONE', 'SP_R_RACKS_1', N'R/Racks-1', N'R/Racks-1', 210),
    ('MNT_ZONE_SP_R_RACKS_2', 'MNT_ZONE', 'SP_R_RACKS_2', N'R/Racks-2', N'R/Racks-2', 220),
    ('MNT_ZONE_SP_C_RACK_1',  'MNT_ZONE', 'SP_C_RACK_1',  N'C/Rack-1',  N'C/Rack-1',  230),
    ('MNT_ZONE_SP_FL1',       'MNT_ZONE', 'SP_FL1',       N'FL1',       N'FL1',       240),
    ('MNT_ZONE_SP_FL2',       'MNT_ZONE', 'SP_FL2',       N'FL2',       N'FL2',       250),
    ('MNT_ZONE_SP_FL3',       'MNT_ZONE', 'SP_FL3',       N'FL3',       N'FL3',       260),
    ('MNT_ZONE_SP_FL4',       'MNT_ZONE', 'SP_FL4',       N'FL4',       N'FL4',       270),
    ('MNT_ZONE_SP_EXTRA',     'MNT_ZONE', 'SP_EXTRA',     N'Extra',     N'Extra',     280)
) AS v (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem i WHERE i.CodeID = v.CodeID);
PRINT CONCAT('MNT_ZONE/MNT_SLOT 공통코드 항목 추가: ', @@ROWCOUNT);

-- ── ② MD_SparePart 재생성 ───────────────────────────────────────────
IF COL_LENGTH('dbo.MD_SparePart', 'ZoneCode') IS NULL
BEGIN
    IF COL_LENGTH('dbo.MD_SparePart', 'SparePartImage') IS NULL
        THROW 50001, 'migrate_spare_part_image.sql 을 먼저 적용하세요 (MD_SparePart.SparePartImage 없음).', 1;

    CREATE TABLE dbo.MD_SparePart_new (
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- EOS-SP-{분류}{적용설비}-{yy}{순번4}, 저장 시 자동 채번
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [PartNo]                    VARCHAR(20)          NOT NULL,  -- 제조사/구매 부품번호 (고유)
      [PartName]                  NVARCHAR(60)             NULL,
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [SparePartImage]            VARBINARY(MAX)           NULL,  -- 부품 이미지(320×180 이내 JPEG/PNG 바이트)
      [ZoneCode]                  VARCHAR(20)              NULL,  -- Spare Part Master 보관 구역, 공통코드 MNT_ZONE
      [Slot]                      VARCHAR(5)               NULL,  -- Spare Part Master 보관 칸(층), 공통코드 MNT_SLOT
      [UOM]                       VARCHAR(10)              NULL,
      [OnHandQty]                 INT                  NOT NULL CONSTRAINT DF_MD_SparePart_OnHandQty_v5 DEFAULT 0,  -- 현재고 (입출고로만 변경)
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [SupplierID]                VARCHAR(20)              NULL,
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag_v5 DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS_v5 DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([SparePartNo]),
      CONSTRAINT UX_MD_SparePart_PartNo_new UNIQUE ([PartNo])
    );

    INSERT INTO dbo.MD_SparePart_new
        (SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, UOM, OnHandQty, SafetyStock, ReorderPoint, ReorderQty,
         LeadTimeDays, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, UOM, OnHandQty, SafetyStock, ReorderPoint, ReorderQty,
           LeadTimeDays, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MD_SparePart;
    PRINT CONCAT('MD_SparePart 이관 행수: ', @@ROWCOUNT);

    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    EXEC sp_rename 'dbo.UX_MD_SparePart_PartNo_new', 'UX_MD_SparePart_PartNo', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_OnHandQty_v5', 'DF_MD_SparePart_OnHandQty', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_ActiveFlag_v5', 'DF_MD_SparePart_ActiveFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_CreatedTS_v5', 'DF_MD_SparePart_CreatedTS', 'OBJECT';
    PRINT 'MD_SparePart: ZoneCode · Slot(SparePartImage 다음) 추가 — 재생성 완료';
END
ELSE
    PRINT 'MD_SparePart: 이미 ZoneCode 있음';

-- ── ③ StorageLoc 삭제 — 구역(ZoneCode)·칸(Slot) 이 대체 ───────────────────
IF COL_LENGTH('dbo.MD_SparePart', 'StorageLoc') IS NOT NULL
BEGIN
    ALTER TABLE dbo.MD_SparePart DROP COLUMN StorageLoc;
    PRINT 'MD_SparePart.StorageLoc 삭제';
END

COMMIT;
GO

-- 확인(재생성 뒤 별도 배치)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' ORDER BY ORDINAL_POSITION;
SELECT GroupCode, COUNT(*) AS Items FROM dbo.MD_CodeItem WHERE GroupCode IN ('MNT_ZONE', 'MNT_SLOT') GROUP BY GroupCode;
