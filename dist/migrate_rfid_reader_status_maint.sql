/* ------------------------------------------------------------------
   migrate_rfid_reader_status_maint.sql  (2026-10-28)

   공통코드 RFID_READER_STATUS 에 MAINT(점검중) 를 더한다 — MD-022 RFID 리더 화면의 「점검중」 KPI 는 MAINT 상태를 세는데
   그룹에 ONLINE·OFFLINE 만 있어 늘 0 이었다(MD 단위테스트 결과서 MD022-TC-003, 10-28 사용자 결정: 코드 추가).

   · 재실행 안전(없는 항목만 넣는다 — MD-030 에서 고친 값은 보존). 적용: sqlcmd -f 65001 -I -b
   · 순서 무관. 구 Web 도 그대로 동작한다(상태 콤보에 점검중이 하나 더 보일 뿐).
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'RFID_READER_STATUS')
    THROW 50001, 'RFID_READER_STATUS 그룹이 없습니다 — seed_md_code.sql 을 먼저 적용하세요.', 1;

MERGE dbo.MD_CodeItem AS t
USING (SELECT 'RFID_READER_STATUS_MAINT' AS CodeID, 'MAINT' AS CodeValue, N'점검중' AS CodeName, N'Maintenance' AS CodeNameEn, 30 AS SortOrder) AS s
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'RFID_READER_STATUS', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, 'system', SYSDATETIME());
GO

SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag
FROM   dbo.MD_CodeItem WHERE GroupCode = 'RFID_READER_STATUS' ORDER BY SortOrder;
