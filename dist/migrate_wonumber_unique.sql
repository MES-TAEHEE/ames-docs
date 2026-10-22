-- ════════════════════════════════════════════════════════════════════════
--  migrate_wonumber_unique.sql
--  PP_WorkOrder.WoNumber 유니크 인덱스 UX_PP_WorkOrder_WoNumber (10-22)
--
--  WO 채번(PpRepository.NextWoSeq — PP-003 일괄 생성·PP-APS WO 생성·미리보기 dryRun)은
--  `WHERE WoNumber LIKE '{접두어}%'` 를 UPDLOCK·HOLDLOCK 으로 잡는다. WoNumber 에 인덱스가
--  없으면 이 범위 잠금이 테이블 전체(클러스터 PK 전 범위)에 걸려, 채번 트랜잭션이 도는 동안
--  POP 스캔 확정(BumpStepCompleted 의 PP_WorkOrder UPDATE)이 기다리다 30초를 넘기면 실패했다.
--  인덱스가 있으면 잠금은 그 날 접두어(WO-yyyyMMdd-) 키 범위로 좁아지고, 유니크라서 직접 SQL
--  등 다른 경로의 번호 중복도 막는다(NextWoSeq 주석의 "최종 방어선").
--
--  필터 인덱스(WHERE WoNumber IS NOT NULL)로 두지 않는다 — 필터 인덱스는 PP_WorkOrder 를 고치는
--  모든 연결에 QUOTED_IDENTIFIER ON 을 요구해(Msg 1934) -I 없는 sqlcmd DML 이 깨진다.
--  대신 NULL 은 한 행까지만 허용된다(앱 코드는 WoNumber 를 항상 채운다 — 개발 DB 10-22 NULL 0건).
--
--  같은 번호 행이 2건 이상이거나 NULL 행이 2건 이상이면 목록을 보이고 중단한다 — 정리 후 다시 실행.
--  스키마 변경 없음(인덱스만) — 적용 순서 무관, 재실행 안전. PP_WorkOrder 는 사무실·POP 모두 쓰지만
--  유니크 위반은 채번 경합에서만 날 수 있고 그때는 채번 쪽 트랜잭션이 실패한다(현장 확정은 WoNumber 를 바꾸지 않는다).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_wonumber_unique.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PP_WorkOrder_WoNumber' AND object_id = OBJECT_ID('dbo.PP_WorkOrder'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PP_WorkOrder WHERE WoNumber IS NOT NULL GROUP BY WoNumber HAVING COUNT(*) > 1)
    BEGIN
        SELECT WoNumber, COUNT(*) AS Cnt, STRING_AGG(CAST(WoID AS varchar(12)), ',') AS WoIDs
        FROM   dbo.PP_WorkOrder
        WHERE  WoNumber IS NOT NULL
        GROUP  BY WoNumber
        HAVING COUNT(*) > 1;
        THROW 50000, 'PP_WorkOrder 에 같은 WoNumber 행이 있습니다 — 위 목록에서 남길 행만 두고 번호를 고친 뒤 다시 실행하십시오.', 1;
    END
    IF (SELECT COUNT(*) FROM dbo.PP_WorkOrder WHERE WoNumber IS NULL) > 1
    BEGIN
        SELECT WoID, ItemNo, Status, CreatedTS FROM dbo.PP_WorkOrder WHERE WoNumber IS NULL ORDER BY WoID;
        THROW 50000, 'PP_WorkOrder 에 WoNumber 가 NULL 인 행이 2건 이상입니다 — 번호를 채우거나 지운 뒤 다시 실행하십시오.', 1;
    END
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrder_WoNumber ON dbo.PP_WorkOrder (WoNumber);
    PRINT 'UX_PP_WorkOrder_WoNumber created';
END
ELSE PRINT 'UX_PP_WorkOrder_WoNumber already exists';
GO

SELECT i.name AS IndexName, i.is_unique AS IsUnique, i.has_filter AS HasFilter,
       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.object_id = OBJECT_ID('dbo.PP_WorkOrder') AND i.name = 'UX_PP_WorkOrder_WoNumber'
GROUP  BY i.name, i.is_unique, i.has_filter;
GO
