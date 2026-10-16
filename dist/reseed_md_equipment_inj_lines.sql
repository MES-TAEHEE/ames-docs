/* ------------------------------------------------------------------
   reseed_md_equipment_inj_lines.sql — 설비 마스터(MD_Equipment)를 사출 라인마다 1대로 다시 만든다 (10-16 사용자 결정)
   ① 새 설비 9대: INJ-{라인 2자리}-01-{톤수}, 이름 「사출 n라인 1호기 (톤수T)」, 유형 INJ, WC-INJ, 톤수는 라인 이름의 값
   ② 기존 설비는 전부 지운다. 참조 행은
      - 사출(INJ) 라인의 옛 사출기 → 그 라인의 새 설비 ID 로 옮긴다(MNT·생산 기록 모두).
        같은 라인에 옛 사출기가 여럿이면 EquipID 순 첫 설비의 설비 카드만 옮기고, 나머지 설비의 카드와
        OEE 로그(같은 날짜가 겹쳐 이중 계상)는 지운다.
      - 그 밖의 옛 설비(감싸기·도장 등) → MNT 쪽(설비 카드·OEE 로그·PM 일정·실행 이력·PP-LSB PM 슬롯·
        보전 작업지시·점검 항목·고장·조치 이력)만 지우고, 생산 기록(안돈·상태 로그·LOT·금형 교체 등)은 옛 ID 그대로 둔다.
        고장이 연 비가동(PP_LineDowntimeLog)은 남기고 고장 연결만 끊는다. 예비품 출고 이력(MNT_SparePartsTxn)은 그대로.
   적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P … -f 65001 -I -b -v Mode=ROLLBACK -i dist\reseed_md_equipment_inj_lines.sql   (미리보기)
         sqlcmd … -v Mode=COMMIT -i dist\reseed_md_equipment_inj_lines.sql   (적용)
   재실행 안전(두 번째 실행은 옮기거나 지울 옛 설비가 없다). 사출 라인 마스터(MD_Line)가 없으면 중단.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50001, N'Mode 는 ROLLBACK 또는 COMMIT', 1;

DECLARE @By varchar(20) = 'EQUIP-RESEED-1016';

DECLARE @New TABLE (EquipID varchar(20) PRIMARY KEY, LineID varchar(20) NOT NULL UNIQUE, Tonnage int NOT NULL, EquipName nvarchar(100) NOT NULL);
INSERT @New (LineID, Tonnage, EquipID, EquipName)
SELECT v.LineID, v.Tonnage,
       CONCAT('INJ-', RIGHT(v.LineID, 2), '-01-', v.Tonnage),
       CONCAT(N'사출 ', CAST(RIGHT(v.LineID, 2) AS int), N'라인 1호기 (', v.Tonnage, N'T)')
FROM (VALUES ('LINE-INJ-01', 1800), ('LINE-INJ-02', 1800), ('LINE-INJ-03', 1800),
             ('LINE-INJ-04', 1400), ('LINE-INJ-05',  850), ('LINE-INJ-06',  650),
             ('LINE-INJ-07',  650), ('LINE-INJ-08',  450), ('LINE-INJ-09',  450)) v(LineID, Tonnage);

IF EXISTS (SELECT 1 FROM @New n WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Line l WHERE l.LineID = n.LineID))
    THROW 50002, N'MD_Line 에 없는 사출 라인이 있다 — 라인 마스터부터 맞출 것', 1;

BEGIN TRAN;

-- 옛 설비 → 새 ID (INJ 라인의 사출기만, 나머지는 NewID NULL = MNT 삭제 대상)
DECLARE @Map TABLE (OldID varchar(20) PRIMARY KEY, NewID varchar(20) NULL, IsPrimary bit NOT NULL);
INSERT @Map (OldID, NewID, IsPrimary)
SELECT e.EquipID, n.EquipID,
       CASE WHEN n.EquipID IS NOT NULL AND ROW_NUMBER() OVER (PARTITION BY n.EquipID ORDER BY e.EquipID) = 1 THEN 1 ELSE 0 END
FROM   dbo.MD_Equipment e
LEFT JOIN @New n ON n.LineID = e.LineID AND e.EquipType = 'INJ'
WHERE  e.EquipID NOT IN (SELECT EquipID FROM @New);

SELECT m.OldID, e.EquipType, e.LineID, ISNULL(m.NewID, N'(MNT 삭제)') AS NewID, m.IsPrimary
FROM   @Map m JOIN dbo.MD_Equipment e ON e.EquipID = m.OldID ORDER BY m.NewID, m.OldID;

/* ── A. 옮기지 않는 옛 설비의 MNT 데이터 삭제 ───────────────────────────── */
DECLARE @Drop TABLE (EquipID varchar(20) PRIMARY KEY);
INSERT @Drop SELECT OldID FROM @Map WHERE NewID IS NULL;

CREATE TABLE #Wo (WorkOrderID int PRIMARY KEY);
INSERT #Wo SELECT WorkOrderID FROM dbo.MNT_WorkOrder WHERE EquipID IN (SELECT EquipID FROM @Drop);
CREATE TABLE #Fail (FailureID int PRIMARY KEY);
INSERT #Fail SELECT FailureID FROM dbo.MNT_FailureRegister WHERE EquipID IN (SELECT EquipID FROM @Drop);
DECLARE @Pm TABLE (PMScheduleID int PRIMARY KEY);
INSERT @Pm SELECT PMScheduleID FROM dbo.MNT_PMSchedule WHERE EquipID IN (SELECT EquipID FROM @Drop);

-- 고장 ↔ 비가동 연결 컬럼(migrate_mnt_failure_downtime.sql)이 없는 DB 도 돌게 동적 SQL
IF COL_LENGTH('dbo.PP_LineDowntimeLog', 'FailureID') IS NOT NULL
BEGIN
    EXEC (N'UPDATE dbo.PP_LineDowntimeLog SET FailureID = NULL WHERE FailureID IN (SELECT FailureID FROM #Fail); PRINT CONCAT(N''A 비가동 고장 연결 해제: '', @@ROWCOUNT);');
END
DELETE dbo.MNT_FailureAction   WHERE FailureID IN (SELECT FailureID FROM #Fail);
PRINT CONCAT(N'A 고장 조치 이력 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_FailureRegister WHERE FailureID IN (SELECT FailureID FROM #Fail);
PRINT CONCAT(N'A 고장 삭제: ', @@ROWCOUNT);

DELETE dbo.PP_LineSchedule WHERE EntryType = 'PM' AND RefType = 'PMSCH'
   AND RefID IN (SELECT CAST(PMScheduleID AS varchar(20)) FROM @Pm);
PRINT CONCAT(N'A PP-LSB PM 슬롯 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_PMExecution WHERE PMScheduleID IN (SELECT PMScheduleID FROM @Pm) OR EquipID IN (SELECT EquipID FROM @Drop);
PRINT CONCAT(N'A PM 실행 이력 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_PMSchedule  WHERE PMScheduleID IN (SELECT PMScheduleID FROM @Pm);
PRINT CONCAT(N'A PM 일정 삭제: ', @@ROWCOUNT);

UPDATE dbo.MNT_FailureRegister SET WorkOrderID = NULL WHERE WorkOrderID IN (SELECT WorkOrderID FROM #Wo);
UPDATE dbo.MNT_PMSchedule      SET ActiveWoID  = NULL WHERE ActiveWoID  IN (SELECT WorkOrderID FROM #Wo);
IF COL_LENGTH('dbo.MNT_FailureAction', 'WorkOrderID') IS NOT NULL
    EXEC (N'UPDATE dbo.MNT_FailureAction SET WorkOrderID = NULL WHERE WorkOrderID IN (SELECT WorkOrderID FROM #Wo);');
DELETE dbo.MNT_WorkOrderTask WHERE WorkOrderID IN (SELECT WorkOrderID FROM #Wo);
PRINT CONCAT(N'A 작업지시 점검 항목 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_WorkOrder     WHERE WorkOrderID IN (SELECT WorkOrderID FROM #Wo);
PRINT CONCAT(N'A 보전 작업지시 삭제: ', @@ROWCOUNT);

DELETE dbo.MNT_OEELog          WHERE EquipID IN (SELECT EquipID FROM @Drop);
PRINT CONCAT(N'A OEE 로그 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_EquipmentStatus WHERE EquipID IN (SELECT EquipID FROM @Drop);
PRINT CONCAT(N'A 설비 카드 삭제: ', @@ROWCOUNT);
UPDATE dbo.MNT_MoldShotCount SET MountedEquipID = NULL WHERE MountedEquipID IN (SELECT EquipID FROM @Drop);
PRINT CONCAT(N'A 금형 장착 설비 비움: ', @@ROWCOUNT);

/* ── B. 사출 라인 옛 사출기 → 새 ID ─────────────────────────────────────── */
-- 같은 라인의 두 번째 이후 설비: 카드·OEE 로그는 합치지 않고 지운다
DELETE dbo.MNT_EquipmentStatus WHERE EquipID IN (SELECT OldID FROM @Map WHERE NewID IS NOT NULL AND IsPrimary = 0);
PRINT CONCAT(N'B 중복 설비 카드 삭제: ', @@ROWCOUNT);
DELETE dbo.MNT_OEELog          WHERE EquipID IN (SELECT OldID FROM @Map WHERE NewID IS NOT NULL AND IsPrimary = 0);
PRINT CONCAT(N'B 중복 설비 OEE 로그 삭제: ', @@ROWCOUNT);

UPDATE x SET EquipID = m.NewID, LineID = n.LineID FROM dbo.MNT_EquipmentStatus x JOIN @Map m ON m.OldID = x.EquipID JOIN @New n ON n.EquipID = m.NewID;
PRINT CONCAT(N'B 설비 카드: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.MNT_OEELog          x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B OEE 로그: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.MNT_FailureRegister x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 고장: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.MNT_WorkOrder       x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 보전 작업지시: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.MNT_PMSchedule      x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B PM 일정: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.MNT_PMExecution     x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B PM 실행 이력: ', @@ROWCOUNT);
UPDATE x SET MountedEquipID = m.NewID FROM dbo.MNT_MoldShotCount x JOIN @Map m ON m.OldID = x.MountedEquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 금형 장착 설비: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_AndonCall        x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 안돈: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_EquipStatusLog   x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 설비 상태 로그: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_InjLot           x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 사출 LOT: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_ImgLot           x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B IMG LOT: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_MoldChange       x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 금형 교체: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_PlcInterlock     x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B PLC 인터록: ', @@ROWCOUNT);
UPDATE x SET EquipID = m.NewID FROM dbo.PR_RobotInspection  x JOIN @Map m ON m.OldID = x.EquipID WHERE m.NewID IS NOT NULL;
PRINT CONCAT(N'B 로봇 검사: ', @@ROWCOUNT);

/* ── C. 설비 마스터 교체 ───────────────────────────────────────────────── */
DELETE dbo.MD_Equipment WHERE EquipID IN (SELECT OldID FROM @Map);
PRINT CONCAT(N'C 옛 설비 삭제: ', @@ROWCOUNT);

INSERT dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, WCID, TargetOEE, Tonnage, Status, ActiveFlag, CreatedBy, CreatedTS)
SELECT n.EquipID, n.EquipName, 'INJ', n.LineID, 'WC-INJ', 85.00, n.Tonnage, 'RUN', 1, @By, SYSDATETIME()
FROM   @New n WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Equipment e WHERE e.EquipID = n.EquipID);
PRINT CONCAT(N'C 새 설비 등록: ', @@ROWCOUNT);

SELECT EquipID, EquipName, EquipType, LineID, WCID, Tonnage, Status FROM dbo.MD_Equipment ORDER BY LineID, EquipID;

IF '$(Mode)' = 'COMMIT' BEGIN COMMIT; PRINT N'적용(COMMIT)'; END
ELSE BEGIN ROLLBACK; PRINT N'미리보기(ROLLBACK) — 바뀐 것 없음'; END
DROP TABLE IF EXISTS #Wo; DROP TABLE IF EXISTS #Fail;
GO
