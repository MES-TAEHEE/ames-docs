-- ════════════════════════════════════════════════════════════════════════
-- Admin Role FULL 권한 시드 (SYS_Screen + SYS_RolePermission)
-- 멱등 실행 가능 — MERGE 사용으로 중복 없음
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;

-- ── Step 1: SYS_Screen 데이터 확보 (없으면 삽입) ─────────────────────────

MERGE dbo.SYS_Screen AS tgt
USING (VALUES
  -- PP · 생산계획
  ('PP-01',  'PP', N'수요 예측',                  N'Forecast',                    'pp/forecast',            'PP-001',  1, 1),
  ('PP-02',  'PP', N'공급계획 가져오기',            N'Supply Plan Import',          'pp/supply-plan-import',  'PP-002',  2, 1),
  ('PP-03',  'PP', N'계획 확정',                  N'Plan Confirm',                'pp/plan-confirm',        'PP-003',  3, 1),
  ('PP-04',  'PP', N'작업 지시',                  N'Work Order',                  'pp/work-order',          'PP-004',  4, 1),
  ('PP-05',  'PP', N'MRP',                         N'MRP',                         'pp/mrp',                 'PP-005',  5, 1),
  ('PP-06',  'PP', N'구매 요청',                  N'Purchase Req',                'pp/purchase-req',        'PP-006',  6, 1),
  ('PP-07',  'PP', N'작업 지시 릴리스',           N'WO Release',                  'pp/wo-release',          'PP-007',  7, 1),
  ('PP-CAL', 'PP', N'캘린더',                     N'Calendar',                    'pp/calendar',            'CAL',     8, 1),
  ('PP-LSB', 'PP', N'라인 일정',                  N'Line Schedule',               'pp/line-schedule',       'LSB',     9, 1),
  ('PP-OEE', 'PP', N'라인 OEE',                   N'Line OEE',                    'pp/oee',                 'OEE',    10, 1),
  ('PP-DTL', 'PP', N'비가동 이력',                N'Downtime Log',                'pp/downtime',            'DTL',    11, 1),
  ('PP-ODM', 'PP', N'비가동 모니터',              N'Downtime Monitor',            'pp/downtime-monitor',    'ODM',    12, 1),
  ('PP-OTD', 'PP', N'납기 준수율',                N'On-Time Delivery',            'pp/delivery',            'OTD',    13, 1),
  -- MNT · 설비보전
  ('MNT-01', 'MNT', N'설비 카드',                 N'Equipment Card',              'mnt/equipment-card',     'MNT-001', 1, 1),
  ('MNT-02', 'MNT', N'고장 등록',                 N'Failure Register',            'mnt/failure',            'MNT-002', 2, 1),
  ('MNT-03', 'MNT', N'OEE 분석',                  N'OEE Analysis',                'mnt/oee-analysis',       'MNT-003', 3, 1),
  ('MNT-04', 'MNT', N'금형 관리',                 N'Mold Management',             'mnt/mold',               'MNT-004', 4, 1),
  ('MNT-05', 'MNT', N'PM 일정',                   N'PM Schedule',                 'mnt/pm-schedule',        'MNT-005', 5, 1),
  ('MNT-06', 'MNT', N'비가동 이력',               N'Downtime Log',                'mnt/downtime',           'MNT-006', 6, 1),
  ('MNT-07', 'MNT', N'작업 지시',                 N'Work Order',                  'mnt/work-order',         'MNT-007', 7, 1),
  ('MNT-08', 'MNT', N'예비 부품',                 N'Spare Parts',                 'mnt/spare-parts',        'MNT-008', 8, 1),
  ('MNT-09', 'MNT', N'대시보드',                  N'Dashboard',                   'mnt/dashboard',          'MNT-009', 9, 1),
  -- RPT · 보고서
  ('RPT-01', 'RPT', N'일별 생산 실적',             N'Daily Production',            'rpt/daily-production',   'RPT-001',  1, 1),
  ('RPT-02', 'RPT', N'불량 파레토',               N'Defect Pareto',               'rpt/defect-pareto',      'RPT-002',  2, 1),
  ('RPT-03', 'RPT', N'일별 출하 현황',             N'Daily Shipment',              'rpt/daily-shipment',     'RPT-003',  3, 1),
  ('RPT-04', 'RPT', N'납기 준수율',               N'On-Time Delivery',            'rpt/on-time',            'RPT-004',  4, 1),
  ('RPT-05', 'RPT', N'재고 현황',                 N'Inventory Status',            'rpt/inventory',          'RPT-005',  5, 1),
  ('RPT-06', 'RPT', N'설비 OEE',                  N'Equipment OEE',               'rpt/equipment-oee',      'RPT-006',  6, 1),
  ('RPT-07', 'RPT', N'월간 KPI',                  N'Monthly KPI',                 'rpt/monthly-kpi',        'RPT-007',  7, 1),
  ('RPT-08', 'RPT', N'계획 준수율',               N'Schedule Adherence',          'rpt/schedule-adherence', 'RPT-008',  8, 1),
  ('RPT-09', 'RPT', N'리포트 센터',               N'Report Center',               'rpt/report-center',      'RPT-009',  9, 1),
  ('RPT-10', 'RPT', N'리포트 빌더',               N'Report Builder',              'rpt/report-builder',     'RPT-010', 10, 1),
  -- MD · 마스터데이터
  ('MD-001', 'MD', N'공장/라인 기준정보 관리',    N'Factory / Line Master',       'md/rp/line',             'MD-001',  1, 1),
  ('MD-002', 'MD', N'공정 기준정보 관리',         N'Process / Station Master',    'md/rp/station',          'MD-002',  2, 1),
  ('MD-003', 'MD', N'제품 기준정보 관리',         N'Product Item Master',         'md/fd/items',            'MD-003',  3, 1),
  ('MD-004', 'MD', N'BOM 관리',                   N'BOM Management',              'md/fd/bom',              'MD-004',  4, 1),
  ('MD-005', 'MD', N'BOP 관리',                   N'BOP Management',              'md/rp/bop',              'MD-005',  5, 1),
  ('MD-006', 'MD', N'Work Center 관리',           N'Work Center Management',      'md/rp/work-center',      'MD-006',  6, 1),
  ('MD-007', 'MD', N'금형 기준정보 관리',         N'Mold Master',                 'md/re/mold',             'MD-007',  7, 1),
  ('MD-008', 'MD', N'원부자재 기준정보 관리',     N'Paint & Fabric Master',       'md/rm/paint-fabric',     'MD-008',  8, 1),
  ('MD-009', 'MD', N'공급업체 기준정보 관리',     N'Vendor Master',               'md/re/vendor',           'MD-009',  9, 1),
  ('MD-010', 'MD', N'고객사 기준정보 관리',       N'Customer Master',             'md/ql/customer',            'MD-010', 10, 1),
  ('MD-011', 'MD', N'출하처 기준정보 관리',       N'Shipment Destination Master', 'md/ql/shipment-dest',       'MD-011', 11, 1),
  ('MD-012', 'MD', N'불량유형 기준정보 관리',     N'Defect Code Master',          'md/ql/defect-code',         'MD-012', 12, 1),
  ('MD-013', 'MD', N'불량원인 기준정보 관리',     N'Defect Cause Master',         'md/ql/defect-cause',        'MD-013', 13, 1),
  ('MD-014', 'MD', N'설비 기준정보 관리',         N'Equipment Master',            'md/re/equipment',        'MD-014', 14, 1),
  ('MD-015', 'MD', N'건조로 기준정보 관리',       N'Oven Master',                 'md/re/oven',             'MD-015', 15, 1),
  ('MD-016', 'MD', N'지그 기준정보 관리',         N'Jig Master',                  'md/re/jig',              'MD-016', 16, 1),
  ('MD-017', 'MD', N'검사기준 기준정보 관리',     N'Inspection Standard Master',  'md/ql/inspection-standard', 'MD-017', 17, 1),
  ('MD-018', 'MD', N'창고/로케이션 기준정보 관리',N'Warehouse Location Master',   'md/fd/location',         'MD-018', 18, 1),
  ('MD-019', 'MD', N'단위 관리',                  N'UOM Master',                  'md/fd/uom',              'MD-019', 19, 1),
  ('MD-020', 'MD', N'RFID 태그 관리',             N'RFID Tag Master',             'md/rm/rfid-tag',         'MD-020', 20, 1),
  ('MD-021', 'MD', N'RAL 색상 관리',              N'RAL Color Master',            'md/rm/ral-color',        'MD-021', 21, 1),
  ('MD-022', 'MD', N'RFID 리더 관리',             N'RFID Reader Master',          'md/rm/rfid-reader',      'MD-022', 22, 1),
  ('MD-023', 'MD', N'포장 사양 관리',             N'Packaging Spec Master',       'md/ql/packaging-spec',      'MD-023', 23, 1),
  ('MD-024', 'MD', N'라벨 템플릿 관리',           N'Label Template Master',       'md/ql/label-template',      'MD-024', 24, 1),
  ('MD-025', 'MD', N'사유 코드 관리',             N'Reason Code Master',          'md/re/reason-code',      'MD-025', 25, 1),
  ('MD-026', 'MD', N'예비품 마스터',              N'Spare Part Master',           'md/re/spare-part',       'MD-026', 26, 1),
  ('MD-027', 'MD', N'PM 템플릿 관리',             N'PM Template Master',          'md/rp/pm-template',      'MD-027', 27, 1),
  ('MD-028', 'MD', N'라인 시간 패턴 관리',        N'Line Time Pattern Master',    'md/rp/line-time-pattern','MD-028', 28, 1),
  ('MD-029', 'MD', N'레시피 관리',                N'Recipe Master',               'md/ql/recipe',              'MD-029', 29, 1),
  ('MD-030', 'MD', N'코드 기준정보 관리',         N'Common Code Master',          'md/ql/common-code',         'MD-030', 30, 1),
  -- SYS · 시스템
  ('SYS-001', 'SYS', N'사용자 관리',              N'User Management',             'sys/users',              'SYS-001',  1, 1),
  ('SYS-002', 'SYS', N'역할 관리',                N'Role Management',             'sys/roles',              'SYS-002',  2, 1),
  ('SYS-003', 'SYS', N'화면 관리',                N'Screen Management',           'sys/screen',             'SYS-003',  3, 1),
  ('SYS-004', 'SYS', N'역할/권한 관리 (RBAC)',    N'Role & Permission (RBAC)',    'sys/rbac',               'SYS-004',  4, 1),
  ('SYS-005', 'SYS', N'공장 캘린더',              N'Factory Calendar',            'sys/calendar',           'SYS-005',  5, 1),
  ('SYS-006', 'SYS', N'인터페이스 모니터',        N'Interface Monitor',           'sys/interfaces',         'SYS-006',  6, 1),
  ('SYS-007', 'SYS', N'감사 로그',                N'Audit Log',                   'sys/audit',              'SYS-007',  7, 1),
  ('SYS-008', 'SYS', N'알림 관리',                N'Notification Management',     'sys/notifications',      'SYS-008',  8, 1),
  ('SYS-009', 'SYS', N'시스템 설정',              N'System Configuration',        'sys/config',             'SYS-009',  9, 1),
  ('SYS-010', 'SYS', N'시스템 상태',              N'System Health',               'sys/health',             'SYS-010', 10, 1)
) AS src (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible)
ON tgt.ScreenCode = src.ScreenCode
WHEN NOT MATCHED THEN
    INSERT (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy)
    VALUES (src.ScreenCode, src.ModuleCode, src.ScreenName, src.ScreenNameEn,
            src.HRef, src.LidLabel, src.SortOrder, src.IsVisible, 'seed')
WHEN MATCHED AND (tgt.HRef IS NULL OR tgt.HRef != src.HRef) THEN
    UPDATE SET tgt.HRef = src.HRef, tgt.ModifiedBy = 'seed', tgt.ModifiedTS = SYSDATETIME();

PRINT CONCAT('SYS_Screen: ', @@ROWCOUNT, '행 처리됨');
GO

-- ModuleCode='WEB'/ProcessCode 정규화 (단독 실행 시 이전 구조 보정)
UPDATE dbo.SYS_Screen SET ModuleCode='WEB', ProcessCode='PP'  WHERE ScreenCode LIKE 'PP-%'  AND (ModuleCode != 'WEB' OR ProcessCode IS NULL);
UPDATE dbo.SYS_Screen SET ModuleCode='WEB', ProcessCode='MNT' WHERE ScreenCode LIKE 'MNT-%' AND (ModuleCode != 'WEB' OR ProcessCode IS NULL);
UPDATE dbo.SYS_Screen SET ModuleCode='WEB', ProcessCode='RPT' WHERE ScreenCode LIKE 'RPT-%' AND (ModuleCode != 'WEB' OR ProcessCode IS NULL);
UPDATE dbo.SYS_Screen SET ModuleCode='WEB', ProcessCode='MD'  WHERE ScreenCode LIKE 'MD-%'  AND (ModuleCode != 'WEB' OR ProcessCode IS NULL);
UPDATE dbo.SYS_Screen SET ModuleCode='WEB', ProcessCode='SYS' WHERE ScreenCode LIKE 'SYS-%' AND (ModuleCode != 'WEB' OR ProcessCode IS NULL);
GO

-- SubProcessCode HRef 기반 설정 (MD 화면)
UPDATE dbo.SYS_Screen SET SubProcessCode='Fd' WHERE ProcessCode='MD' AND HRef LIKE 'md/fd/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Rp' WHERE ProcessCode='MD' AND HRef LIKE 'md/rp/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Re' WHERE ProcessCode='MD' AND HRef LIKE 'md/re/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Rm' WHERE ProcessCode='MD' AND HRef LIKE 'md/rm/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Ql' WHERE ProcessCode='MD' AND HRef LIKE 'md/ql/%';
UPDATE dbo.SYS_Screen SET SubProcessCode=NULL WHERE ProcessCode='MD' AND HRef NOT LIKE 'md/%/%';
GO

-- ── Step 2: Admin Role 확보 ───────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Name = 'Admin')
BEGIN
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Admin', 'ADMIN', CAST(NEWID() AS NVARCHAR(MAX)));
    PRINT 'Admin 롤 생성 완료';
END
ELSE
    PRINT 'Admin 롤 이미 존재';
GO

-- ── Step 3: SYS_RolePermission — Admin FULL 권한 upsert ──────────────────

DECLARE @AdminRoleId NVARCHAR(450);
SELECT @AdminRoleId = Id FROM dbo.AspNetRoles WHERE Name = 'Admin';

-- PermissionService는 'R'/'RE'/'REA' 문자 조합으로 권한 판단
DECLARE @FullLevel VARCHAR(10);
SET @FullLevel = 'REA'; -- R=조회, E=편집, A=승인 전체 허용

MERGE dbo.SYS_RolePermission AS tgt
USING (
    SELECT s.ScreenCode, s.ModuleCode
    FROM   dbo.SYS_Screen s
) AS src ON tgt.RoleName = 'Admin' AND tgt.ScreenCode = src.ScreenCode
WHEN MATCHED THEN
    UPDATE SET
        tgt.RoleID          = @AdminRoleId,
        tgt.PermissionLevel = @FullLevel,
        tgt.IsSystemRole    = 1,
        tgt.ModifiedBy      = 'seed',
        tgt.ModifiedTS      = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel,
            IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES (@AdminRoleId, 'Admin', src.ModuleCode, src.ScreenCode, @FullLevel,
            1, SYSDATETIME(), 'seed', SYSDATETIME());

PRINT CONCAT('SYS_RolePermission Admin FULL(', @FullLevel, '): ', @@ROWCOUNT, '행 처리됨');
GO

-- ── Step 4: admin@ames.local 계정에 Admin Role 부여 ──────────────────────

DECLARE @UserId    NVARCHAR(450);
DECLARE @AdminRoleId2 NVARCHAR(450);

SELECT @UserId       = Id FROM dbo.AspNetUsers WHERE NormalizedEmail = 'ADMIN@AMES.LOCAL';
SELECT @AdminRoleId2 = Id FROM dbo.AspNetRoles  WHERE Name = 'Admin';

IF @UserId IS NOT NULL AND @AdminRoleId2 IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @UserId AND RoleId = @AdminRoleId2)
    BEGIN
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        VALUES (@UserId, @AdminRoleId2);
        PRINT 'admin@ames.local → Admin 롤 할당 완료';
    END
    ELSE
        PRINT 'admin@ames.local Admin 롤 이미 할당됨';
END
ELSE
    PRINT '⚠ admin@ames.local 또는 Admin 롤 미발견 — 수동 확인 필요';
GO

-- ── 결과 확인 ─────────────────────────────────────────────────────────────

SELECT
    rp.ModuleCode,
    rp.ScreenCode,
    s.HRef,
    rp.PermissionLevel
FROM   dbo.SYS_RolePermission rp
JOIN   dbo.SYS_Screen         s  ON s.ScreenCode = rp.ScreenCode
WHERE  rp.RoleName = 'Admin'
ORDER  BY rp.ModuleCode, rp.ScreenCode;

SELECT COUNT(*) AS [Admin FULL 권한 수]
FROM   dbo.SYS_RolePermission
WHERE  RoleName = 'Admin'
  AND  PermissionLevel = (SELECT CAST(SUM(CAST(CodeValue AS INT)) AS VARCHAR(10))
                          FROM   dbo.MD_CodeItem
                          WHERE  GroupCode = 'PERMISSION_LEVEL' AND UseFlag = 1);
GO
