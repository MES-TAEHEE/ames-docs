-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_failure_codes.sql
--  고장 등록(MNT-002) 등록 모달용 공통코드: FAILURE_TYPE · FAILURE_SOURCE
--
--  MNT_FailureRegister.FailureType / Source 는 지금까지 고정 문자열이었다. 화면에서 등록하려면
--  콤보로 골라야 하므로 기존 데이터 값 그대로 공통코드에 올린다. WEB 은 사무실 포탈에서 직접
--  등록한 건의 출처다.
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_mnt_failure_codes.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'FAILURE_TYPE')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('FAILURE_TYPE', N'고장 유형', N'Failure type', N'MNT_FailureRegister.FailureType', 1, 'admin@ames.local');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'FAILURE_SOURCE')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('FAILURE_SOURCE', N'고장 출처', N'Failure source', N'MNT_FailureRegister.Source', 1, 'admin@ames.local');

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
  ('FAILURE_TYPE_MECHANICAL',  'FAILURE_TYPE',   'MECHANICAL', N'기계',        N'Mechanical',  10),
  ('FAILURE_TYPE_ELECTRICAL',  'FAILURE_TYPE',   'ELECTRICAL', N'전기',        N'Electrical',  20),
  ('FAILURE_TYPE_PROCESS',     'FAILURE_TYPE',   'PROCESS',    N'공정',        N'Process',     30),
  ('FAILURE_TYPE_TOOLING',     'FAILURE_TYPE',   'TOOLING',    N'금형·치공구', N'Tooling',     40),
  ('FAILURE_TYPE_QUALITY',     'FAILURE_TYPE',   'QUALITY',    N'품질',        N'Quality',     50),
  ('FAILURE_TYPE_EQUIP',       'FAILURE_TYPE',   'EQUIP',      N'설비(안돈)',  N'Equipment (andon)', 60),
  ('FAILURE_SOURCE_OPERATOR',  'FAILURE_SOURCE', 'OPERATOR',   N'작업자',      N'Operator',    10),
  ('FAILURE_SOURCE_PLC',       'FAILURE_SOURCE', 'PLC',        N'PLC',         N'PLC',         20),
  ('FAILURE_SOURCE_ANDON',     'FAILURE_SOURCE', 'ANDON',      N'안돈',        N'Andon',       30),
  ('FAILURE_SOURCE_QC',        'FAILURE_SOURCE', 'QC',         N'품질검사',    N'QC',          40),
  ('FAILURE_SOURCE_WEB',       'FAILURE_SOURCE', 'WEB',        N'사무실 등록', N'Office web',  50)
) AS src (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, NULL, src.SortOrder, NULL, 1, NULL, 'admin@ames.local');
PRINT CONCAT(N'✓ MD_CodeItem FAILURE_TYPE/FAILURE_SOURCE: ', @@ROWCOUNT, N'행');
GO

SELECT GroupCode, CodeValue, CodeName FROM dbo.MD_CodeItem WHERE GroupCode IN ('FAILURE_TYPE','FAILURE_SOURCE') ORDER BY GroupCode, SortOrder;
GO
