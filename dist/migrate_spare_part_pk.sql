/* ------------------------------------------------------------------
   migrate_spare_part_pk.sql  — migrate_spare_part_no.sql 다음에 적용
   ① MD_SparePart 의 PK 를 PartNo → SparePartNo 로. PartNo 는 고유 인덱스(UX_MD_SparePart_PartNo)로 유지.
      현재고 컬럼 OnHandQty INT NOT NULL DEFAULT 0 추가(UOM 다음). 기존 값은 입출고 마지막 BalanceAfter 로 백필.
   ② MNT_SparePartsTxn 재생성: 마스터에 있는 값(PartName·Category·StorageLoc·SupplierCode)은 버리고
      SparePartNo(MD_SparePart 를 가리키는 논리 참조, FK 없음) · MoveType · Qty · BalanceBefore · BalanceAfter ·
      RefType/RefID · Note · TxnAt · ActorID · 감사 컬럼만 남긴다(UnitPrice 도 제거 — 금액은 마스터 UnitCost). SparePartsTxnID 는 IDENTITY_INSERT 로 보존.
      BalanceBefore 는 행 자체 값으로 역산(IN: After−Qty, OUT: After+Qty) — 시연 데이터의 TxnAt 순서가 잔량 사슬과 어긋나 LAG 는 못 쓴다.
   ③ 재고 증감은 MntRepository.AdjustSparePartStock 이 마스터 OnHandQty 갱신 + 이력 INSERT 를 한 트랜잭션으로 처리한다.
   가드형: MNT_SparePartsTxn 에 SparePartNo 가 없을 때만 실행. 두 테이블은 서로 FK 로 묶이므로 한 트랜잭션에서 함께 재생성.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF COL_LENGTH('dbo.MNT_SparePartsTxn', 'SparePartNo') IS NULL
BEGIN
    IF COL_LENGTH('dbo.MD_SparePart', 'SparePartNo') IS NULL
        THROW 50001, 'migrate_spare_part_no.sql 을 먼저 적용하세요 (MD_SparePart.SparePartNo 없음).', 1;

    -- 마스터에 없는 부품번호의 이력이 있으면 매핑 불가 → 중단
    -- (옛 컬럼 t.PartNo 를 읽는 문장은 동적 SQL — 재생성 뒤 재실행해도 배치 컴파일이 깨지지 않게)
    EXEC sp_executesql N'
        IF EXISTS (SELECT 1 FROM dbo.MNT_SparePartsTxn t WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_SparePart p WHERE p.PartNo = t.PartNo))
            THROW 50002, ''MNT_SparePartsTxn 에 MD_SparePart 에 없는 PartNo 가 있어 SparePartNo 로 매핑할 수 없습니다. 먼저 정리하세요.'', 1;';

    -- ── ① MD_SparePart ─────────────────────────────────────────────
    CREATE TABLE dbo.MD_SparePart_new (
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- EOS-SP-{분류}{적용설비}-{yy}{순번4}, 저장 시 자동 채번
      [Category]                  VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_CATEGORY (A~K)
      [ApplicableEquip]           VARCHAR(1)               NULL,  -- 공통코드 SPAREPARTS_EQUIP (1~9)
      [PartNo]                    VARCHAR(20)          NOT NULL,  -- 제조사/구매 부품번호 (고유)
      [PartName]                  NVARCHAR(60)             NULL,
      [UnitCost]                  DECIMAL(12,2)            NULL,
      [UOM]                       VARCHAR(10)              NULL,
      [OnHandQty]                 INT                  NOT NULL CONSTRAINT DF_MD_SparePart_OnHandQty DEFAULT 0,  -- 현재고 (입출고로만 변경)
      [SafetyStock]               INT                      NULL,
      [ReorderPoint]              INT                      NULL,
      [ReorderQty]                INT                      NULL,
      [LeadTimeDays]              INT                      NULL,
      [SupplierID]                VARCHAR(20)              NULL,
      [StorageLoc]                VARCHAR(20)              NULL,
      [ActiveFlag]                BIT                      NULL CONSTRAINT DF_MD_SparePart_ActiveFlag_v3 DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MD_SparePart_CreatedTS_v3 DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_SparePart_new PRIMARY KEY CLUSTERED ([SparePartNo]),
      CONSTRAINT UX_MD_SparePart_PartNo_new UNIQUE ([PartNo])
    );

    -- 현재고 백필은 옛 이력(t.PartNo)을 읽으므로 동적 SQL
    EXEC sp_executesql N'
        INSERT INTO dbo.MD_SparePart_new
            (SparePartNo, Category, ApplicableEquip, PartNo, PartName, UnitCost, UOM, OnHandQty, SafetyStock, ReorderPoint, ReorderQty,
             LeadTimeDays, SupplierID, StorageLoc, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT p.SparePartNo, p.Category, p.ApplicableEquip, p.PartNo, p.PartName, p.UnitCost, p.UOM,
               ISNULL(b.BalanceAfter, 0),
               p.SafetyStock, p.ReorderPoint, p.ReorderQty, p.LeadTimeDays, p.SupplierID, p.StorageLoc, p.ActiveFlag,
               p.CreatedBy, p.CreatedTS, p.ModifiedBy, p.ModifiedTS
        FROM   dbo.MD_SparePart p
        OUTER APPLY (SELECT TOP 1 t.BalanceAfter FROM dbo.MNT_SparePartsTxn t WHERE t.PartNo = p.PartNo ORDER BY t.SparePartsTxnID DESC) b;
        PRINT CONCAT(''MD_SparePart 이관(현재고 백필) 행수: '', @@ROWCOUNT);';

    -- ── ② MNT_SparePartsTxn ────────────────────────────────────────
    CREATE TABLE dbo.MNT_SparePartsTxn_new (
      [SparePartsTxnID]           INT IDENTITY         NOT NULL,
      [SparePartNo]               VARCHAR(16)          NOT NULL,  -- 참조 -> MD_SparePart.SparePartNo (FK 없음)
      [MoveType]                  VARCHAR(10)          NOT NULL,  -- IN 입고 / OUT 출고 / ADJ 조정(Qty 부호 허용)
      [Qty]                       INT                  NOT NULL,
      [BalanceBefore]             INT                  NOT NULL,  -- 처리 전 현재고
      [BalanceAfter]              INT                  NOT NULL,  -- 처리 후 현재고 (= MD_SparePart.OnHandQty 스냅샷)
      [RefType]                   VARCHAR(15)              NULL,  -- PO / MWO / ADJ …
      [RefID]                     VARCHAR(24)              NULL,
      [Note]                      NVARCHAR(500)            NULL,
      [TxnAt]                     DATETIME2            NOT NULL CONSTRAINT DF_MNT_SparePartsTxn_TxnAt DEFAULT SYSDATETIME(),
      [ActorID]                   NVARCHAR(450)            NULL,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_MNT_SparePartsTxn_CreatedTS DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MNT_SparePartsTxn_new PRIMARY KEY CLUSTERED ([SparePartsTxnID])
    );

    -- 옛 구조(t.PartNo 등)를 읽는 이관문은 동적 SQL
    EXEC sp_executesql N'
        SET IDENTITY_INSERT dbo.MNT_SparePartsTxn_new ON;
        INSERT INTO dbo.MNT_SparePartsTxn_new
            (SparePartsTxnID, SparePartNo, MoveType, Qty, BalanceBefore, BalanceAfter, RefType, RefID, Note, TxnAt, ActorID,
             CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT t.SparePartsTxnID, p.SparePartNo, ISNULL(t.MoveType, ''ADJ''), ISNULL(t.Qty, 0),
               ISNULL(t.BalanceAfter, 0) - CASE WHEN t.MoveType = ''IN'' THEN ISNULL(t.Qty, 0) WHEN t.MoveType = ''OUT'' THEN -ISNULL(t.Qty, 0) ELSE 0 END,
               ISNULL(t.BalanceAfter, 0), t.RefType, t.RefID, t.Note, ISNULL(t.TxnAt, t.CreatedTS), t.ActorID,
               t.CreatedBy, t.CreatedTS, t.ModifiedBy, t.ModifiedTS
        FROM   dbo.MNT_SparePartsTxn t
        JOIN   dbo.MD_SparePart p ON p.PartNo = t.PartNo;
        PRINT CONCAT(''MNT_SparePartsTxn 이관 행수: '', @@ROWCOUNT);
        SET IDENTITY_INSERT dbo.MNT_SparePartsTxn_new OFF;';

    -- ── 교체 ───────────────────────────────────────────────────────
    DROP TABLE dbo.MNT_SparePartsTxn;
    DROP TABLE dbo.MD_SparePart;
    EXEC sp_rename 'dbo.MD_SparePart_new', 'MD_SparePart';
    EXEC sp_rename 'dbo.PK_MD_SparePart_new', 'PK_MD_SparePart', 'OBJECT';
    EXEC sp_rename 'dbo.UX_MD_SparePart_PartNo_new', 'UX_MD_SparePart_PartNo', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_ActiveFlag_v3', 'DF_MD_SparePart_ActiveFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_SparePart_CreatedTS_v3', 'DF_MD_SparePart_CreatedTS', 'OBJECT';
    EXEC sp_rename 'dbo.MNT_SparePartsTxn_new', 'MNT_SparePartsTxn';
    EXEC sp_rename 'dbo.PK_MNT_SparePartsTxn_new', 'PK_MNT_SparePartsTxn', 'OBJECT';

    CREATE INDEX IX_MNT_SparePartsTxn_Part ON dbo.MNT_SparePartsTxn (SparePartNo, TxnAt DESC, SparePartsTxnID DESC);
    PRINT 'MD_SparePart(PK SparePartNo · OnHandQty) · MNT_SparePartsTxn(SparePartNo 참조 · BalanceBefore) 재생성 완료';
END
ELSE
    PRINT 'MNT_SparePartsTxn: 이미 SparePartNo 구조';

-- SparePartNo 는 FK 없는 논리 참조(사용자 결정). 앞선 버전이 만든 FK 가 있으면 제거 — 삭제 보호는 리포지토리가 이력 존재를 검사해 처리
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_MNT_SparePartsTxn_SparePart')
BEGIN
    ALTER TABLE dbo.MNT_SparePartsTxn DROP CONSTRAINT FK_MNT_SparePartsTxn_SparePart;
    PRINT 'FK_MNT_SparePartsTxn_SparePart 제거';
END

-- 거래 단가(UnitPrice)도 불필요(사용자 결정) — 재고 금액은 마스터 UnitCost 로 계산. 앞선 버전이 남긴 컬럼이 있으면 제거
IF COL_LENGTH('dbo.MNT_SparePartsTxn', 'UnitPrice') IS NOT NULL
BEGIN
    ALTER TABLE dbo.MNT_SparePartsTxn DROP COLUMN UnitPrice;
    PRINT 'MNT_SparePartsTxn.UnitPrice 제거';
END

COMMIT;
GO

-- 확인(재생성 뒤 별도 배치)
SELECT 'MD_SparePart' AS T, COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_SparePart' ORDER BY ORDINAL_POSITION;
SELECT 'MNT_SparePartsTxn' AS T, COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MNT_SparePartsTxn' ORDER BY ORDINAL_POSITION;
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS child, OBJECT_NAME(fk.referenced_object_id) AS parent FROM sys.foreign_keys fk WHERE fk.referenced_object_id = OBJECT_ID('dbo.MD_SparePart');
SELECT p.SparePartNo, p.PartNo, p.OnHandQty, (SELECT TOP 1 BalanceAfter FROM dbo.MNT_SparePartsTxn t WHERE t.SparePartNo = p.SparePartNo ORDER BY SparePartsTxnID DESC) AS LastBal FROM dbo.MD_SparePart p ORDER BY p.SparePartNo;
