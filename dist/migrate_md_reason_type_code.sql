/* ------------------------------------------------------------------
   migrate_md_reason_type_code.sql  (2026-11-13)

   사유 코드(MD_ReasonCode.ReasonType)의 유형을 공통코드 REASON_TYPE 으로 관리한다(사용자 결정 — 하드코딩 금지).
   MD-025 등록·수정 창의 「사유 유형」은 이 그룹의 사용 중인 항목에서 고른다.
     DOWNTIME 비가동 — PP-DTL 비가동 등록의 원인 콤보는 이 유형만 불러온다
     DEFECT   불량
     SCRAP    폐기
   그룹은 CodeGroupsUsedByCode 에 있어 MD-030 에서 삭제할 수 없다. 항목은 MD-030 에서 추가·사용 중지할 수 있다.

   · 없는 행만 넣는다(MD-030 에서 고친 이름·사용 여부는 보존). 재실행 안전. 적용: sqlcmd -f 65001 -I -b
   · 순서 무관 — 마이그레이션 없이 신 Web 을 올리면 사유 유형 콤보가 비어 있을 뿐 예외는 없다(옛 값은 그대로 보인다).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'REASON_TYPE' AS GroupCode, N'사유 유형' AS GroupName, N'Reason type' AS GroupNameEn,
              N'MD_ReasonCode.ReasonType — MD-025 사유 코드의 유형. DOWNTIME 은 PP-DTL 비가동 원인 콤보가 읽는다' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('REASON_TYPE_DOWNTIME', 'DOWNTIME', N'비가동', N'Downtime', 10, N'PP-DTL 비가동 등록의 원인으로 쓰이는 사유'),
        ('REASON_TYPE_DEFECT', 'DEFECT', N'불량', N'Defect', 20, N'불량 사유'),
        ('REASON_TYPE_SCRAP', 'SCRAP', N'폐기', N'Scrap', 30, N'폐기 사유')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'REASON_TYPE', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());
PRINT CONCAT('REASON_TYPE 항목 추가: ', @@ROWCOUNT, '행');

COMMIT;
GO

SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag FROM dbo.MD_CodeItem WHERE GroupCode = 'REASON_TYPE' ORDER BY SortOrder;
SELECT ISNULL(ReasonType, '(NULL)') AS ReasonType, COUNT(*) AS Rows FROM dbo.MD_ReasonCode GROUP BY ReasonType ORDER BY ReasonType;
GO
