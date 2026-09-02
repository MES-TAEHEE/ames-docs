-- ============================================================
-- SYS-001 사용자 관리 코드 그룹 시드 데이터
-- 실행 순서: 1) CodeGroup 등록  2) CodeItem 등록  3) DefaultShift 값 변환
-- ============================================================
USE AMES_DEV;
GO

-- ── 1. MD_CodeGroup 등록 ─────────────────────────────────────
-- 이미 존재하면 스킵 (MERGE 사용)
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('Department',       N'부서',         N'Department',    N'사용자 부서 코드'),
    ('Plant',            N'공장',         N'Plant',         N'공장/사업장 코드'),
    ('WORK_SHIFT',       N'작업교대',     N'Work Shift',    N'A/B/C 교대 코드'),
    ('USER_STATUS',      N'사용자 상태',  N'User Status',   N'SYS_UserProfile.AccountStatus 코드'),
    ('PERMISSION_LEVEL', N'권한 수준',    N'Permission Level', N'SYS_RolePermission.PermissionLevel 코드')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'system', SYSDATETIME());
GO

-- ── 2. Department — SYS_UserProfile DISTINCT 값으로 등록 ─────
INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, UseFlag, SortOrder, CreatedBy, CreatedTS)
SELECT
    'DEPT_' + CAST(ROW_NUMBER() OVER (ORDER BY Department) AS VARCHAR(3)),
    'Department',
    Department,
    Department,
    1,
    ROW_NUMBER() OVER (ORDER BY Department),
    'system',
    SYSDATETIME()
FROM (
    SELECT DISTINCT LTRIM(RTRIM(Department)) AS Department
    FROM   dbo.SYS_UserProfile
    WHERE  Department IS NOT NULL AND LTRIM(RTRIM(Department)) <> ''
) d
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MD_CodeItem ci
    WHERE  ci.GroupCode = 'Department' AND ci.CodeValue = d.Department
);
GO

-- ── 3. Plant — SYS_UserProfile DISTINCT 값으로 등록 ─────────
INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, UseFlag, SortOrder, CreatedBy, CreatedTS)
SELECT
    'PLANT_' + CAST(ROW_NUMBER() OVER (ORDER BY Plant) AS VARCHAR(3)),
    'Plant',
    Plant,
    Plant,
    1,
    ROW_NUMBER() OVER (ORDER BY Plant),
    'system',
    SYSDATETIME()
FROM (
    SELECT DISTINCT LTRIM(RTRIM(PlantCode)) AS Plant
    FROM   dbo.SYS_UserProfile
    WHERE  PlantCode IS NOT NULL AND LTRIM(RTRIM(PlantCode)) <> ''
) p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MD_CodeItem ci
    WHERE  ci.GroupCode = 'Plant' AND ci.CodeValue = p.Plant
);
GO

-- ── 4. WorkShift — A / B / C 고정 등록 ──────────────────────
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('WORK_SHIFT_A', 'WORK_SHIFT', 'A', N'A교대', 1),
    ('WORK_SHIFT_B', 'WORK_SHIFT', 'B', N'B교대', 2),
    ('WORK_SHIFT_C', 'WORK_SHIFT', 'C', N'C교대', 3)
) AS src(CodeID, GroupCode, CodeValue, CodeName, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, UseFlag, SortOrder, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, 1, src.SortOrder, 'system', SYSDATETIME());
GO

-- ── 5. SYS_UserProfile.DefaultShift 값 변환 ─────────────────
-- DAY → A, NIGHT → B (이미 A/B/C인 경우는 영향 없음)
UPDATE dbo.SYS_UserProfile
SET    DefaultShift = 'A',
       ModifiedBy   = 'system',
       ModifiedTS   = SYSDATETIME()
WHERE  DefaultShift = 'DAY';

UPDATE dbo.SYS_UserProfile
SET    DefaultShift = 'B',
       ModifiedBy   = 'system',
       ModifiedTS   = SYSDATETIME()
WHERE  DefaultShift = 'NIGHT';
GO

-- ── 5. USER_STATUS — 계정 상태 고정 등록 ────────────────────
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('USER_STATUS_Active',   'USER_STATUS', 'Active',   N'활성',   N'Active',   1),
    ('USER_STATUS_LOCKED',   'USER_STATUS', 'LOCKED',   N'잠김',   N'Locked',   2),
    ('USER_STATUS_INACTIVE', 'USER_STATUS', 'INACTIVE', N'비활성', N'Inactive', 3)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, UseFlag, SortOrder, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, 1, src.SortOrder, 'system', SYSDATETIME());
GO

-- ── 6. PERMISSION_LEVEL — 권한 수준 고정 등록 ───────────────
MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('PERMISSION_LEVEL_FULL', 'PERMISSION_LEVEL', 'FULL', N'전체권한', N'Full Access', 1),
    ('PERMISSION_LEVEL_EDIT', 'PERMISSION_LEVEL', 'EDIT', N'편집',     N'Edit',        2),
    ('PERMISSION_LEVEL_VIEW', 'PERMISSION_LEVEL', 'VIEW', N'조회',     N'View',        3),
    ('PERMISSION_LEVEL_NONE', 'PERMISSION_LEVEL', 'NONE', N'없음',     N'None',        4)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, UseFlag, SortOrder, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, 1, src.SortOrder, 'system', SYSDATETIME());
GO

-- ── 확인 쿼리 ────────────────────────────────────────────────
SELECT GroupCode, GroupName, GroupNameEn FROM dbo.MD_CodeGroup WHERE GroupCode IN ('Department','Plant','WORK_SHIFT','USER_STATUS','PERMISSION_LEVEL') ORDER BY GroupCode;
SELECT CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode IN ('Department','Plant','WORK_SHIFT','USER_STATUS','PERMISSION_LEVEL') ORDER BY GroupCode, SortOrder;
SELECT DefaultShift, COUNT(*) AS Cnt FROM dbo.SYS_UserProfile GROUP BY DefaultShift;
GO
