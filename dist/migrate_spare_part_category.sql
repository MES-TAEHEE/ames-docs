/* ------------------------------------------------------------------
   migrate_spare_part_category.sql
   ① 공통코드 SPAREPARTS_CATEGORY 항목 교체: BRG/FLT/SEAL/LUB/ELE → A~K (1자리)
        A 로봇·서보  B PLC·HMI  C 전장  D 케이블·배선  E 모터·드라이브  F 안전
        G 기계  H 유압  I 공압  J 체결  K 기타
   ② 공통코드 SPAREPARTS_EQUIP 신설(적용 설비, 1자리):
        1 사출  2 초음파  3 본딩  4 IMG  5 트리밍  6 A/Wrapping  7 조립  8 도장  9 기타
   ③ MD_SparePart.Category VARCHAR(16) → VARCHAR(1),
      ApplicableEquip VARCHAR(1) 을 Category 바로 다음 컬럼으로 추가 — 컬럼 순서 때문에 테이블 재생성
      (FK 없음, PK + 기본값 2개뿐. MNT_SparePartsTxn 은 PartNo 로만 연결)
      CompatEquipJSON 은 삭제(미사용, ApplicableEquip 이 대체)
   ④ 기존 부품의 분류값을 새 코드로 변환(시연 12건은 부품별, 그 외는 구 코드별 폴백),
      MNT_SparePartsTxn.Category 스냅샷도 마스터 값으로 맞춤
   가드형: 코드는 존재 시 건너뛰고, 테이블 재생성은 ApplicableEquip 컬럼이 없을 때만. 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

-- ── ① SPAREPARTS_CATEGORY ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'SPAREPARTS_CATEGORY')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('SPAREPARTS_CATEGORY', N'예비부품 분류', N'Spare parts category', N'MD_SparePart.Category (1자리)', 1, 'admin@ames.local');

DECLARE @cat TABLE (CodeValue VARCHAR(1), NameKo NVARCHAR(50), NameEn NVARCHAR(50), SortOrder INT);
INSERT @cat VALUES
    ('A', N'로봇·서보 부품',     N'Robot & Servo Parts',   10),
    ('B', N'PLC·HMI 부품',       N'PLC & HMI Parts',       20),
    ('C', N'전장 부품',          N'Electrical Parts',      30),
    ('D', N'케이블·배선 부품',   N'Cable & Wiring Parts',  40),
    ('E', N'모터·드라이브 부품', N'Motor & Drive Parts',   50),
    ('F', N'안전 부품',          N'Safety Parts',          60),
    ('G', N'기계 부품',          N'Mechanical Parts',      70),
    ('H', N'유압 부품',          N'Hydraulic Parts',       80),
    ('I', N'공압 부품',          N'Pneumatic Parts',       90),
    ('J', N'체결 부품',          N'Fastener Parts',       100),
    ('K', N'기타',               N'Others',               110);

DELETE FROM dbo.MD_CodeItem
WHERE  GroupCode = 'SPAREPARTS_CATEGORY' AND CodeValue NOT IN (SELECT CodeValue FROM @cat);
PRINT CONCAT('SPAREPARTS_CATEGORY 구 항목 삭제: ', @@ROWCOUNT);

INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
SELECT 'SPAREPARTS_CATEGORY_' + c.CodeValue, 'SPAREPARTS_CATEGORY', c.CodeValue, c.NameKo, c.NameEn, NULL, c.SortOrder, NULL, 1, NULL, 'admin@ames.local'
FROM   @cat c
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem x WHERE x.GroupCode = 'SPAREPARTS_CATEGORY' AND x.CodeValue = c.CodeValue);
PRINT CONCAT('SPAREPARTS_CATEGORY 항목 추가: ', @@ROWCOUNT);

-- ── ② SPAREPARTS_EQUIP ────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'SPAREPARTS_EQUIP')
BEGIN
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('SPAREPARTS_EQUIP', N'예비부품 적용 설비', N'Spare parts applicable equipment', N'MD_SparePart.ApplicableEquip (1자리)', 1, 'admin@ames.local');
    PRINT 'MD_CodeGroup: SPAREPARTS_EQUIP 추가';
END

DECLARE @eq TABLE (CodeValue VARCHAR(1), NameKo NVARCHAR(50), NameEn NVARCHAR(50), SortOrder INT);
INSERT @eq VALUES
    ('1', N'사출',       N'Injection',  10),
    ('2', N'초음파',     N'Ultrasonic', 20),
    ('3', N'본딩',       N'Bonding',    30),
    ('4', N'IMG',        N'IMG',        40),
    ('5', N'트리밍',     N'Trimming',   50),
    ('6', N'A/Wrapping', N'A/Wrapping', 60),
    ('7', N'조립',       N'Assembly',   70),
    ('8', N'도장',       N'Painting',   80),
    ('9', N'기타',       N'Other',      90);

INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
SELECT 'SPAREPARTS_EQUIP_' + e.CodeValue, 'SPAREPARTS_EQUIP', e.CodeValue, e.NameKo, e.NameEn, NULL, e.SortOrder, NULL, 1, NULL, 'admin@ames.local'
FROM   @eq e
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem x WHERE x.GroupCode = 'SPAREPARTS_EQUIP' AND x.CodeValue = e.CodeValue);
PRINT CONCAT('SPAREPARTS_EQUIP 항목 추가: ', @@ROWCOUNT);

-- ── ③ MD_SparePart 재생성 (Category 1자리 + ApplicableEquip) ────────
IF COL_LENGTH('dbo.MD_SparePart', 'ApplicableEquip') IS NULL
BEGIN
    CREATE TABLE dbo.MD_SparePart_new (
      [PartNo]                    VARCHAR(20)          NOT NULL,
      [PartName]                  NVARCHAR(60)             NULL,
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [UOM]                       VARCHAR(10)              NULL,
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [SupplierID]                VARCHAR(20)              NULL,
      [StorageLoc]                VARCHAR(20)              NULL,
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([PartNo])
    );

    INSERT INTO dbo.MD_SparePart_new
        (PartNo, PartName, Category, ApplicableEquip, UnitCost, UOM, SafetyStock, ReorderPoint, ReorderQty,
         LeadTimeDays, SupplierID, StorageLoc, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT PartNo, PartName,
           CASE
             WHEN Category IN ('A','B','C','D','E','F','G','H','I','J','K') THEN Category   -- 이미 새 코드
             -- 시연 데이터 12건은 부품 성격대로
             WHEN PartNo IN ('SP-BRG-6204','SP-BRG-6206')             THEN 'G'
             WHEN PartNo IN ('SP-FLT-HYD','SP-OIL-46','SP-SEAL-32','SP-SEAL-50') THEN 'H'
             WHEN PartNo = 'SP-FLT-AIR'                               THEN 'I'
             WHEN PartNo = 'SP-MOT-1HP'                               THEN 'E'
             WHEN PartNo IN ('SP-FUSE-25A','SP-HTR-2KW','SP-SENS-PT100') THEN 'C'
             WHEN PartNo = 'SP-GREASE-EP'                             THEN 'K'
             -- 그 외 구 코드 폴백
             WHEN Category IN ('BRG','SEAL') THEN 'G'
             WHEN Category = 'ELE'           THEN 'C'
             WHEN Category IN ('FLT','LUB')  THEN 'K'
             WHEN Category IS NULL           THEN NULL
             ELSE 'K'
           END,
           NULL,
           UnitCost, UOM, SafetyStock, ReorderPoint, ReorderQty,
           LeadTimeDays, SupplierID, StorageLoc, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM dbo.MD_SparePart;
    PRINT CONCAT('MD_SparePart 이관 행수: ', @@ROWCOUNT);

    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    PRINT 'MD_SparePart 재생성 완료 (Category VARCHAR(1), ApplicableEquip 추가)';
END
ELSE
    PRINT 'MD_SparePart: 이미 재생성됨';

-- ── ③-2 CompatEquipJSON 제거 — 어느 화면·리포지토리도 읽지 않는 컬럼(적용 설비는 ApplicableEquip 로 대체) ──
IF COL_LENGTH('dbo.MD_SparePart', 'CompatEquipJSON') IS NOT NULL
BEGIN
    ALTER TABLE dbo.MD_SparePart DROP COLUMN CompatEquipJSON;
    PRINT 'MD_SparePart.CompatEquipJSON 삭제';
END
ELSE
    PRINT 'MD_SparePart.CompatEquipJSON: 이미 없음';

-- ── ④ 입출고 스냅샷의 구 분류값을 마스터 값으로 ─────────────────────
UPDATE t SET t.Category = p.Category
FROM   dbo.MNT_SparePartsTxn t
JOIN   dbo.MD_SparePart p ON p.PartNo = t.PartNo
WHERE  t.Category IS NOT NULL AND LEN(t.Category) > 1;
PRINT CONCAT('MNT_SparePartsTxn.Category 변환: ', @@ROWCOUNT);

COMMIT;

GO

-- 확인(재생성 뒤 별도 배치)
SELECT GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode IN ('SPAREPARTS_CATEGORY','SPAREPARTS_EQUIP') ORDER BY GroupCode, SortOrder;
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' AND ORDINAL_POSITION <= 5 ORDER BY ORDINAL_POSITION;
SELECT PartNo, PartName, Category, ApplicableEquip FROM dbo.MD_SparePart ORDER BY PartNo;
