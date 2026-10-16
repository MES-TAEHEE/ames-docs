/* ------------------------------------------------------------------
   migrate_bom_status_code.sql  (2026-10-02)

   1) 공통코드 BOM_STATUS 신설 — MD_BomVersion.Status 의 값과 표시 이름(MD-004 상태 콤보·배지)
        DRAFT 초안 · PENDING 승인 대기 · APPROVED 승인 · REJECTED 반려 · EXPIRED 만료
   2) 종료일(EffTo)이 오늘(DB 날짜)보다 지난 APPROVED 버전을 EXPIRED 로 바꾼다.
      이후로는 MD-004 가 목록을 읽을 때마다 같은 규칙으로 만료 처리한다
      (MasterDataRepository.ExpireBomVersions — 감사 EXPIRE).
      MRP·코어 해석·BOP 상하위는 원래 기간(EffFrom/EffTo)으로 유효 버전을 고르므로 영향 없다.

   · 재실행 안전(없는 그룹·항목만 넣고, 만료 대상만 바꾼다). 적용: sqlcmd -f 65001 -I -b
   · 신 Web 없이 먼저 적용해도 안전 — 구 Web 은 EXPIRED 를 상태 필터에 없을 뿐 그대로 표시한다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- 1) 공통코드 그룹·항목 (없는 행만 넣는다 — MD-26 에서 고친 값은 보존)
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'BOM_STATUS' AS GroupCode, N'BOM 상태' AS GroupName, N'BOM status' AS GroupNameEn,
              N'MD_BomVersion.Status — DRAFT 초안·PENDING 승인 대기·APPROVED 승인·REJECTED 반려·EXPIRED 만료(종료일 경과)' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('BOM_STATUS_DRAFT',    'DRAFT',    N'초안',      N'Draft',    10),
        ('BOM_STATUS_PENDING',  'PENDING',  N'승인 대기', N'Pending',  20),
        ('BOM_STATUS_APPROVED', 'APPROVED', N'승인',      N'Approved', 30),
        ('BOM_STATUS_REJECTED', 'REJECTED', N'반려',      N'Rejected', 40),
        ('BOM_STATUS_EXPIRED',  'EXPIRED',  N'만료',      N'Expired',  50)
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'BOM_STATUS', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, 'system', SYSDATETIME());

-- 2) 이미 기간이 끝난 승인 버전 → EXPIRED
UPDATE dbo.MD_BomVersion
SET    Status = 'EXPIRED', ModifiedBy = 'system', ModifiedTS = SYSDATETIME()
WHERE  Status = 'APPROVED' AND EffTo < CAST(SYSDATETIME() AS date);

COMMIT;
GO

SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'BOM_STATUS' ORDER BY SortOrder;
SELECT ISNULL(Status, '(NULL)') AS Status, COUNT(*) AS Versions FROM dbo.MD_BomVersion GROUP BY Status;
GO
