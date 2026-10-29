/* ------------------------------------------------------------------
   migrate_pp_downtime_reason_code.sql  (2026-10-29)

   비가동 사유(PP_LineDowntimeLog.ReasonCode)를 공통코드 DOWNTIME_REASON 으로 관리한다(사용자 결정 10-29).
   전에는 PP-DTL 사유 콤보가 이력에 쓰인 값(DISTINCT)이라 이력이 없는 DB 에서는 고를 사유가 없어
   웹 비가동 등록이 되지 않았다(PP 단위테스트 PPDTL-TC-012).
     MACHINE    설비 고장        — 보전 고장(MNT-002) 「라인 정지」가 여는 행도 이 코드(FailureDowntimeRules.ReasonCode)
     CHANGEOVER 금형·품번 교체
     MATERIAL   자재 대기
     QUALITY    품질 문제
     MANPOWER   인원 부족
     PLANNED    계획 정지
     OTHER      기타
     ANDON      안돈              — POP 안돈이 여는 행. 웹 등록·수정 콤보에서는 빠진다
   그룹은 CodeGroupsUsedByCode 에 있어 MD-030 에서 삭제할 수 없다. 이력에만 있는 옛 사유 값은
   PP-DTL 필터에 그대로 보이고, 그 행을 고칠 때만 수정 콤보에 남는다.

   · 없는 행만 넣는다(MD-030 에서 고친 이름·사용 여부는 보존). 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 순서 무관 — 마이그레이션 없이 신 Web 을 올리면 사유 콤보가 비어 웹 등록이 막힐 뿐(전과 같음) 예외는 없다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'DOWNTIME_REASON' AS GroupCode, N'비가동 사유' AS GroupName, N'Downtime reason' AS GroupNameEn,
              N'PP_LineDowntimeLog.ReasonCode — PP-DTL 비가동 등록·수정 사유. ANDON 은 POP 안돈 전용, MACHINE 은 보전 고장 라인 정지도 씀' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('DOWNTIME_REASON_MACHINE', 'MACHINE', N'설비 고장', N'Machine failure', 10, N'설비 고장으로 라인 정지 — 보전 고장(MNT-002) 「라인 정지」가 여는 행도 이 코드'),
        ('DOWNTIME_REASON_CHANGEOVER', 'CHANGEOVER', N'금형·품번 교체', N'Changeover', 20, N'금형 교체·품번 전환'),
        ('DOWNTIME_REASON_MATERIAL', 'MATERIAL', N'자재 대기', N'Material shortage', 30, N'자재·부품 대기'),
        ('DOWNTIME_REASON_QUALITY', 'QUALITY', N'품질 문제', N'Quality issue', 40, N'품질 이상으로 정지'),
        ('DOWNTIME_REASON_MANPOWER', 'MANPOWER', N'인원 부족', N'Manpower shortage', 50, N'작업자 부족'),
        ('DOWNTIME_REASON_PLANNED', 'PLANNED', N'계획 정지', N'Planned stop', 60, N'계획된 정지(점검·회의 등)'),
        ('DOWNTIME_REASON_OTHER', 'OTHER', N'기타', N'Other', 90, N'그 밖의 사유 — 비고에 내용'),
        ('DOWNTIME_REASON_ANDON', 'ANDON', N'안돈', N'Andon (POP)', 100, N'POP 안돈이 여는 행 — 웹 등록·수정에서는 고르지 않음')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'DOWNTIME_REASON', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());
PRINT CONCAT('DOWNTIME_REASON 항목 추가: ', @@ROWCOUNT, '행');

COMMIT;
GO

SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag FROM dbo.MD_CodeItem WHERE GroupCode = 'DOWNTIME_REASON' ORDER BY SortOrder;
SELECT ISNULL(ReasonCode, '(NULL)') AS ReasonCode, COUNT(*) AS Rows FROM dbo.PP_LineDowntimeLog GROUP BY ReasonCode ORDER BY ReasonCode;
GO
