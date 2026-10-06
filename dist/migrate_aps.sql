/* ------------------------------------------------------------------
   migrate_aps.sql
   APS 생산계획(PP-APS) — 마스터 gap 컬럼 1개 · 라인 선행일 예외 · 실행 결과 테이블 3개 · 공통코드 2그룹 (2026-09-28)

     §1 MD_Item.BoxQty              INT NULL   ToteFlag 바로 뒤 — 포장 단위(박스당 수량). APS PackSize, NULL 이면 1 + 경고
     §2 MD_MoldItem.PartCavityCount 폐지(09-30) — 초판(09-28)이 만든 컬럼이 있으면 떨어뜨린다. 품번별 캐비티는 APS 가 금형 CavityCount ÷ 활성 품번 수(내림, 최소 1)로 추정
     §3 MD_ApsLineStage             라인별 사출 선행일·재고 사용·가동 시간 패턴(PatternID, 10-06 §3-b 로 추가) 예외 (PP-APS 설정 다이얼로그가 관리, MD-26 아님)
     §4 PP_ApsRun · PP_ApsPlanLine · PP_ApsRunWo   실행 스냅샷(JSON 3개) + 정규화 계획 행 + WO 연결
     §5 공통코드 APS_SETTING(8) · APS_COVER_TIER(3)  — ApsSettingsLoader 가 읽는다. §5-b 는 폐기 키 SHIFT_DAY_H·SHIFT_NIGHT_H 행을 지운다(10-06). 값 수정은 PP-APS 설정 다이얼로그(E 권한) 또는 MD-26

   · §1 은 컬럼 순서 때문에 테이블을 재생성한다(migrate_md_item_pallet_qty.sql 방식). 가드: 컬럼이 없거나
     기준 컬럼 바로 뒤가 아니면 재생성. 들어오는 FK 와 컬럼 설명(MS_Description)은 DB 에서 읽어 되살린다.
     재생성 전에 컬럼 구성을 재생성 목록과 대조해, 목록 밖 컬럼(다른 마이그레이션이 먼저 추가한 컬럼 등)·빠진 컬럼이 있으면
     THROW 50000 으로 중단한다(나가는 FK·인덱스도 — MD_Item 은 PK 밖에 재선언 목록이 없다).
     목록을 고친 뒤 재실행할 것.
   · §1 은 migrate_md_item_pallet_qty.sql(PalletQty·MaxPalletQty·ToteFlag) · migrate_pp_mrp_result.sql(LeadTimeDays) ·
     migrate_md_item_mount_pos.sql(MountPos) 이 적용된 DB 를 전제한다(AMES_Schema.sql 로 새로 만든 DB 는 포함). 빠지면 위 대조가 THROW.
     dev 의 MD_Item.ScanRequired(케이스 입고 개별 스캔, 10-06 — AMES_Schema.sql·migrate_md_item_case_receive.sql 이 만든다)는 BoxQty 바로 뒤에
     같은 이름의 기본 제약(DF_MD_Item_ScanRequired)으로 재생성하고, 없는 DB 에서는 0 으로 만든다.
     dev 의 migrate_md_item_inj_flag.sql(InjFlag, ItemCategory 바로 뒤, 10-03)은 있어도 없어도 된다 — 재생성 목록에 InjFlag 가 들어 있어
     없으면 0 으로 만들고 있으면 값을 옮긴다. 단 그 스크립트의 재생성 목록에는 BoxQty 가 없어 **이 마이그레이션 뒤에 돌리면 BoxQty 가
     데이터째 사라진다**(10-05 복제본에서 실제 발생) — inj_flag 를 먼저, migrate_aps 를 나중에 적용하거나 이 스크립트를 다시 돌려 복구한다(값은 NULL).
   · §3~§5 는 IF OBJECT_ID / MERGE(WHEN NOT MATCHED) 라 기존 행·값을 덮지 않는다.
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b  (-I: MD_Mold 계산 컬럼 인덱스 때문에 QUOTED_IDENTIFIER ON 필수)
       sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_aps.sql
   · 이 마이그레이션 없이 신 Web 을 올리면 PP-APS 진입·MD-003 품목 목록이 매번 예외(Invalid column name 'BoxQty').
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ═══════════════════════════════════════════════════════════════════
-- §1 MD_Item.BoxQty (ToteFlag 바로 뒤)
-- ═══════════════════════════════════════════════════════════════════
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.MD_Item', 'BoxQty') IS NOT NULL
   AND (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'BoxQty')
     = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'ToteFlag') + 1
   AND (COL_LENGTH('dbo.MD_Item', 'ScanRequired') IS NULL
        OR (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'ScanRequired')
         = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MD_Item') AND name = 'BoxQty') + 1)
BEGIN
    PRINT N'· MD_Item.BoxQty 이미 ToteFlag 뒤에 있음';
    RETURN;
END

-- 0) 컬럼·나가는 FK·인덱스 대조 — 아래 CREATE·INSERT 목록 밖 컬럼은 재생성 때 데이터째 사라지므로 중단 (BoxQty 는 순서만 다른 초판이면 값을 옮긴다).
--    §1 은 PK_MD_Item 말고는 나가는 FK·인덱스를 다시 만들지 않는다 — 있으면 재생성 때 사라지므로 중단(재선언 목록 = 없음)
DECLARE @expect TABLE (name nvarchar(128) NOT NULL PRIMARY KEY);
INSERT @expect (name) VALUES
    (N'ItemNo'), (N'ItemName'), (N'ItemNameEN'), (N'ItemType'), (N'ItemCategory'), (N'DefaultUOM'), (N'RoutingType'),
    (N'MinStock'), (N'MaxStock'), (N'SafetyStock'), (N'UnitCost'), (N'CustItemNoSAV'), (N'CustItemNoGEO'), (N'DrawingNo'),
    (N'PalletQty'), (N'MaxPalletQty'), (N'ToteFlag'),
    (N'ActiveFlag'), (N'CreatedBy'), (N'CreatedTS'), (N'ModifiedBy'), (N'ModifiedTS'),
    (N'CarType'), (N'PGN'), (N'ALC'), (N'MountPos'), (N'SparePartNo'), (N'ApplicableEquipment'), (N'MakerName'), (N'LeadTimeDays');
DECLARE @unexpected nvarchar(max) =
    (SELECT STRING_AGG(c.name, N', ') WITHIN GROUP (ORDER BY c.column_id)
     FROM   sys.columns c
     WHERE  c.object_id = OBJECT_ID('dbo.MD_Item') AND c.name NOT IN (N'BoxQty', N'InjFlag', N'ScanRequired')   -- 셋은 없으면 만들고 있으면 옮긴다
       AND  NOT EXISTS (SELECT 1 FROM @expect e WHERE e.name COLLATE DATABASE_DEFAULT = c.name COLLATE DATABASE_DEFAULT));
DECLARE @missing nvarchar(max) =
    (SELECT STRING_AGG(e.name, N', ') FROM @expect e WHERE COL_LENGTH('dbo.MD_Item', e.name) IS NULL);
DECLARE @extraFk nvarchar(max) =
    (SELECT STRING_AGG(fk.name, N', ') FROM sys.foreign_keys fk
     WHERE  fk.parent_object_id = OBJECT_ID('dbo.MD_Item'));
DECLARE @extraIx nvarchar(max) =
    (SELECT STRING_AGG(i.name, N', ') FROM sys.indexes i
     WHERE  i.object_id = OBJECT_ID('dbo.MD_Item') AND i.type > 0 AND i.is_primary_key = 0 AND i.is_hypothetical = 0);
IF @unexpected IS NOT NULL OR @missing IS NOT NULL OR @extraFk IS NOT NULL OR @extraIx IS NOT NULL
BEGIN
    DECLARE @guardMsg nvarchar(2048) = CONCAT(N'MD_Item 재생성 중단 — 재생성 목록과 다르다. 목록 밖 컬럼: ', COALESCE(@unexpected, N'(없음)'),
                                              N' / 빠진 컬럼: ', COALESCE(@missing, N'(없음)'),
                                              N' / 목록 밖 나가는 FK: ', COALESCE(@extraFk, N'(없음)'),
                                              N' / 목록 밖 인덱스: ', COALESCE(@extraIx, N'(없음)'), N'. §1 의 CREATE TABLE·INSERT·재선언 목록에 반영한 뒤 다시 실행할 것');
    THROW 50000, @guardMsg, 1;
END

BEGIN TRAN;

-- 1) 들어오는 FK 를 스크립트로 저장 (단일·복합 컬럼 모두)
DECLARE @fk TABLE (Name sysname, DropSql nvarchar(max), AddSql nvarchar(max));
INSERT @fk (Name, DropSql, AddSql)
SELECT fk.name,
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name),
       N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
         + CASE WHEN fk.is_not_trusted = 1 THEN N' WITH NOCHECK' ELSE N' WITH CHECK' END
         + N' ADD CONSTRAINT ' + QUOTENAME(fk.name) + N' FOREIGN KEY ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.parent_object_id, fc.parent_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N') REFERENCES [dbo].[MD_Item] ('
         + (SELECT STRING_AGG(QUOTENAME(COL_NAME(fc.referenced_object_id, fc.referenced_column_id)), N',') WITHIN GROUP (ORDER BY fc.constraint_column_id)
            FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id = fk.object_id)
         + N')'
         + CASE fk.delete_referential_action WHEN 1 THEN N' ON DELETE CASCADE' WHEN 2 THEN N' ON DELETE SET NULL' WHEN 3 THEN N' ON DELETE SET DEFAULT' ELSE N'' END
         + CASE fk.update_referential_action WHEN 1 THEN N' ON UPDATE CASCADE' WHEN 2 THEN N' ON UPDATE SET NULL' WHEN 3 THEN N' ON UPDATE SET DEFAULT' ELSE N'' END
         + CASE WHEN fk.is_disabled = 1 THEN N'; ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) ELSE N'' END
FROM   sys.foreign_keys fk
JOIN   sys.tables t ON t.object_id = fk.parent_object_id
WHERE  fk.referenced_object_id = OBJECT_ID('dbo.MD_Item');

-- 2) 컬럼·테이블 설명 저장
DECLARE @ep TABLE (ColName sysname NULL, Name sysname, Value sql_variant);
INSERT @ep (ColName, Name, Value)
SELECT CASE WHEN ep.minor_id = 0 THEN NULL ELSE COL_NAME(ep.major_id, ep.minor_id) END, ep.name, ep.value
FROM   sys.extended_properties ep
WHERE  ep.class = 1 AND ep.major_id = OBJECT_ID('dbo.MD_Item');

DECLARE @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT DropSql FROM @fk;
OPEN c; FETCH NEXT FROM c INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM c INTO @sql; END
CLOSE c; DEALLOCATE c;

-- 3) 새 순서로 재생성
IF OBJECT_ID('dbo.MD_Item_new', 'U') IS NOT NULL DROP TABLE dbo.MD_Item_new;
CREATE TABLE dbo.MD_Item_new (
    ItemNo              varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    ItemName            nvarchar(80)  COLLATE Korean_Wansung_CI_AS NOT NULL,
    ItemNameEN          nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    ItemType            varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    ItemCategory        varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    InjFlag             bit           NOT NULL DEFAULT ((0)),
    DefaultUOM          varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    RoutingType         char(1)       COLLATE Korean_Wansung_CI_AS NULL,
    MinStock            decimal(14,4) NULL,
    MaxStock            decimal(14,4) NULL,
    SafetyStock         decimal(14,4) NULL,
    UnitCost            decimal(14,2) NULL,
    CustItemNoSAV       varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    CustItemNoGEO       varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    DrawingNo           varchar(30)   COLLATE Korean_Wansung_CI_AS NULL,
    PalletQty           int           NULL,
    MaxPalletQty        int           NULL,
    ToteFlag            bit           NOT NULL DEFAULT ((0)),
    BoxQty              int           NULL,
    ScanRequired        bit           NOT NULL CONSTRAINT DF_MD_Item_new_ScanRequired DEFAULT ((0)),
    ActiveFlag          bit           NULL DEFAULT ((1)),
    CreatedBy           varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
    CreatedTS           datetime2(7)  NULL DEFAULT (sysdatetime()),
    ModifiedBy          varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
    ModifiedTS          datetime2(7)  NULL,
    CarType             varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    PGN                 varchar(4)    COLLATE Korean_Wansung_CI_AS NULL,
    ALC                 varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
    MountPos            varchar(2)    COLLATE Korean_Wansung_CI_AS NULL,
    SparePartNo         nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    ApplicableEquipment nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    MakerName           nvarchar(80)  COLLATE Korean_Wansung_CI_AS NULL,
    LeadTimeDays        int           NULL,
    CONSTRAINT PK_MD_Item_new PRIMARY KEY CLUSTERED (ItemNo)
);

-- 새 컬럼이 이미 있으면(순서만 다른 초판) 값을 옮기고, 없으면 NULL
SET @sql = N'INSERT INTO dbo.MD_Item_new
    (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, InjFlag, DefaultUOM, RoutingType,
     MinStock, MaxStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, DrawingNo,
     PalletQty, MaxPalletQty, ToteFlag, BoxQty, ScanRequired,
     ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS,
     CarType, PGN, ALC, MountPos, SparePartNo, ApplicableEquipment, MakerName, LeadTimeDays)
SELECT ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, '
    + CASE WHEN COL_LENGTH('dbo.MD_Item', 'InjFlag') IS NULL THEN N'0' ELSE N'InjFlag' END + N', DefaultUOM, RoutingType,
       MinStock, MaxStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, DrawingNo,
       PalletQty, MaxPalletQty, ToteFlag, '
    + CASE WHEN COL_LENGTH('dbo.MD_Item', 'BoxQty') IS NULL THEN N'NULL' ELSE N'BoxQty' END + N', '
    + CASE WHEN COL_LENGTH('dbo.MD_Item', 'ScanRequired') IS NULL THEN N'0' ELSE N'ScanRequired' END + N',
       ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS,
       CarType, PGN, ALC, MountPos, SparePartNo, ApplicableEquipment, MakerName, LeadTimeDays
FROM dbo.MD_Item;';
EXEC sp_executesql @sql;
DECLARE @moved int = @@ROWCOUNT;

DROP TABLE dbo.MD_Item;
EXEC sp_rename 'dbo.MD_Item_new', 'MD_Item';
EXEC sp_rename 'dbo.PK_MD_Item_new', 'PK_MD_Item', 'OBJECT';
EXEC sp_rename 'dbo.DF_MD_Item_new_ScanRequired', 'DF_MD_Item_ScanRequired', 'OBJECT';

-- 4) 설명 되살리기 + 새 컬럼 설명
DECLARE @col sysname, @name sysname, @val sql_variant;
DECLARE e CURSOR LOCAL FAST_FORWARD FOR SELECT ColName, Name, Value FROM @ep WHERE ColName IS NULL OR COL_LENGTH('dbo.MD_Item', ColName) IS NOT NULL;
OPEN e; FETCH NEXT FROM e INTO @col, @name, @val;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @col IS NULL
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item';
    ELSE
        EXEC sys.sp_addextendedproperty @name = @name, @value = @val, @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = @col;
    FETCH NEXT FROM e INTO @col, @name, @val;
END
CLOSE e; DEALLOCATE e;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Item'), 'InjFlag', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'사출품 여부 — 품목 유형 SUB 만 1 이 될 수 있다(그 밖의 유형은 저장할 때 0) · bit', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = N'InjFlag';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_Item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Item'), 'BoxQty', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'박스 수량 — 포장 단위(박스 1개당 제품 수). APS 공급 올림 단위, NULL 이면 1 + 경고 · int', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = N'BoxQty';

-- 5) FK 되살리기
DECLARE a CURSOR LOCAL FAST_FORWARD FOR SELECT AddSql FROM @fk;
OPEN a; FETCH NEXT FROM a INTO @sql;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC (@sql); FETCH NEXT FROM a INTO @sql; END
CLOSE a; DEALLOCATE a;

DECLARE @fkCount int = (SELECT COUNT(*) FROM @fk);
COMMIT;
PRINT CONCAT(N'✓ MD_Item 재생성 (', @moved, N' 행) — BoxQty 를 ToteFlag 뒤에(InjFlag 는 ItemCategory 뒤), FK ', @fkCount, N' 개 복원');
GO

-- ═══════════════════════════════════════════════════════════════════
-- §2 MD_MoldItem.PartCavityCount — 폐지(09-30). 초판이 추가했던 컬럼을 떨어뜨린다(재실행 안전)
-- ═══════════════════════════════════════════════════════════════════
IF COL_LENGTH('dbo.MD_MoldItem', 'PartCavityCount') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MD_MoldItem')
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_MoldItem'), 'PartCavityCount', 'ColumnId')
                 AND name = 'MS_Description')
        EXEC sys.sp_dropextendedproperty @name = N'MS_Description', @level0type = N'SCHEMA', @level0name = N'dbo',
             @level1type = N'TABLE', @level1name = N'MD_MoldItem', @level2type = N'COLUMN', @level2name = N'PartCavityCount';
    ALTER TABLE dbo.MD_MoldItem DROP COLUMN PartCavityCount;
    PRINT N'✓ MD_MoldItem.PartCavityCount 삭제 — 품번별 캐비티는 금형 CavityCount ÷ 활성 품번 수로 추정한다';
END
ELSE
    PRINT N'· MD_MoldItem.PartCavityCount 없음 — 건너뜀';
GO

-- ═══════════════════════════════════════════════════════════════════
-- §3 MD_ApsLineStage — 라인별 사출 선행일·재고 사용 예외
-- ═══════════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.MD_ApsLineStage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_ApsLineStage (
        LineID      varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        OffsetDays  int           NOT NULL DEFAULT ((1)),
        UseStock    bit           NOT NULL DEFAULT ((1)),
        Note        nvarchar(200) COLLATE Korean_Wansung_CI_AS NULL,
        PatternID   varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        CreatedBy   varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS   datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        ModifiedBy  varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        ModifiedTS  datetime2(7)  NULL,
        CONSTRAINT PK_MD_ApsLineStage PRIMARY KEY CLUSTERED (LineID),
        CONSTRAINT FK_MD_ApsLineStage_Line    FOREIGN KEY (LineID)    REFERENCES dbo.MD_Line (LineID),
        CONSTRAINT FK_MD_ApsLineStage_Pattern FOREIGN KEY (PatternID) REFERENCES dbo.MD_LineTimePattern (PatternID)
    );
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 라인별 단계 예외 — 사출 라인 행: 선행일(OffsetDays)·재고 사용(UseStock)·가동 시간 패턴(PatternID). 행이 없으면 APS_SETTING.INJ_OFFSET_DAYS(사출)/0(완제품)·DEFAULT_PATTERN. PP-APS 설정 다이얼로그가 관리', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage';
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'대상 라인 → MD_Line(LineID) · varchar(20)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage', @level2type = N'COLUMN', @level2name = N'LineID';
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'사출 선행일(사출 라인) / 0(완제품 라인) · int', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage', @level2type = N'COLUMN', @level2name = N'OffsetDays';
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'재고를 소요에 반영할지 · bit', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage', @level2type = N'COLUMN', @level2name = N'UseStock';
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 가 이 라인의 능력(주간/야간)을 읽을 가동 시간 패턴 → MD_LineTimePattern(PatternID). NULL = APS_SETTING.DEFAULT_PATTERN. PP_LineSchedule 저장 패턴·자동 해석보다 우선(2026-10-06) · varchar(20)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage', @level2type = N'COLUMN', @level2name = N'PatternID';
    PRINT N'✓ MD_ApsLineStage 생성';
END
ELSE
    PRINT N'· MD_ApsLineStage 이미 존재';
GO

-- §3-b PatternID (2026-10-06) — 초판(09-28)이 만든 테이블에 컬럼·FK·설명을 더한다. 끝에 붙는 컬럼이 아니라 Note 다음이어야 하지만
--      ALTER ADD 는 끝에 붙는다 — 이 테이블은 재생성하지 않는다(행이 적고 PP-APS 설정 다이얼로그만 읽는다, 순서는 조회 SQL 이 컬럼명으로 집는다).
IF COL_LENGTH('dbo.MD_ApsLineStage', 'PatternID') IS NULL
BEGIN
    ALTER TABLE dbo.MD_ApsLineStage ADD PatternID varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
    PRINT N'✓ MD_ApsLineStage.PatternID 추가';
END
ELSE
    PRINT N'· MD_ApsLineStage.PatternID 이미 존재';
GO
IF OBJECT_ID('dbo.FK_MD_ApsLineStage_Pattern', 'F') IS NULL
BEGIN
    ALTER TABLE dbo.MD_ApsLineStage WITH CHECK ADD CONSTRAINT FK_MD_ApsLineStage_Pattern FOREIGN KEY (PatternID) REFERENCES dbo.MD_LineTimePattern (PatternID);
    PRINT N'✓ FK_MD_ApsLineStage_Pattern 추가';
END
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_ApsLineStage') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_ApsLineStage'), 'PatternID', 'ColumnId') AND name = 'MS_Description')
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 가 이 라인의 능력(주간/야간)을 읽을 가동 시간 패턴 → MD_LineTimePattern(PatternID). NULL = APS_SETTING.DEFAULT_PATTERN. PP_LineSchedule 저장 패턴·자동 해석보다 우선(2026-10-06) · varchar(20)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_ApsLineStage', @level2type = N'COLUMN', @level2name = N'PatternID';
GO

-- ═══════════════════════════════════════════════════════════════════
-- §4 PP_ApsRun · PP_ApsPlanLine · PP_ApsRunWo
-- ═══════════════════════════════════════════════════════════════════
IF OBJECT_ID('dbo.PP_ApsRun', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_ApsRun (
        RunID        int           IDENTITY(1,1) NOT NULL,
        LineID       varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        BaseDate     date          NOT NULL,
        Days         int           NOT NULL,
        CustomerID   varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        IncludeOpen  bit           NOT NULL DEFAULT ((0)),
        Status       varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        SettingsJson nvarchar(max) COLLATE Korean_Wansung_CI_AS NOT NULL,
        BundleJson   nvarchar(max) COLLATE Korean_Wansung_CI_AS NOT NULL,
        ResultJson   nvarchar(max) COLLATE Korean_Wansung_CI_AS NOT NULL,
        WarningCount int           NOT NULL DEFAULT ((0)),
        CreatedBy    varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS    datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        ModifiedBy   varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        ModifiedTS   datetime2(7)  NULL,
        CONSTRAINT PK_PP_ApsRun PRIMARY KEY CLUSTERED (RunID)
    );
    CREATE NONCLUSTERED INDEX IX_PP_ApsRun_Line_Base ON dbo.PP_ApsRun (LineID, BaseDate DESC, RunID DESC);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 실행(PP-APS 저장) — 선택 라인·기준일·일수와 Settings/PlanBundle/PlanResult JSON 스냅샷. Status Saved | Released', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_ApsRun';
    PRINT N'✓ PP_ApsRun 생성';
END
ELSE
    PRINT N'· PP_ApsRun 이미 존재';

IF OBJECT_ID('dbo.PP_ApsPlanLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_ApsPlanLine (
        PlanLineID  int           IDENTITY(1,1) NOT NULL,
        RunID       int           NOT NULL,
        Kind        char(3)       COLLATE Korean_Wansung_CI_AS NOT NULL,
        ItemNo      varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        LineID      varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        PlanDate    date          NOT NULL,
        Demand      decimal(14,3) NOT NULL DEFAULT ((0)),
        Supply      decimal(14,3) NOT NULL DEFAULT ((0)),
        Requirement decimal(14,3) NOT NULL DEFAULT ((0)),
        PlanDay     decimal(14,3) NOT NULL DEFAULT ((0)),
        PlanNight   decimal(14,3) NOT NULL DEFAULT ((0)),
        Stock       decimal(14,3) NOT NULL DEFAULT ((0)),
        Locked      bit           NOT NULL DEFAULT ((0)),
        Status      varchar(10)   COLLATE Korean_Wansung_CI_AS NOT NULL DEFAULT ('ok'),
        WoID        int           NULL,
        SameItem    bit           NOT NULL DEFAULT ((0)),
        CONSTRAINT PK_PP_ApsPlanLine PRIMARY KEY CLUSTERED (PlanLineID),
        CONSTRAINT FK_PP_ApsPlanLine_Run FOREIGN KEY (RunID) REFERENCES dbo.PP_ApsRun (RunID) ON DELETE CASCADE
    );
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_ApsPlanLine ON dbo.PP_ApsPlanLine (RunID, Kind, ItemNo, PlanDate);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 계획 행(정규화 사본) — Kind ASM(완제품: Demand·Supply) | INJ(사출: Requirement·PlanDay·PlanNight). 정본은 PP_ApsRun 의 JSON. WoID = 첫 연결 WO(전체는 PP_ApsRunWo). SameItem = 같은 품번 규칙(§4.2 ①) 사출 행 1 / BOM 규칙 행 0 — 「WO 생성」 대상 판정', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_ApsPlanLine';
    PRINT N'✓ PP_ApsPlanLine 생성';
END
ELSE
    PRINT N'· PP_ApsPlanLine 이미 존재';

-- SameItem 이 없는 선행 적용본(컬럼 추가 전 CREATE) 보정 — 재실행 안전. 같은 품번 규칙(§4.2 ①) 사출 행 표시, 「WO 생성」 대상 판정
IF COL_LENGTH('dbo.PP_ApsPlanLine', 'SameItem') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ApsPlanLine ADD SameItem bit NOT NULL CONSTRAINT DF_PP_ApsPlanLine_SameItem DEFAULT ((0));
    PRINT N'✓ PP_ApsPlanLine.SameItem 추가';
END

IF OBJECT_ID('dbo.PP_ApsRunWo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_ApsRunWo (
        RunWoID    int           IDENTITY(1,1) NOT NULL,
        RunID      int           NOT NULL,
        PlanLineID int           NOT NULL,
        WoID       int           NOT NULL,
        SoID       int           NULL,
        Qty        decimal(14,3) NOT NULL,
        CreatedBy  varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS  datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        CONSTRAINT PK_PP_ApsRunWo PRIMARY KEY CLUSTERED (RunWoID),
        CONSTRAINT FK_PP_ApsRunWo_Run FOREIGN KEY (RunID) REFERENCES dbo.PP_ApsRun (RunID) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_PP_ApsRunWo_Wo ON dbo.PP_ApsRunWo (WoID);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 계획 행 → 생성 WO 연결(수주 FIFO 조각마다 1행). WoID·PlanLineID 는 PP_ 관례대로 FK 없음(RunID 두 경로 CASCADE 금지)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_ApsRunWo';
    PRINT N'✓ PP_ApsRunWo 생성';
END
ELSE
    PRINT N'· PP_ApsRunWo 이미 존재';
GO

-- ═══════════════════════════════════════════════════════════════════
-- §5 공통코드 APS_SETTING · APS_COVER_TIER (WHEN NOT MATCHED — 기존 값은 덮지 않는다)
-- ═══════════════════════════════════════════════════════════════════
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('APS_SETTING',    N'APS · 생산계획 설정',  N'APS · Planning Settings',
     N'CodeValue=설정 키, Attribute1=값. DEFAULT_COVER·ROUND_TO·TRIM_LAST_DAY·INJ_OFFSET_DAYS·PULL_FORWARD·SAFETY_TERM·WARN_STATUS·DEFAULT_PATTERN. 파싱 실패·누락은 기본값 + PP-APS 경고(DEFAULT_PATTERN 미지정은 조회 차단)'),
    ('APS_COVER_TIER', N'APS · 커버일수 구간',  N'APS · Days-of-cover Tiers',
     N'CodeValue=일평균 수요 하한(숫자), Attribute1=커버일수. 하한 내림차순으로 첫 매치. 어느 구간에도 안 걸리면 APS_SETTING.DEFAULT_COVER')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());
PRINT 'APS_SETTING / APS_COVER_TIER code groups merged';
GO

-- APS_COVER_TIER 의 SortOrder 는 스펙 §3.3 "SortOrder 내림차순으로 첫 매치" 그대로 — 일수요 하한이 큰 구간이 큰 값(100→3, 20→2, 0→1).
-- (ApsSettingsLoader.Parse 는 MD-26 수동 편집에 흔들리지 않게 MinDailyDemand 내림차순으로 다시 정렬하고, PP-APS 설정 다이얼로그는 SortOrder 내림차순으로 보인다 — 셋이 같은 순서다.)
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('APS_SETTING_DEFAULT_COVER',   'APS_SETTING', 'DEFAULT_COVER',   N'커버일수 기본값',                   N'Default days of cover',                 N'0.7',  1),
    ('APS_SETTING_ROUND_TO',        'APS_SETTING', 'ROUND_TO',        N'올림 단위(포장 단위 없는 품번)',      N'Round-to (items without pack size)',    N'5',    2),
    ('APS_SETTING_TRIM_LAST_DAY',   'APS_SETTING', 'TRIM_LAST_DAY',   N'마지막 날 기준수요 0 (1/0)',          N'Trim last-day base demand (1/0)',       N'1',    3),
    ('APS_SETTING_INJ_OFFSET_DAYS', 'APS_SETTING', 'INJ_OFFSET_DAYS', N'사출 선행일 기본',                   N'Injection offset days',                 N'1',    4),
    -- SHIFT_DAY_H(5)·SHIFT_NIGHT_H(6)는 10-06 폐기 — 교대 시간은 가동 시간 패턴(MD_LineTimeSegment × WORK_SHIFT.SortOrder)에서만 나온다. 아래 §5-b 가 기존 행을 지운다
    ('APS_SETTING_PULL_FORWARD',    'APS_SETTING', 'PULL_FORWARD',    N'능력 초과분 앞 근무일로 당김 (1/0)',  N'Pull forward over-capacity supply (1/0)', N'0',  7),
    ('APS_SETTING_SAFETY_TERM',     'APS_SETTING', 'SAFETY_TERM',     N'목표재고 = max(커버, 안전재고) (1/0)', N'Target = max(cover, safety stock) (1/0)', N'0',  8),
    ('APS_SETTING_WARN_STATUS',     'APS_SETTING', 'WARN_STATUS',     N'재고 < 안전재고 warn 상태 (1/0)',     N'warn status below safety stock (1/0)',  N'0',    9),
    -- 2026-10-06: 사출 라인의 APS 가동 시간 패턴 기본값(MD_LineTimePattern.PatternID, 전역 패턴). 빈 값 = 미지정 → 라인 지정(MD_ApsLineStage.PatternID)도 없는 사출 라인이 있으면 PP-APS 조회가 막힌다
    ('APS_SETTING_DEFAULT_PATTERN', 'APS_SETTING', 'DEFAULT_PATTERN', N'가동 시간 패턴 기본값(전역 패턴 ID)', N'Default line time pattern (global pattern ID)', NULL, 10),
    ('APS_COVER_TIER_100',          'APS_COVER_TIER', '100',          N'일수요 100 이상',                    N'Daily demand >= 100',                   N'0.4',  3),
    ('APS_COVER_TIER_20',           'APS_COVER_TIER', '20',           N'일수요 20 이상',                     N'Daily demand >= 20',                    N'0.7',  2),
    ('APS_COVER_TIER_0',            'APS_COVER_TIER', '0',            N'일수요 0 이상',                      N'Daily demand >= 0',                     N'1.5',  1)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'APS_SETTING (8) / APS_COVER_TIER (3) code items merged';
GO

-- §5-b 폐기 키 삭제 (10-06) — SHIFT_DAY_H·SHIFT_NIGHT_H 는 패턴이 없으면 조회를 막는 규칙 뒤로 어디서도 읽지 않는다(ApsSettingsLoader.RetiredKeys).
--      초판이 만든 행이 남아 있으면 지운다. 재실행 안전(없으면 0건).
DELETE FROM dbo.MD_CodeItem WHERE GroupCode = 'APS_SETTING' AND CodeValue IN ('SHIFT_DAY_H', 'SHIFT_NIGHT_H');
PRINT CONCAT('APS_SETTING retired rows (SHIFT_DAY_H/SHIFT_NIGHT_H) deleted: ', @@ROWCOUNT);
GO

-- ═══════════════════════════════════════════════════════════════════
-- 확인 (GO 뒤 별도 배치)
-- ═══════════════════════════════════════════════════════════════════
SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_Item')     AND c.name IN ('ToteFlag', 'BoxQty', 'ScanRequired', 'ActiveFlag') ORDER BY c.column_id;
SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_MoldItem') AND c.name IN ('CavityCount', 'PartCavityCount', 'MoldCategory') ORDER BY c.column_id;   -- PartCavityCount 는 안 보여야 정상(폐지)
SELECT v.name, OBJECT_ID('dbo.' + v.name, 'U') AS ObjectId FROM (VALUES ('MD_ApsLineStage'), ('PP_ApsRun'), ('PP_ApsPlanLine'), ('PP_ApsRunWo')) v(name);
SELECT i.name, i.is_unique FROM sys.indexes i WHERE i.object_id = OBJECT_ID('dbo.MD_MoldItem') AND i.type_desc = 'NONCLUSTERED' ORDER BY i.name;
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS ParentTable, OBJECT_NAME(fk.referenced_object_id) AS RefTable
FROM   sys.foreign_keys fk
WHERE  fk.referenced_object_id IN (OBJECT_ID('dbo.MD_Item'), OBJECT_ID('dbo.PP_ApsRun'), OBJECT_ID('dbo.MD_Line'))
   OR  fk.parent_object_id = OBJECT_ID('dbo.MD_MoldItem')
ORDER  BY fk.name;
SELECT GroupCode, COUNT(*) AS Items FROM dbo.MD_CodeItem WHERE GroupCode IN ('APS_SETTING', 'APS_COVER_TIER') GROUP BY GroupCode ORDER BY GroupCode;
GO
