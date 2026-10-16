/* ------------------------------------------------------------------
   migrate_spare_part_no.sql  — migrate_spare_part_category.sql 다음에 적용
   ① MD_SparePart 에 SparePartNo VARCHAR(16) NOT NULL UNIQUE 를 첫 컬럼으로 추가
   ② 컬럼 순서 재배치: SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost … (테이블 재생성)
      PK 는 종전대로 PartNo (MNT_SparePartsTxn·MD_PmTemplateStep 이 PartNo 값으로 연결)
   ③ 채번 규칙  EOS-SP-{Category}{ApplicableEquip}-{yy}{순번4}  (분류·적용설비·연도별 0001 부터)
      기존 행은 CreatedTS 연도·등록순으로 백필. 채번에 분류·적용설비가 필요하므로
      NULL 인 기존 행은 분류 K(기타)·적용설비 9(기타)로 채운다.
   가드형: SparePartNo 컬럼이 없을 때만 재생성. 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF COL_LENGTH('dbo.MD_SparePart', 'SparePartNo') IS NULL
BEGIN
    UPDATE dbo.MD_SparePart SET Category = 'K'        WHERE Category IS NULL;
    UPDATE dbo.MD_SparePart SET ApplicableEquip = '9' WHERE ApplicableEquip IS NULL;
    PRINT '채번 전 분류/적용설비 NULL 보정 완료';

    CREATE TABLE dbo.MD_SparePart_new (
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- EOS-SP-{분류}{적용설비}-{yy}{순번4}, 저장 시 자동 채번
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [PartNo]                    VARCHAR(20)          NOT NULL,
      [PartName]                  NVARCHAR(60)             NULL,
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [UOM]                       VARCHAR(10)              NULL,
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [SupplierID]                VARCHAR(20)              NULL,
      [StorageLoc]                VARCHAR(20)              NULL,
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag_v2 DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS_v2 DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([PartNo]),
      CONSTRAINT UQ_MD_SparePart_SparePartNo UNIQUE ([SparePartNo])
    );

    ;WITH src AS (
        SELECT p.*,
               'EOS-SP-' + p.Category + p.ApplicableEquip + '-' + RIGHT(CONVERT(varchar(4), YEAR(ISNULL(p.CreatedTS, SYSDATETIME()))), 2) AS Prefix,
               ROW_NUMBER() OVER (PARTITION BY p.Category, p.ApplicableEquip, YEAR(ISNULL(p.CreatedTS, SYSDATETIME()))
                                  ORDER BY p.CreatedTS, p.PartNo) AS Seq
        FROM dbo.MD_SparePart p
    )
    INSERT INTO dbo.MD_SparePart_new
        (SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, UOM, SafetyStock, ReorderPoint, ReorderQty,
         LeadTimeDays, SupplierID, StorageLoc, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT Prefix + RIGHT('000' + CAST(Seq AS varchar(4)), 4),
           Category, ApplicableEquip, PartNo, PartName, UnitCost, UOM, SafetyStock, ReorderPoint, ReorderQty,
           LeadTimeDays, SupplierID, StorageLoc, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM src;
    PRINT CONCAT('MD_SparePart 이관·채번 행수: ', @@ROWCOUNT);

    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_ActiveFlag_v2', 'DF_MD_SparePart_ActiveFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_CreatedTS_v2', 'DF_MD_SparePart_CreatedTS', 'OBJECT';
    PRINT 'MD_SparePart 재생성 완료 (SparePartNo 첫 컬럼 · UNIQUE · 컬럼 순서 재배치)';
END
ELSE
    PRINT 'MD_SparePart: SparePartNo 이미 있음';

COMMIT;
GO

-- 확인(재생성 뒤 별도 배치)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' AND ORDINAL_POSITION <= 6 ORDER BY ORDINAL_POSITION;
SELECT name, type_desc, is_unique FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.MD_SparePart') AND name IS NOT NULL;
SELECT SparePartNo, Category, ApplicableEquip, PartNo, PartName FROM dbo.MD_SparePart ORDER BY SparePartNo;
