-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_number_index.sql
--  MNT 번호 컬럼 인덱스 — 고장번호·보전 작업지시 번호·PM 계획번호 (10-13)
--
--  MntRepository 가 FAIL-/MWO-/PM-yyMM-nnn 을 저장 트랜잭션 안에서 그 달 범위를
--  UPDLOCK·HOLDLOCK 으로 잡고 채번한다(POP 안돈 고장번호 AndonRepository 도 같다).
--  인덱스가 없으면 그 범위 잠금이 테이블 전체(클러스터 스캔)를 잡아, 등록과 작업지시
--  완료(다음 PM 작업지시 채번)가 서로 기다리거나 교착할 수 있다. 인덱스가 있으면 그 달
--  번호 범위만 잡는다.
--
--  유니크가 아닌 일반 인덱스다(사용자 결정 10-13) — 중복은 저장소가 막고, 만약의 중복이
--  POP 안돈 보전 호출 트랜잭션을 실패시키지 않게 한다.
--
--  스키마 변경 없음(인덱스만) — 적용 순서 무관, 재실행 안전. 신 Web 보다 먼저 적용한다.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_mnt_number_index.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_FailureRegister_FailureNumber' AND object_id = OBJECT_ID('dbo.MNT_FailureRegister'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MNT_FailureRegister_FailureNumber ON dbo.MNT_FailureRegister (FailureNumber);
    PRINT 'IX_MNT_FailureRegister_FailureNumber created';
END
ELSE PRINT 'IX_MNT_FailureRegister_FailureNumber already exists';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_WorkOrder_WoNumber' AND object_id = OBJECT_ID('dbo.MNT_WorkOrder'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MNT_WorkOrder_WoNumber ON dbo.MNT_WorkOrder (WoNumber);
    PRINT 'IX_MNT_WorkOrder_WoNumber created';
END
ELSE PRINT 'IX_MNT_WorkOrder_WoNumber already exists';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_PMSchedule_PMPlanNumber' AND object_id = OBJECT_ID('dbo.MNT_PMSchedule'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_MNT_PMSchedule_PMPlanNumber ON dbo.MNT_PMSchedule (PMPlanNumber);
    PRINT 'IX_MNT_PMSchedule_PMPlanNumber created';
END
ELSE PRINT 'IX_MNT_PMSchedule_PMPlanNumber already exists';
GO

SELECT OBJECT_NAME(i.object_id) AS TableName, i.name AS IndexName, c.name AS ColumnName
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.name IN ('IX_MNT_FailureRegister_FailureNumber', 'IX_MNT_WorkOrder_WoNumber', 'IX_MNT_PMSchedule_PMPlanNumber')
ORDER  BY TableName;
GO
