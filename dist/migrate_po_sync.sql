-- ════════════════════════════════════════════════════════════════════════
--  migrate_po_sync.sql
--  고객사 SRM 구매오더(MM31006 INQUERY) 자동 수집 — 공통코드 설정 기반
--
--  1) 공통코드 그룹 SW_POSYNC(전역) · SW_POSYNC_SOURCE(고객사) · SW_POSYNC_URL · SW_POSYNC_AUTH
--     ScheduledWorker 설정 그룹은 "SW_" + Worker Code, 이름은 "ScheduledWorker · " 로 시작한다(MD-26 에서 SW_ 로 모아 찾는다).
--  2) 구 이름 PO_SYNC* 로 적용된 DB 이관 — 항목을 새 그룹으로 옮기고 CodeID 접두어도 바꾼 뒤(MD-26 은 CodeID 를
--     GroupCode_CodeValue 로 만든다) 비게 된 구 그룹을 지운다. 새 CodeID 가 이미 있는 항목은 옮기지 않고 경고만 남긴다.
--  3) SW_POSYNC 항목 INTERVAL=30(분) · WINDOW=-60,0(발주일 창)
--     TICK_SEC · STARTUP_DELAY_SEC · TIMEOUT_SEC 는 시드하지 않는다 — 없으면 Api appsettings ScheduledWorker 공통 기본값.
--     소스 행은 시드하지 않는다(운영은 MD-26 에서 등록, dev 는 seed_po_sync_dev.sql).
--  4) SYS_InterfaceMonitor POSYNC-* 행을 공통코드 값으로 보정 — Direction 'IN' → 'INBOUND'(IF_DIRECTION), ConnStatus 'ERR' → 'ERROR'(IF_STATUS).
--
--  선행: dist/migrate_md_codeitem_widen.sql (MD_CodeItem.Attribute1 200 · Description 500).
--  URL·인증 토큰을 공통코드에 두므로 넓히지 않은 DB 에서는 MD-26 저장이 길이 초과로 실패한다.
--  그룹·항목 MERGE 자체는 폭과 무관해 이 스크립트는 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_po_sync.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1. 공통코드 그룹 ─────────────────────────────────────────────────
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('SW_POSYNC',        N'ScheduledWorker · PO 자동수집 설정',   N'ScheduledWorker · PO Sync Settings',
     N'INTERVAL: Attribute1=기본 주기(분, 0=전체 중지) / WINDOW: Attribute1=발주일(PO_DATE) 창 "-N,M"(오늘-N ~ 오늘+M일) / 선택 TICK_SEC·STARTUP_DELAY_SEC·TIMEOUT_SEC: Attribute1=초, 없으면 Api appsettings ScheduledWorker 기본값'),
    ('SW_POSYNC_SOURCE', N'ScheduledWorker · PO 자동수집 고객사', N'ScheduledWorker · PO Sync Sources',
     N'CodeValue=소스 키(13자 이하). Attribute1=귀속 MD_Customer.CustomerID. Description="CORCD=;BIZCD=;VENDCD=;PURC_ORG=" 필수, "WINDOW=-N,M" 선택(주기는 전역 INTERVAL 만). UseFlag=0 이면 건너뜀'),
    ('SW_POSYNC_URL',    N'ScheduledWorker · PO 자동수집 URL',    N'ScheduledWorker · PO Sync Endpoints',
     N'CodeValue=소스 키. Description=엔드포인트 절대 URL'),
    ('SW_POSYNC_AUTH',   N'ScheduledWorker · PO 자동수집 인증',   N'ScheduledWorker · PO Sync Auth',
     N'CodeValue=소스 키. Attribute1=Query:{매개변수이름} | Bearer | Basic | Header:{헤더이름}. Description=키 | 토큰 | user:pw | 헤더값. 행이 없으면 인증 없이 호출. 예약 행 AMES_SERVICE_KEY=Web→Api 수동 실행 키(16자 이상)')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());
PRINT 'SW_POSYNC* code groups merged';
GO

-- ── 2. 구 이름 PO_SYNC* 이관 ─────────────────────────────────────────
BEGIN TRAN;

DECLARE @Move TABLE (OldCodeID varchar(41) PRIMARY KEY, NewCodeID varchar(41), NewGroup varchar(20));
INSERT @Move (OldCodeID, NewCodeID, NewGroup)
SELECT i.CodeID,
       CASE WHEN i.CodeID LIKE 'PO[_]SYNC%' THEN 'SW_POSYNC' + SUBSTRING(i.CodeID, 8, 41) ELSE i.CodeID END,
       'SW_POSYNC' + SUBSTRING(i.GroupCode, 8, 20)
FROM   dbo.MD_CodeItem i
WHERE  i.GroupCode IN ('PO_SYNC', 'PO_SYNC_SOURCE', 'PO_SYNC_URL', 'PO_SYNC_AUTH');

DELETE m
OUTPUT deleted.OldCodeID AS SkippedCodeID, deleted.NewCodeID AS AlreadyExists
FROM   @Move m
WHERE  m.NewCodeID <> m.OldCodeID
  AND  EXISTS (SELECT 1 FROM dbo.MD_CodeItem n WHERE n.CodeID = m.NewCodeID);

UPDATE i
SET    i.CodeID     = m.NewCodeID,
       i.GroupCode  = m.NewGroup,
       i.ModifiedBy = N'migrate',
       i.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_CodeItem i
JOIN   @Move m ON m.OldCodeID = i.CodeID;

UPDATE i
SET    i.ParentCodeID = m.NewCodeID
FROM   dbo.MD_CodeItem i
JOIN   @Move m ON m.OldCodeID = i.ParentCodeID;

DECLARE @moved int = (SELECT COUNT(*) FROM @Move);
IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode IN ('PO_SYNC', 'PO_SYNC_SOURCE', 'PO_SYNC_URL', 'PO_SYNC_AUTH'))
    PRINT 'WARNING: PO_SYNC* 에 남은 항목이 있다 — SW_POSYNC* 에 같은 CodeID 가 이미 있어 옮기지 않았다. MD-26 에서 값을 확인해 정리할 것';

DELETE g
FROM   dbo.MD_CodeGroup g
WHERE  g.GroupCode IN ('PO_SYNC', 'PO_SYNC_SOURCE', 'PO_SYNC_URL', 'PO_SYNC_AUTH')
  AND  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem i WHERE i.GroupCode = g.GroupCode);

COMMIT;
PRINT CONCAT('PO_SYNC* -> SW_POSYNC* moved items: ', @moved);
GO

-- ── 3. 전역 기본값 ───────────────────────────────────────────────────
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_POSYNC_INTERVAL', 'SW_POSYNC', 'INTERVAL', N'기본 수집 주기(분)', N'Default interval (min)', N'30',    1),
    ('SW_POSYNC_WINDOW',   'SW_POSYNC', 'WINDOW',   N'기본 조회 창(일)',   N'Default window (days)',  N'-60,0', 2)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'SW_POSYNC INTERVAL / WINDOW merged';
GO

-- ── 4. 모니터 행 공통코드 값 보정 ────────────────────────────────────
-- 09-17 이전 Worker 는 Direction 'IN'·실패 ConnStatus 'ERR' 로 넣었다. 공통코드 IF_DIRECTION·IF_STATUS 값으로 맞춘다
-- (SYS-Interfaces 의 장애 KPI 는 DOWN/ERROR/FAULT 만 센다).
UPDATE dbo.SYS_InterfaceMonitor
SET    Direction = 'INBOUND', ModifiedBy = N'migrate', ModifiedTS = SYSDATETIME()
WHERE  InterfaceCode LIKE 'POSYNC-%' AND Direction = 'IN';
PRINT CONCAT('POSYNC-* Direction IN -> INBOUND: ', @@ROWCOUNT);

UPDATE dbo.SYS_InterfaceMonitor
SET    ConnStatus = 'ERROR', ModifiedBy = N'migrate', ModifiedTS = SYSDATETIME()
WHERE  InterfaceCode LIKE 'POSYNC-%' AND ConnStatus = 'ERR';
PRINT CONCAT('POSYNC-* ConnStatus ERR -> ERROR: ', @@ROWCOUNT);
GO

SELECT g.GroupCode, g.GroupNameEn, i.CodeID, i.CodeValue, i.Attribute1
FROM   dbo.MD_CodeGroup g
LEFT JOIN dbo.MD_CodeItem i ON i.GroupCode = g.GroupCode
WHERE  g.GroupCode LIKE 'SW[_]POSYNC%' OR g.GroupCode LIKE 'PO[_]SYNC%'
ORDER  BY g.GroupCode, i.SortOrder;
GO
