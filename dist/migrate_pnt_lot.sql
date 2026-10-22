-- ════════════════════════════════════════════════════════════════════════
--  migrate_pnt_lot.sql
--  PR_PntLot — PNT(도장) 원천 LOT (1 LOT = 1 EA) + 구 PNT 저장 프로시저 17개 DROP
--
--  PNT-MAIN(2026-10-22)은 INJ·IMG 와 같은 "라벨 발행 → OK/NG 판정 → 확정" 모델로 실적을 낸다.
--  라벨 발행 버튼이 tbl_Lot(ProcessCode='PNT', BatchSize 1) + PR_PntLot(RAW) 를 만들고,
--  판정 OK 가 ConfirmStatus 를 CONFIRMED 로 바꾸며 PR_ProductionResult 1 EA 를 남긴다.
--  코어 스캔이 없어 tbl_Lot.ParentLotID 는 NULL 이고, 확정 차감(BackflushRepository)이
--  BOM 의 사출품 자식(MD_Item.InjFlag = 1)까지 라인 재고에서 뺀다.
--
--  PR_ImgLot 을 쓰지 않는 이유: 원단·본딩 컬럼은 PNT 에 없고, 재작업·불량 쓰기가
--  tbl_Lot.ProcessCode 로 공정별 보조 테이블을 가르는 기존 분기에 세 번째 테이블을 더하는
--  것이 가장 작은 변경이다. 외래키는 두지 않는다(10-08 사용자 결정).
--
--  §2 는 구 PNT 모듈(PNT_VirtualLot 가상 LOT 모델)의 저장 프로시저 17개를 이름을 하나씩 들어
--  지운다 — 새 SP_POP_PNT_* (migrate_pop_stored_procedures.sql) 와 이름이 겹치지 않으므로
--  적용 순서와 무관하고, LIKE 패턴을 쓰지 않아 재실행해도 새 프로시저를 지우지 않는다.
--  구 PNT_* 테이블 22개는 그대로 둔다(삭제 여부는 Claude_확인사항.md).
--
--  전제: migrate_lotno_rule.sql (LotNo 채번 — MD_Line.LotPrefix P1·P2), migrate_inj_lot_line_created.sql
--        (IX_tbl_Lot_Line_Created — 오늘 LOT 조회) 적용 후. 재실행 안전.
--  배포: 이 파일 → migrate_pop_stored_procedures.sql(새 SP_POP_PNT_*) → 신 AMES.Pop/Api/Web.
--        구 Pop 은 §2 뒤 구 PNT 화면(/pnt01~09)이 Could not find stored procedure 로 깨지므로 신 Pop 과 함께 반영한다
--        (QC-07 은 프로시저 이름·결과 형태가 같아 구 Pop 에서도 동작한다).
--  적용:  sqlcmd -S 192.168.0.132,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_pnt_lot.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) PR_PntLot ─────────────────────────────────────────────────────────
IF OBJECT_ID('dbo.PR_PntLot', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PR_PntLot (
      [LotID]                     INT                  NOT NULL,  -- = tbl_Lot.LotID (1:1, FK 없음)
      [EquipID]                   VARCHAR(20)              NULL,  -- 라인 대표 설비 MD_Equipment.EquipID
      [ConfirmStatus]             VARCHAR(16)          NOT NULL CONSTRAINT DF_PR_PntLot_ConfirmStatus DEFAULT ('RAW'),  -- RAW / CONFIRMED / DEFECT / SCRAPPED
      [ConfirmedAt]               DATETIME2                NULL,
      [ConfirmedBy]               VARCHAR(20)              NULL,  -- 사번
      [ConfirmedSessionID]        INT                      NULL,
      [CustomerCode]              VARCHAR(20)              NULL,  -- 발행 시점 열린 WO 의 수주처 MD_Customer.CustomerCode (라벨 V 토큰, 재출력 불변)
      [PrintedCount]              INT                  NOT NULL CONSTRAINT DF_PR_PntLot_PrintedCount DEFAULT ((0)),
      [CreatedBy]                 VARCHAR(20)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_PR_PntLot_CreatedTS DEFAULT (SYSDATETIME()),
      [ModifiedBy]                VARCHAR(20)              NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_PR_PntLot PRIMARY KEY CLUSTERED ([LotID])
    );
    PRINT 'PR_PntLot created';
END
ELSE
    PRINT 'PR_PntLot already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PR_PntLot_Status' AND object_id = OBJECT_ID('dbo.PR_PntLot'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PR_PntLot_Status ON dbo.PR_PntLot ([ConfirmStatus]);
    PRINT 'IX_PR_PntLot_Status created';
END
GO

-- ── 2) 구 PNT 모듈 저장 프로시저 17개 DROP (이름 명시 — 새 SP_POP_PNT_* 와 겹치지 않음) ──
IF OBJECT_ID('dbo.SP_POP_PNT_ListTodayPlan',                 'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListTodayPlan;
IF OBJECT_ID('dbo.SP_POP_PNT_ListAllTodayLots',              'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListAllTodayLots;
IF OBJECT_ID('dbo.SP_POP_PNT_IssueLot',                      'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_IssueLot;
IF OBJECT_ID('dbo.SP_POP_PNT_ListAvailableJigs',             'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListAvailableJigs;
IF OBJECT_ID('dbo.SP_POP_PNT_BindAndLoad_UpdPNT_VirtualLot', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_BindAndLoad_UpdPNT_VirtualLot;
IF OBJECT_ID('dbo.SP_POP_PNT_BindAndLoad_InsPNT_JigLoad',    'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_BindAndLoad_InsPNT_JigLoad;
IF OBJECT_ID('dbo.SP_POP_PNT_BindAndLoad_InsPNT_LineEvent',  'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_BindAndLoad_InsPNT_LineEvent;
IF OBJECT_ID('dbo.SP_POP_PNT_ListRecentEvents',              'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListRecentEvents;
IF OBJECT_ID('dbo.SP_POP_PNT_GetOvenStatus',                 'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_GetOvenStatus;
IF OBJECT_ID('dbo.SP_POP_PNT_ListOvenSamples',               'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListOvenSamples;
IF OBJECT_ID('dbo.SP_POP_PNT_ConfirmUnload_UpdPNT_VirtualLot','P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ConfirmUnload_UpdPNT_VirtualLot;
IF OBJECT_ID('dbo.SP_POP_PNT_ConfirmUnload_InsPNT_LineEvent','P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ConfirmUnload_InsPNT_LineEvent;
IF OBJECT_ID('dbo.SP_POP_PNT_ApplyLabel',                    'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ApplyLabel;
IF OBJECT_ID('dbo.SP_POP_PNT_ListDefectCodes',               'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListDefectCodes;
IF OBJECT_ID('dbo.SP_POP_PNT_RegisterDefect',                'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_RegisterDefect;
IF OBJECT_ID('dbo.SP_POP_PNT_GetShiftBuckets',               'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_GetShiftBuckets;
IF OBJECT_ID('dbo.SP_POP_PNT_ListRalColors',                 'P') IS NOT NULL DROP PROCEDURE dbo.SP_POP_PNT_ListRalColors;
PRINT 'old SP_POP_PNT_* (virtual-lot module) dropped where present';
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.PR_PntLot')
ORDER  BY c.column_id;
GO
