-- ════════════════════════════════════════════════════════════════════════
--  migrate_mnt_pm_screens.sql
--  PM 일정 화면 분리: MNT-005 설비 PM 일정 · MNT-010 보전 PM 일정 (+ 공통코드 PM_CYCLE_BASIS · PM_STATUS)
--
--  둘 다 MNT_PMSchedule 한 테이블을 쓰고 PMClass(EQUIP/MAINT) 로만 나뉜다.
--  · MNT-005 (mnt/pm-schedule)       : 화면명을 '설비 PM 일정' 으로 변경
--  · MNT-010 (mnt/maint-pm-schedule) : 신규 등록 + Admin REA
--  · 등록 모달 콤보용 공통코드: PM_CYCLE_BASIS(TIME/CYCLE), PM_STATUS(OK/DUE/OVERDUE/DONE/CANCELLED)
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_mnt_pm_screens.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1) SYS_Screen ─────────────────────────────────────────────────────────
UPDATE dbo.SYS_Screen
   SET ScreenName = N'설비 PM 일정', ScreenNameEn = N'Equipment PM Schedule', ModifiedBy = 'seed', ModifiedTS = SYSDATETIME()
 WHERE ScreenCode = 'MNT-005' AND (ScreenName <> N'설비 PM 일정' OR ScreenNameEn <> N'Equipment PM Schedule');
PRINT CONCAT(N'✓ MNT-005 화면명 갱신: ', @@ROWCOUNT, N'행');

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MNT-010')
BEGIN
    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('MNT-010', 'WEB', 'MNT', NULL, N'보전 PM 일정', N'Maintenance PM Schedule', 'mnt/maint-pm-schedule', 'MNT-010', 10, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MNT-010 등록 (mnt/maint-pm-schedule)';
END
ELSE
    PRINT N'· SYS_Screen MNT-010 이미 존재';
GO

-- ── 2) Admin 권한 ─────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'MNT-010')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, 'Admin', 'WEB', 'MNT', 'MNT-010', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/MNT-010 (REA)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/MNT-010 이미 존재';
GO

-- ── 2b) 기존 행 PMClass 백필: 분류가 없던 시연 PM 은 설비 PM(EQUIP) 으로 본다 ──
UPDATE dbo.MNT_PMSchedule SET PMClass = 'EQUIP' WHERE PMClass IS NULL;
PRINT CONCAT(N'✓ PMClass NULL → EQUIP 백필: ', @@ROWCOUNT, N'행');
GO

-- ── 3) 공통코드 PM_CYCLE_BASIS · PM_STATUS ───────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'PM_CYCLE_BASIS')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('PM_CYCLE_BASIS', N'PM 주기 기준', N'PM cycle basis', N'MNT_PMSchedule.CycleBasis', 1, 'admin@ames.local');
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'PM_STATUS')
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('PM_STATUS', N'PM 상태', N'PM status', N'MNT_PMSchedule.Status', 1, 'admin@ames.local');

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
  ('PM_CYCLE_BASIS_TIME',  'PM_CYCLE_BASIS', 'TIME',      N'기간(일)',   N'Time (days)',    10),
  ('PM_CYCLE_BASIS_CYCLE', 'PM_CYCLE_BASIS', 'CYCLE',     N'사이클(샷)', N'Cycle (shots)',  20),
  ('PM_STATUS_OK',         'PM_STATUS',      'OK',        N'정상',       N'OK',             10),
  ('PM_STATUS_DUE',        'PM_STATUS',      'DUE',       N'도래',       N'Due',            20),
  ('PM_STATUS_OVERDUE',    'PM_STATUS',      'OVERDUE',   N'지연',       N'Overdue',        30),
  ('PM_STATUS_DONE',       'PM_STATUS',      'DONE',      N'완료',       N'Done',           40),
  ('PM_STATUS_CANCELLED',  'PM_STATUS',      'CANCELLED', N'취소',       N'Cancelled',      50)
) AS src (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, NULL, src.SortOrder, NULL, 1, NULL, 'admin@ames.local');
PRINT CONCAT(N'✓ MD_CodeItem PM_CYCLE_BASIS/PM_STATUS: ', @@ROWCOUNT, N'행');
GO

SELECT ScreenCode, ScreenName, ScreenNameEn, HRef, SortOrder FROM dbo.SYS_Screen WHERE ScreenCode IN ('MNT-005','MNT-010');
SELECT ScreenCode, RoleName, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode IN ('MNT-005','MNT-010') ORDER BY 1, 2;
SELECT GroupCode, CodeValue, CodeName FROM dbo.MD_CodeItem WHERE GroupCode IN ('PM_CYCLE_BASIS','PM_STATUS') ORDER BY GroupCode, SortOrder;
GO
