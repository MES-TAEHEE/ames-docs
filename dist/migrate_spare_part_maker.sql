/* ------------------------------------------------------------------
   migrate_spare_part_maker.sql  — migrate_spare_part_zone_slot.sql 다음에 적용
   MD_SparePart 에 제조사 컬럼 Maker NVARCHAR(100) NULL 을 SupplierID 앞(LeadTimeDays 다음)에 추가.
   컬럼 순서를 지키기 위해 테이블을 재생성한다(행·PK·고유 인덱스·기본값 보존, MD_SparePart 를 참조하는 FK 없음).
   가드형: Maker 컬럼이 없을 때만 실행. 재실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF COL_LENGTH('dbo.MD_SparePart', 'Maker') IS NULL
BEGIN
    IF COL_LENGTH('dbo.MD_SparePart', 'ZoneCode') IS NULL OR COL_LENGTH('dbo.MD_SparePart', 'StorageLoc') IS NOT NULL
        THROW 50001, 'migrate_spare_part_zone_slot.sql 을 먼저 적용하세요 (ZoneCode 없음 또는 StorageLoc 잔존).', 1;

    CREATE TABLE dbo.MD_SparePart_new (
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- EOS-SP-{분류}{적용설비}-{yy}{순번4}, 저장 시 자동 채번
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [PartNo]                    VARCHAR(20)          NOT NULL,  -- 제조사/구매 부품번호 (고유)
      [PartName]                  NVARCHAR(60)             NULL,
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [SparePartImage]            VARBINARY(MAX)           NULL,  -- 부품 이미지(320×180 이내 JPEG/PNG 바이트)
      [ZoneCode]                  VARCHAR(20)              NULL,  -- 보관 구역, 공통코드 MNT_ZONE
      [Slot]                      VARCHAR(5)               NULL,  -- 보관 칸(층), 공통코드 MNT_SLOT
      [UOM]                       VARCHAR(10)              NULL,
      [OnHandQty]                 INT                  NOT NULL CONSTRAINT DF_MD_SparePart_OnHandQty_v6 DEFAULT 0,  -- 현재고 (입출고로만 변경)
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [Maker]                     NVARCHAR(100)            NULL,  -- 제조사(자유 입력)
      [SupplierID]                VARCHAR(20)              NULL,  -- 논리 참조 -> MD_Vendor.VendorID
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag_v6 DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS_v6 DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([SparePartNo]),
      CONSTRAINT UX_MD_SparePart_PartNo_new UNIQUE ([PartNo])
    );

    INSERT INTO dbo.MD_SparePart_new
        (SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, ZoneCode, Slot, UOM, OnHandQty,
         SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, ZoneCode, Slot, UOM, OnHandQty,
           SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MD_SparePart;
    PRINT CONCAT('MD_SparePart 이관 행수: ', @@ROWCOUNT);

    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    EXEC sp_rename 'dbo.UX_MD_SparePart_PartNo_new', 'UX_MD_SparePart_PartNo', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_OnHandQty_v6', 'DF_MD_SparePart_OnHandQty', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_ActiveFlag_v6', 'DF_MD_SparePart_ActiveFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_CreatedTS_v6', 'DF_MD_SparePart_CreatedTS', 'OBJECT';
    PRINT 'MD_SparePart: Maker(SupplierID 앞) 추가 — 재생성 완료';
END
ELSE
    PRINT 'MD_SparePart: 이미 Maker 있음';

COMMIT;
GO

-- 확인(재생성 뒤 별도 배치)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' ORDER BY ORDINAL_POSITION;
