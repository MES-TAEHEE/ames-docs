-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_aps_no_foreign_keys.sql
--  APS·일별 구매계획 테이블의 외래키 6개 삭제 + Worker 설정 그룹 이름의 옛 접두어 정리 (10-28)
--
--  FK 를 두지 않는 원칙(10-08 — FK 위반이 현장 저장을 막지 않게 정합성은 리포지토리가 책임진다)과 달리
--  migrate_aps.sql·migrate_aps_plan_line_shift.sql·migrate_demand_plan.sql 이 FK 를 만들었다. 새 DB 는 그 스크립트가
--  더는 만들지 않고, 이미 만든 DB 는 여기서 지운다.
--    FK_MD_ApsLineStage_Line·_Pattern — 라인·패턴 삭제는 MasterDataRepository 사용처 검사(APSLINE)가 막는다
--    FK_PP_ApsPlanLine_Run·FK_PP_ApsRunWo_Run·FK_PP_ApsPlanLineShift_Line(ON DELETE CASCADE) — 앱은 실행·계획 행을 지우지 않는다
--    FK_PP_DemandPlan_Batch — 배치 헤더와 행은 PpRepository.ReplaceDemandPlan 이 한 트랜잭션으로 쓴다
--  Worker 설정 그룹 이름(MD_CodeGroup.GroupName·GroupNameEn)의 폐지된 'ScheduledWorker · ' 접두어도 뗀다(10-05 규칙).
--  순서 무관·재실행 안전, 구·신 바이너리 모두 그대로 동작(코드는 FK·그룹 이름을 읽지 않는다).
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pp_aps_no_foreign_keys.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @fk sysname, @tbl nvarchar(300), @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT fk.name, QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name)
    FROM   sys.foreign_keys fk JOIN sys.tables t ON t.object_id = fk.parent_object_id
    WHERE  fk.name IN (N'FK_MD_ApsLineStage_Line', N'FK_MD_ApsLineStage_Pattern',
                       N'FK_PP_ApsPlanLine_Run', N'FK_PP_ApsRunWo_Run', N'FK_PP_ApsPlanLineShift_Line',
                       N'FK_PP_DemandPlan_Batch');
OPEN c;
FETCH NEXT FROM c INTO @fk, @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER TABLE ' + @tbl + N' DROP CONSTRAINT ' + QUOTENAME(@fk) + N';';
    EXEC (@sql);
    PRINT N'✓ ' + @fk + N' 삭제';
    FETCH NEXT FROM c INTO @fk, @tbl;
END
CLOSE c; DEALLOCATE c;
GO

UPDATE dbo.MD_CodeGroup
SET    GroupName   = CASE WHEN GroupName   LIKE N'ScheduledWorker · %' THEN SUBSTRING(GroupName,   DATALENGTH(N'ScheduledWorker · ') / 2 + 1, 200) ELSE GroupName   END,
       GroupNameEn = CASE WHEN GroupNameEn LIKE N'ScheduledWorker · %' THEN SUBSTRING(GroupNameEn, DATALENGTH(N'ScheduledWorker · ') / 2 + 1, 200) ELSE GroupNameEn END,
       ModifiedBy  = 'migrate',
       ModifiedTS  = SYSDATETIME()
WHERE  GroupCode LIKE 'SW[_]%'
  AND  (GroupName LIKE N'ScheduledWorker · %' OR GroupNameEn LIKE N'ScheduledWorker · %');
PRINT CONCAT(N'Worker 설정 그룹 이름 정리: ', @@ROWCOUNT, N'건');
GO
