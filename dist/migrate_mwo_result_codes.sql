/* ------------------------------------------------------------------
   migrate_mwo_result_codes.sql
   공통코드 그룹 MWO_RESULT(정비 작업지시 완료 결과) 신설.
     OK        정상 완료   — 조치 완료, 후속 없음
     PARTIAL   부분 완료   — 임시 조치, 추가 작업 필요
     FOLLOWUP  후속 조치 필요 — 부품 대기·외주 등
   MNT-007 완료 모달·MNT-005/010 PM 완료 모달의 결과 콤보가 읽고,
   값은 MNT_WorkOrder.ChecklistResultsJSON(결과 JSON)·MNT_PMExecution.Result 에 저장된다.
   가드형: 그룹·아이템이 이미 있으면 건너뜀. 반복 실행 안전. 순서 무관.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'MWO_RESULT')
BEGIN
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('MWO_RESULT', N'정비 작업지시 완료 결과', N'Maintenance WO result', N'MNT_PMExecution.Result / MNT_WorkOrder 완료 결과', 1, 'admin@ames.local');
    PRINT 'MD_CodeGroup: MWO_RESULT 추가';
END
ELSE
    PRINT 'MD_CodeGroup: MWO_RESULT 이미 존재';

DECLARE @items TABLE (CodeValue VARCHAR(10), NameKo NVARCHAR(50), NameEn NVARCHAR(50), SortOrder INT, Descr NVARCHAR(200));
INSERT @items VALUES
    ('OK',       N'정상 완료',      N'Completed', 10, N'조치 완료, 후속 없음'),
    ('PARTIAL',  N'부분 완료',      N'Partial',   20, N'임시 조치, 추가 작업 필요'),
    ('FOLLOWUP', N'후속 조치 필요', N'Follow-up', 30, N'부품 대기·외주 등 후속 필요');

INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
SELECT 'MWO_RESULT_' + i.CodeValue, 'MWO_RESULT', i.CodeValue, i.NameKo, i.NameEn, NULL, i.SortOrder, NULL, 1, i.Descr, 'admin@ames.local'
FROM   @items i
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.GroupCode = 'MWO_RESULT' AND c.CodeValue = i.CodeValue);
PRINT CONCAT('MD_CodeItem 추가 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'MWO_RESULT' ORDER BY SortOrder;
