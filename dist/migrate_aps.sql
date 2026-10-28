/* ------------------------------------------------------------------
   migrate_aps.sql
   APS 생산계획(PP-APS) — 라인 선행일 예외 · 실행 결과 테이블 3개 · 공통코드 2그룹 (2026-09-28, 10-13 BoxQty 폐지)

     §1 MD_Item.BoxQty              폐지(10-13) — 초판(09-28)이 만든 컬럼이 있으면 떨어뜨린다. 포장 단위는 PalletQty(적입수량) 하나(APS PackSize · WO 올림 · PP-001 대조)
     §2 MD_MoldItem.PartCavityCount 폐지(09-30) — 초판(09-28)이 만든 컬럼이 있으면 떨어뜨린다. 품번별 캐비티는 APS 가 금형 CavityCount ÷ 활성 품번 수(내림, 최소 1)로 추정
     §3 MD_ApsLineStage             라인별 사출 선행일·재고 사용·가동 시간 패턴(PatternID, 10-06 §3-b 로 추가) 예외 (PP-APS 설정 다이얼로그가 관리, MD-26 아님)
     §4 PP_ApsRun · PP_ApsPlanLine · PP_ApsRunWo   실행 스냅샷(JSON 3개) + 정규화 계획 행 + WO 연결
     §5 공통코드 APS_SETTING(9) · APS_COVER_TIER(3)  — ApsSettingsLoader 가 읽는다. §5-b 는 폐기 키 SHIFT_DAY_H·SHIFT_NIGHT_H 행을 지운다(10-06). 값 수정은 PP-APS 설정 다이얼로그(E 권한) 또는 MD-26

   · §1 은 ALTER TABLE DROP COLUMN 뿐이라 다른 마이그레이션 순서와 무관하다(초판은 BoxQty 를 넣으려고 MD_Item 을 재생성했다 — 그 재생성 목록·가드는 없앴다).
   · §3~§5 는 IF OBJECT_ID / MERGE(WHEN NOT MATCHED) 라 기존 행·값을 덮지 않는다.
   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b  (-I: MD_Mold 계산 컬럼 인덱스 때문에 QUOTED_IDENTIFIER ON 필수)
       sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_aps.sql
   · 이 마이그레이션 없이 신 Web 을 올려도 동작한다(신 Web 은 BoxQty 를 읽지 않는다). 구 Web 은 BoxQty 를 읽으므로 적용 뒤에는 MD-003·PP-APS 가 예외 — 신 Web 과 함께 반영한다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ═══════════════════════════════════════════════════════════════════
-- §1 MD_Item.BoxQty — 폐지(10-13). 초판(09-28)이 만든 컬럼이 있으면 떨어뜨린다(재실행 안전)
--    포장 단위는 PalletQty(적입수량) 하나로 쓴다(APS PackSize · BOM 규칙 WO 올림 · PP-001 SRM PACK_QTY 대조). 값 이관 없음.
-- ═══════════════════════════════════════════════════════════════════
IF COL_LENGTH('dbo.MD_Item', 'BoxQty') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MD_Item')
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_Item'), 'BoxQty', 'ColumnId')
                 AND name = 'MS_Description')
        EXEC sys.sp_dropextendedproperty @name = N'MS_Description', @level0type = N'SCHEMA', @level0name = N'dbo',
             @level1type = N'TABLE', @level1name = N'MD_Item', @level2type = N'COLUMN', @level2name = N'BoxQty';
    ALTER TABLE dbo.MD_Item DROP COLUMN BoxQty;
    PRINT N'✓ MD_Item.BoxQty 삭제 — 포장 단위는 PalletQty(적입수량)를 쓴다';
END
ELSE
    PRINT N'· MD_Item.BoxQty 없음 — 건너뜀';
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
        -- FK 는 두지 않는다(10-08 원칙) — 라인·패턴 삭제는 MasterDataRepository 사용처 검사가 막는다
        CONSTRAINT PK_MD_ApsLineStage PRIMARY KEY CLUSTERED (LineID)
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
        CONSTRAINT PK_PP_ApsPlanLine PRIMARY KEY CLUSTERED (PlanLineID)
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
        CONSTRAINT PK_PP_ApsRunWo PRIMARY KEY CLUSTERED (RunWoID)
    );
    CREATE NONCLUSTERED INDEX IX_PP_ApsRunWo_Wo ON dbo.PP_ApsRunWo (WoID);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'APS 계획 행 → 생성 WO 연결(수주 FIFO 조각마다 1행). FK 없음(10-08 원칙)', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_ApsRunWo';
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
    -- 2026-10-08: 사출 계획의 교대 분할 방식. 0 = 교대 가동 시간 비율(ShiftSplit.Proportional), 1 = 앞 교대의 남은 시간부터 차례로 채움(ShiftSplit.Sequential)
    ('APS_SETTING_SHIFT_FILL_SEQ',  'APS_SETTING', 'SHIFT_FILL_SEQ',  N'사출 계획 앞 교대부터 순차 채움 (1/0)', N'Fill injection plan shift by shift from the first (1/0)', N'0', 11),
    ('APS_COVER_TIER_100',          'APS_COVER_TIER', '100',          N'일수요 100 이상',                    N'Daily demand >= 100',                   N'0.4',  3),
    ('APS_COVER_TIER_20',           'APS_COVER_TIER', '20',           N'일수요 20 이상',                     N'Daily demand >= 20',                    N'0.7',  2),
    ('APS_COVER_TIER_0',            'APS_COVER_TIER', '0',            N'일수요 0 이상',                      N'Daily demand >= 0',                     N'1.5',  1)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'APS_SETTING (9) / APS_COVER_TIER (3) code items merged';
GO

-- §5-b 폐기 키 삭제 (10-06) — SHIFT_DAY_H·SHIFT_NIGHT_H 는 패턴이 없으면 조회를 막는 규칙 뒤로 어디서도 읽지 않는다(ApsSettingsLoader.RetiredKeys).
--      초판이 만든 행이 남아 있으면 지운다. 재실행 안전(없으면 0건).
DELETE FROM dbo.MD_CodeItem WHERE GroupCode = 'APS_SETTING' AND CodeValue IN ('SHIFT_DAY_H', 'SHIFT_NIGHT_H');
PRINT CONCAT('APS_SETTING retired rows (SHIFT_DAY_H/SHIFT_NIGHT_H) deleted: ', @@ROWCOUNT);
GO

-- ═══════════════════════════════════════════════════════════════════
-- 확인 (GO 뒤 별도 배치)
-- ═══════════════════════════════════════════════════════════════════
SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.MD_Item')     AND c.name IN ('PalletQty', 'ToteFlag', 'BoxQty', 'ScanRequired', 'ActiveFlag') ORDER BY c.column_id;   -- BoxQty 는 안 보여야 정상(폐지)
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
