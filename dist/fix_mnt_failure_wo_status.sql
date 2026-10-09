/* ------------------------------------------------------------------
   fix_mnt_failure_wo_status.sql — 고장(MNT_FailureRegister)과 보전 작업지시(MNT_WorkOrder) 상태 어긋남 정리 (10-10)
   MNT-002 등록·수정 창에서 상태·해결일시를 직접 고칠 수 있던 때 생긴 어긋남을 맞춘다. 10-10 부터 상태·해결일시는
   「수리 완료」(MNT-002)·작업지시 「완료」(MNT-007)·안돈 종료로만 바뀐다.
     A. 고장은 미해결인데 연결 WO 가 완료·종결 → 고장을 RESOLVED, 해결일시 = WO 완료(종결) 시각
     B. 고장은 해결인데 연결 WO 가 아직 열림 → 고치지 않고 목록만 보인다(원인·조치 없이 WO 를 닫을 수 없다 — MNT-007 에서 완료)
     C. 고장은 미해결인데 해결일시가 있음 → 해결일시를 비운다
     D. 고장은 해결인데 해결일시가 없음 → WO 완료 시각, 없으면 마지막 수정 시각
   적용: sqlcmd -S <서버> -d AMES_DEV -f 65001 -I -b -v Mode=ROLLBACK -i dist\fix_mnt_failure_wo_status.sql   (미리보기)
         sqlcmd -S <서버> -d AMES_DEV -f 65001 -I -b -v Mode=COMMIT   -i dist\fix_mnt_failure_wo_status.sql   (적용)
   재실행 안전(맞춰진 행은 다시 걸리지 않는다).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50001, N'Mode 는 ROLLBACK 또는 COMMIT', 1;

BEGIN TRAN;

SELECT N'A 미해결+WO 완료' AS Kind, f.FailureNumber, f.Status, w.WoNumber, w.Status AS WoStatus, COALESCE(w.CompletedAt, w.ClosedAt) AS WoDoneAt
FROM   dbo.MNT_FailureRegister f JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
WHERE  ISNULL(f.Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED') AND w.Status IN ('COMPLETED', 'CLOSED');

UPDATE f
SET    Status = 'RESOLVED', ResolvedAt = COALESCE(f.ResolvedAt, w.CompletedAt, w.ClosedAt, SYSDATETIME()),
       ModifiedBy = 'FAIL-FIX-1010', ModifiedTS = SYSDATETIME()
FROM   dbo.MNT_FailureRegister f JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
WHERE  ISNULL(f.Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED') AND w.Status IN ('COMPLETED', 'CLOSED');
PRINT CONCAT(N'A 고장 해결로 맞춤: ', @@ROWCOUNT);

SELECT N'B 해결+WO 열림(수동 처리)' AS Kind, f.FailureNumber, f.Status, w.WoNumber, w.Status AS WoStatus
FROM   dbo.MNT_FailureRegister f JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
WHERE  f.Status IN ('RESOLVED', 'CLOSED') AND w.Status NOT IN ('COMPLETED', 'CLOSED', 'CANCELLED');

UPDATE dbo.MNT_FailureRegister
SET    ResolvedAt = NULL, ModifiedBy = 'FAIL-FIX-1010', ModifiedTS = SYSDATETIME()
WHERE  ISNULL(Status, 'OPEN') NOT IN ('RESOLVED', 'CLOSED') AND ResolvedAt IS NOT NULL;
PRINT CONCAT(N'C 미해결 고장의 해결일시 비움: ', @@ROWCOUNT);

UPDATE f
SET    ResolvedAt = COALESCE(w.CompletedAt, w.ClosedAt, f.ModifiedTS, f.CreatedTS),
       ModifiedBy = 'FAIL-FIX-1010', ModifiedTS = SYSDATETIME()
FROM   dbo.MNT_FailureRegister f LEFT JOIN dbo.MNT_WorkOrder w ON w.WorkOrderID = f.WorkOrderID
WHERE  f.Status IN ('RESOLVED', 'CLOSED') AND f.ResolvedAt IS NULL;
PRINT CONCAT(N'D 해결 고장의 해결일시 채움: ', @@ROWCOUNT);

IF '$(Mode)' = 'COMMIT' BEGIN COMMIT; PRINT N'적용(COMMIT)'; END
ELSE BEGIN ROLLBACK; PRINT N'미리보기(ROLLBACK) — 바뀐 것 없음'; END
GO
