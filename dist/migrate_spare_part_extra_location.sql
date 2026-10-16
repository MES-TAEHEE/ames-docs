/* ------------------------------------------------------------------
   migrate_spare_part_extra_location.sql  — migrate_spare_part_maker.sql 다음에 적용
   ① 공통코드 MNT_SLOT 에 EX(Extra) 추가 — 없을 때만.
   ② MD_SparePart 에 ExtraLocation NVARCHAR(60) NULL 을 Slot 다음에 추가.
      컬럼 순서를 지키기 위해 테이블을 재생성한다(행·PK·고유 인덱스·기본값 보존, MD_SparePart 를 참조하는 FK 없음).
      가드형: ExtraLocation 이 없거나 Slot 바로 뒤가 아닐 때만 실행. 재실행 안전.
   ③ 구역 SP_EXTRA 는 칸이 EX 로 고정된다 — 기존 SP_EXTRA 예비품의 Slot, 그리고 그 구역의 MD_Location 행 Slot 을 EX 로 맞춘다.
      PDA API 가 예비품 위치를 MD_Location 과 (ZoneCode, Slot) 으로 맞추므로 둘을 같이 바꿔야 PDA 에서 위치가 계속 잡힌다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- ① 공통코드
IF EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'MNT_SLOT')
   AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'MNT_SLOT' AND CodeValue = 'EX')
BEGIN
    INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy)
    VALUES ('MNT_SLOT_EX', 'MNT_SLOT', 'EX', N'Extra', N'Extra', 60, 1, 'migrate_spare_part_extra_location');
    PRINT 'MNT_SLOT: EX(Extra) 추가';
END
ELSE
    PRINT 'MNT_SLOT: EX 이미 있음(또는 그룹 없음)';

-- ② 컬럼
DECLARE @slotPos int = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_SparePart') AND name = 'Slot');
DECLARE @exPos   int = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_SparePart') AND name = 'ExtraLocation');

IF @exPos IS NULL OR @exPos <> @slotPos + 1
BEGIN
    IF COL_LENGTH('dbo.MD_SparePart', 'Maker') IS NULL OR @slotPos IS NULL
        THROW 50001, 'migrate_spare_part_maker.sql 까지 먼저 적용하세요 (Maker 또는 Slot 없음).', 1;

    CREATE TABLE dbo.MD_SparePart_new (
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- EOS-SP-{분류}{적용설비}-{yy}{순번4}, 저장 시 자동 채번
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [PartNo]                    VARCHAR(20)          NOT NULL,  -- 제조사/구매 부품번호 (고유)
      [PartName]                  NVARCHAR(60)             NULL,
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [SparePartImage]            VARBINARY(MAX)           NULL,  -- 부품 이미지(320×180 이내 JPEG/PNG 바이트)
      [ZoneCode]                  VARCHAR(20)              NULL,  -- 보관 구역, 공통코드 MNT_ZONE
      [Slot]                      VARCHAR(5)               NULL,  -- 보관 칸(층), 공통코드 MNT_SLOT (구역 SP_EXTRA 는 EX 고정)
      [ExtraLocation]             NVARCHAR(60)             NULL,  -- 구역 SP_EXTRA 일 때만 쓰는 자유 입력 위치
      [UOM]                       VARCHAR(10)              NULL,
      [OnHandQty]                 INT                  NOT NULL CONSTRAINT DF_MD_SparePart_OnHandQty_v7 DEFAULT 0,  -- 현재고 (입출고로만 변경)
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [Maker]                     NVARCHAR(100)            NULL,  -- 제조사(자유 입력)
      [SupplierID]                VARCHAR(20)              NULL,  -- 논리 참조 -> MD_Vendor.VendorID
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag_v7 DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS_v7 DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([SparePartNo]),
      CONSTRAINT UX_MD_SparePart_PartNo_new UNIQUE ([PartNo])
    );

    -- ExtraLocation 이 이미 있으면(순서만 다른 경우) 값을 보존한다. 없는 컬럼을 정적 SQL 로 참조하면 배치가 컴파일에서 죽으므로 동적으로 만든다.
    DECLARE @exExpr nvarchar(40) = CASE WHEN @exPos IS NULL THEN N'CAST(NULL AS NVARCHAR(60))' ELSE N'ExtraLocation' END;
    DECLARE @sql nvarchar(max) = N'
        INSERT INTO dbo.MD_SparePart_new
            (SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, ZoneCode, Slot, ExtraLocation, UOM, OnHandQty,
             SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays, Maker, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, SparePartImage, ZoneCode, Slot, ' + @exExpr + N', UOM, OnHandQty,
               SafetyStock, ReorderPoint, ReorderQty, LeadTimeDays, Maker, SupplierID, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM   dbo.MD_SparePart;
        PRINT CONCAT(N''MD_SparePart 이관 행수: '', @@ROWCOUNT);';
    EXEC sp_executesql @sql;

    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    EXEC sp_rename 'dbo.UX_MD_SparePart_PartNo_new', 'UX_MD_SparePart_PartNo', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_OnHandQty_v7', 'DF_MD_SparePart_OnHandQty', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_ActiveFlag_v7', 'DF_MD_SparePart_ActiveFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_CreatedTS_v7', 'DF_MD_SparePart_CreatedTS', 'OBJECT';
    PRINT 'MD_SparePart: ExtraLocation(Slot 다음) 추가 — 재생성 완료';
END
ELSE
    PRINT 'MD_SparePart: 이미 ExtraLocation 이 Slot 다음에 있음';

COMMIT;
GO

-- ③ SP_EXTRA 구역의 칸을 EX 로 (재생성 뒤 별도 배치)
SET NOCOUNT ON;
UPDATE dbo.MD_SparePart SET Slot = 'EX'
WHERE  ZoneCode = 'SP_EXTRA' AND ISNULL(Slot, '') <> 'EX';
PRINT CONCAT('MD_SparePart: SP_EXTRA 칸 EX 보정 ', @@ROWCOUNT, '행');

GO

-- 확인
SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' AND ORDINAL_POSITION BETWEEN 8 AND 11 ORDER BY ORDINAL_POSITION;
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'MNT_SLOT' ORDER BY SortOrder;
SELECT 'part' src, SparePartNo id, ZoneCode, Slot FROM dbo.MD_SparePart WHERE ZoneCode = 'SP_EXTRA';
