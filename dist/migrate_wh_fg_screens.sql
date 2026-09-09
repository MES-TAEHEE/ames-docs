-- ════════════════════════════════════════════════════════════════════════
--  migrate_wh_fg_screens.sql
--  Web WH·FG 화면 SYS_Screen 등록 + 권한 정리
--
--  지금까지 Web 의 창고(WH)·완제품(FG) 메뉴는 NavMenu 에 정적으로 박혀 있었고
--  권한 필터도 wh/·fg/ 는 무조건 통과시켰다. 다른 모듈처럼 SYS_Screen 이 정본이
--  되도록 화면을 데이터로 등록하고, SYS_Screen 에 없는 구 코드(WH-01·WH-04·FG-01·FG-05)
--  로 남아 있던 SYS_RolePermission 행은 새 코드로 옮긴다(Supervisor RE / Operator R 보존).
--
--  WH-002~005 는 2026-09-02 개발 DB 에 먼저 들어간 행이라 코드를 유지하고, 재고 조회는
--  WH-006 을 쓴다. WH-001 은 dist/pda/PDA_SEED.sql 이 레거시 정리로 삭제하므로 쓰지 않는다.
--
--  스키마 변경 없음(데이터만). 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_wh_fg_screens.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1) SYS_Screen upsert ──────────────────────────────────────────────────
MERGE dbo.SYS_Screen AS tgt
USING (VALUES
  ('WH-006', 'WH', N'재고 조회',     N'Inventory Search',  'wh/inventory',         1, 1),
  ('WH-003', 'WH', N'로케이션 맵',   N'Location Map',      'wh/location-map',      2, 1),
  ('WH-004', 'WH', N'재고 이력',     N'Inventory History', 'wh/log-history',       3, 1),
  ('WH-002', 'WH', N'피킹 오더',     N'Picking Orders',    'wh/picking-orders',    4, 1),
  ('WH-005', 'WH', N'재고 설정',     N'Inventory Setting', 'wh/inventory-setting', 5, 1),
  ('FG-001', 'FG', N'재고 조회',     N'Inventory Search',  'fg/inventory',         1, 1),
  ('FG-002', 'FG', N'로케이션 맵',   N'Location Map',      'fg/location-map',      2, 1),
  ('FG-003', 'FG', N'고객사 리턴',   N'Customer Returns',  'fg/customer-returns',  3, 1),
  ('FG-004', 'FG', N'출하 목록',     N'Shipments',         'fg/shipments',         4, 1),
  ('FG-005', 'FG', N'작업 이력',     N'History',           'fg/history',           5, 1)
) AS src (ScreenCode, ProcessCode, ScreenName, ScreenNameEn, HRef, SortOrder, IsVisible)
ON tgt.ScreenCode = src.ScreenCode
WHEN NOT MATCHED THEN
    INSERT (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES (src.ScreenCode, 'WEB', src.ProcessCode, NULL, src.ScreenName, src.ScreenNameEn, src.HRef, src.ScreenCode, src.SortOrder, src.IsVisible, 'seed', SYSDATETIME())
WHEN MATCHED THEN
    UPDATE SET tgt.ModuleCode = 'WEB', tgt.ProcessCode = src.ProcessCode, tgt.ScreenName = src.ScreenName, tgt.ScreenNameEn = src.ScreenNameEn,
               tgt.HRef = src.HRef, tgt.LidLabel = src.ScreenCode, tgt.SortOrder = src.SortOrder, tgt.IsVisible = src.IsVisible,
               tgt.ModifiedBy = 'seed', tgt.ModifiedTS = SYSDATETIME();
PRINT CONCAT(N'✓ SYS_Screen WH/FG upsert: ', @@ROWCOUNT, N'행');
GO

-- ── 2) 구 코드 권한 행 → 새 코드로 이관 ──────────────────────────────────
DECLARE @map TABLE (OldCode VARCHAR(20), NewCode VARCHAR(20));
INSERT INTO @map VALUES ('WH-01','WH-006'), ('WH-04','WH-002'), ('FG-01','FG-001'), ('FG-05','FG-005');

UPDATE p
   SET p.ScreenCode = m.NewCode, p.ModifiedBy = 'seed', p.ModifiedTS = SYSDATETIME()
  FROM dbo.SYS_RolePermission p
  JOIN @map m ON m.OldCode = p.ScreenCode
 WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission x WHERE x.RoleName = p.RoleName AND x.ScreenCode = m.NewCode);
PRINT CONCAT(N'✓ 구 코드 권한 이관: ', @@ROWCOUNT, N'행');

DELETE p FROM dbo.SYS_RolePermission p JOIN @map m ON m.OldCode = p.ScreenCode;
PRINT CONCAT(N'✓ 구 코드 권한 잔여 삭제: ', @@ROWCOUNT, N'행');
GO

-- ── 3) Admin FULL 권한 (없는 화면만) ─────────────────────────────────────
DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
INSERT INTO dbo.SYS_RolePermission
    (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
SELECT @AdminRoleId, 'Admin', 'WEB', s.ProcessCode, s.ScreenCode, 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME()
  FROM dbo.SYS_Screen s
 WHERE s.ProcessCode IN ('WH','FG')
   AND NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleName = 'Admin' AND p.ScreenCode = s.ScreenCode);
PRINT CONCAT(N'✓ Admin/WH·FG REA 추가: ', @@ROWCOUNT, N'행');
GO

SELECT ScreenCode, ProcessCode, HRef, ScreenName, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ProcessCode IN ('WH','FG') ORDER BY ProcessCode DESC, SortOrder;
SELECT ScreenCode, RoleName, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode LIKE 'WH-%' OR ScreenCode LIKE 'FG-%' ORDER BY ScreenCode, RoleName;
GO
