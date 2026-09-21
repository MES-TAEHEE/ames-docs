-- ════════════════════════════════════════════════════════════════════════
--  migrate_po_sync_service_key.sql
--  PP-002 "API 가져오기" — AMES.Web 이 AMES.Api 의 PO 수집 수동 실행(POST /api/pp/po-sync/run)을 부를 때 쓰는 서비스 키
--
--  1) 공통코드 SW_POSYNC_AUTH 에 예약 행 AMES_SERVICE_KEY 를 만든다 — 값(Description)은 적용 시점에 CRYPT_GEN_RANDOM 으로
--     생성하므로 리포지토리에 비밀값이 남지 않고 DB 마다 다르다. 이미 있으면 건드리지 않는다(키 교체는 MD-26 에서).
--     CodeValue 가 16자라 소스 키(13자 이하)와 겹칠 수 없고, PoSyncConfig.Resolve 는 소스 키로만 인증 행을 찾아 이 행을 무시한다.
--     Web·Api 가 같은 DB 에서 읽으므로 배포본 설정을 맞출 필요가 없다. 행이 없거나 UseFlag=0 · 16자 미만 · 중복이면
--     서비스 키 경로가 닫혀 Web 의 실행 버튼이 401 안내만 띄운다(POP/PDA Bearer 세션 경로는 그대로).
--  2) 그룹 설명에 예약 행 안내를 덧붙인다(migrate_po_sync.sql 의 MERGE 는 기존 그룹 설명을 고치지 않는다).
--
--  선행: dist/migrate_po_sync.sql (그룹 SW_POSYNC_AUTH). 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_po_sync_service_key.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'SW_POSYNC_AUTH')
BEGIN
    RAISERROR('SW_POSYNC_AUTH 그룹이 없다 — dist/migrate_po_sync.sql 을 먼저 적용할 것', 16, 1);
    RETURN;
END

-- ── 1. 서비스 키 행 ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'SW_POSYNC_AUTH' AND CodeValue = 'AMES_SERVICE_KEY')
BEGIN
    INSERT dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES ('SW_POSYNC_AUTH_AMES_SERVICE_KEY', 'SW_POSYNC_AUTH', 'AMES_SERVICE_KEY',
            N'AMES Web → Api 수동 실행 서비스 키', N'AMES Web → Api manual-run service key',
            N'Header:X-AMES-Service-Key',
            LOWER(CONVERT(varchar(64), CRYPT_GEN_RANDOM(32), 2)),
            999, 1, 'migrate', SYSDATETIME());
    PRINT 'SW_POSYNC_AUTH AMES_SERVICE_KEY created (random 64 hex)';
END
ELSE
    PRINT 'SW_POSYNC_AUTH AMES_SERVICE_KEY already exists — left as is';
GO

-- ── 2. 그룹 설명 ─────────────────────────────────────────────────────
UPDATE dbo.MD_CodeGroup
SET    Description = N'CodeValue=소스 키. Attribute1=Query:{매개변수이름} | Bearer | Basic | Header:{헤더이름}. Description=키 | 토큰 | user:pw | 헤더값. 행이 없으면 인증 없이 호출. 예약 행 AMES_SERVICE_KEY=Web→Api 수동 실행 키(16자 이상)',
       ModifiedBy  = N'migrate', ModifiedTS = SYSDATETIME()
WHERE  GroupCode = 'SW_POSYNC_AUTH'
  AND  ISNULL(Description, N'') NOT LIKE N'%AMES_SERVICE_KEY%';
PRINT CONCAT('SW_POSYNC_AUTH group description updated: ', @@ROWCOUNT);
GO

SELECT CodeID, CodeValue, Attribute1, LEN(Description) AS KeyLen, UseFlag
FROM   dbo.MD_CodeItem
WHERE  GroupCode = 'SW_POSYNC_AUTH'
ORDER  BY SortOrder, CodeValue;
GO
