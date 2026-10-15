/* ------------------------------------------------------------------
   migrate_mnt_status_code.sql  (2026-10-15)

   보전 작업지시 상태(MNT_WorkOrder.Status)를 공통코드 MNT_STATUS 로 관리한다(사용자 결정).
     ISSUED      발행     — 담당자 없음
     ASSIGNED    배정     — 담당자 지정, 착수 전 (신설)
     IN_PROGRESS 진행 중
     COMPLETED   완료     — 정상 종료(결과·조치·소요 시간)
     CLOSED      종료     — 작업 없이 닫음(사유 필수). 연결 고장도 CLOSED(해결 시각 없음),
                            PM 은 이번 회차를 건너뛰고 다음 예정일로 넘긴다
   MNT-007 상태 콤보·배지와 MNT-009 작업지시 현황이 이 그룹의 이름을 읽는다(코드는 MwoStatusCodes·MwoStatusRules).

   1) 공통코드 그룹·항목 — 없는 행만 넣는다(MD-26 에서 고친 이름은 보존)
   2) 기존 데이터 정리
        · OPEN(시연 데이터의 ISSUED 동의어)·NULL → 담당자가 있으면 ASSIGNED, 없으면 ISSUED
        · ISSUED 인데 담당자가 있으면 ASSIGNED
        · 완료 뒤 마감 용도로만 정의돼 있던 CLOSED 는 그대로(지금까지 만든 경로가 없어 0건)

   · 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 순서: 신 Web 과 함께 반영한다. 마이그레이션만 먼저 적용하면 구 Web 의 MNT-007 은 ASSIGNED 행을
     「발행」으로 세지 않고 착수 버튼을 숨긴다(배정·완료는 동작). 신 Web 만 먼저 올리면 OPEN 행이 남아
     상태 이름이 코드값으로 보이고 착수할 수 없다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- 1) 공통코드 그룹·항목
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'MNT_STATUS' AS GroupCode, N'보전 작업지시 상태' AS GroupName, N'Maintenance WO status' AS GroupNameEn,
              N'MNT_WorkOrder.Status — ISSUED 발행·ASSIGNED 배정·IN_PROGRESS 진행 중·COMPLETED 완료(정상 종료)·CLOSED 종료(작업 없이 닫음)' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('MNT_STATUS_ISSUED',      'ISSUED',      N'발행',    N'Issued',      10, N'발행됨, 담당자 없음'),
        ('MNT_STATUS_ASSIGNED',    'ASSIGNED',    N'배정',    N'Assigned',    20, N'담당자 지정, 착수 전'),
        ('MNT_STATUS_IN_PROGRESS', 'IN_PROGRESS', N'진행 중', N'In progress', 30, N'착수함'),
        ('MNT_STATUS_COMPLETED',   'COMPLETED',   N'완료',    N'Completed',   40, N'정상 종료 — 결과·조치·소요 시간 기록'),
        ('MNT_STATUS_CLOSED',      'CLOSED',      N'종료',    N'Closed',      50, N'작업 없이 닫음 — 사유 필수, 연결 고장 종료·PM 회차 건너뜀')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'MNT_STATUS', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());

-- 2) 기존 데이터
UPDATE dbo.MNT_WorkOrder
SET    Status = CASE WHEN NULLIF(AssignedTechID, N'') IS NULL THEN 'ISSUED' ELSE 'ASSIGNED' END,
       ModifiedBy = 'system', ModifiedTS = SYSDATETIME()
WHERE  Status IS NULL OR Status = 'OPEN'
   OR (Status = 'ISSUED' AND NULLIF(AssignedTechID, N'') IS NOT NULL);
PRINT CONCAT('MNT_WorkOrder 상태 정리: ', @@ROWCOUNT, '행');

COMMIT;
GO

SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'MNT_STATUS' ORDER BY SortOrder;
SELECT ISNULL(Status, '(NULL)') AS Status, COUNT(*) AS WorkOrders FROM dbo.MNT_WorkOrder GROUP BY Status ORDER BY Status;
GO
