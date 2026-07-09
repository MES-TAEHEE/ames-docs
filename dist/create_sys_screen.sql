-- ════════════════════════════════════════════════════════════════════════
-- SYS_Screen  (화면 기준정보 — 가시성 및 내비게이션 관리)
-- MD_Menu / MD_MenuRole → SYS_Screen 마이그레이션
-- AMES_DEV 기존 DB에 직접 적용하는 스크립트
-- ════════════════════════════════════════════════════════════════════════

USE AMES_DEV;
GO

-- ── 1. SYS_Screen 테이블 생성 ────────────────────────────────────────
IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL DROP TABLE dbo.SYS_Screen;
GO

CREATE TABLE dbo.SYS_Screen (
  [ScreenID]       INT IDENTITY     NOT NULL,
  [ScreenCode]     VARCHAR(20)      NOT NULL,   -- e.g. 'PP-01', 'SYS-003'
  [ModuleCode]     VARCHAR(10)      NOT NULL,   -- 포탈 식별자 (WEB / POP / PDA)
  [ProcessCode]    VARCHAR(10)          NULL,   -- 기능 영역 (PP / MNT / RPT / MD / SYS)
  [SubProcessCode] VARCHAR(10)          NULL,   -- MD 서브그룹 (Rp/Fd/Re/Rm/Ql)
  [ScreenName]     NVARCHAR(100)    NOT NULL,
  [ScreenNameEn]   NVARCHAR(100)        NULL,
  [HRef]           VARCHAR(200)         NULL,   -- route path (e.g. 'md/rp/line')
  [LidLabel]       VARCHAR(20)          NULL,   -- chip label shown in NavMenu
  [SortOrder]      INT                  NULL,
  [IsVisible]      BIT                  NULL  DEFAULT 1,
  [CreatedBy]      VARCHAR(50)      NOT NULL,
  [CreatedTS]      DATETIME2            NULL  DEFAULT SYSDATETIME(),
  [ModifiedBy]     VARCHAR(50)          NULL,
  [ModifiedTS]     DATETIME2            NULL,
  CONSTRAINT PK_SYS_Screen PRIMARY KEY CLUSTERED ([ScreenID]),
  CONSTRAINT UQ_SYS_Screen UNIQUE ([ScreenCode])
);
GO

-- ── 2. Seed: PP · 생산계획 ───────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('PP-01',  'WEB','PP', N'수요 예측',        N'Forecast',          'pp/forecast',         'PP-01',   1, 1, 'admin'),
  ('PP-02',  'WEB','PP', N'SAP 연동',         N'SAP Import',        'pp/sap-import',       'PP-02',   2, 1, 'admin'),
  ('PP-03',  'WEB','PP', N'계획 확정',        N'Plan Confirm',      'pp/plan-confirm',     'PP-03',   3, 1, 'admin'),
  ('PP-04',  'WEB','PP', N'작업 지시',        N'Work Order',        'pp/work-order',       'PP-04',   4, 1, 'admin'),
  ('PP-05',  'WEB','PP', N'MRP',              N'MRP',               'pp/mrp',              'PP-05',   5, 1, 'admin'),
  ('PP-06',  'WEB','PP', N'구매 요청',        N'Purchase Req',      'pp/purchase-req',     'PP-06',   6, 1, 'admin'),
  ('PP-07',  'WEB','PP', N'작업 지시 릴리스', N'WO Release',        'pp/wo-release',       'PP-07',   7, 1, 'admin'),
  ('PP-CAL', 'WEB','PP', N'캘린더',           N'Calendar',          'pp/calendar',         'CAL',     8, 1, 'admin'),
  ('PP-LSB', 'WEB','PP', N'라인 일정',        N'Line Schedule',     'pp/line-schedule',    'LSB',     9, 1, 'admin'),
  ('PP-OEE', 'WEB','PP', N'라인 OEE',         N'Line OEE',          'pp/oee',              'OEE',    10, 1, 'admin'),
  ('PP-DTL', 'WEB','PP', N'비가동 이력',      N'Downtime Log',      'pp/downtime',         'DTL',    11, 1, 'admin'),
  ('PP-ODM', 'WEB','PP', N'비가동 모니터',    N'Downtime Monitor',  'pp/downtime-monitor', 'ODM',    12, 1, 'admin'),
  ('PP-OTD', 'WEB','PP', N'납기 준수율',      N'On-Time Delivery',  'pp/delivery',         'OTD',    13, 1, 'admin');
GO

-- ── 3. Seed: MNT · 설비보전 ──────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('MNT-01', 'WEB','MNT', N'설비 카드',   N'Equipment Card',   'mnt/equipment-card', 'MNT-01', 1, 1, 'admin'),
  ('MNT-02', 'WEB','MNT', N'고장 등록',   N'Failure Register', 'mnt/failure',        'MNT-02', 2, 1, 'admin'),
  ('MNT-03', 'WEB','MNT', N'OEE 분석',    N'OEE Analysis',     'mnt/oee-analysis',   'MNT-03', 3, 1, 'admin'),
  ('MNT-04', 'WEB','MNT', N'금형 관리',   N'Mold Management',  'mnt/mold',           'MNT-04', 4, 1, 'admin'),
  ('MNT-05', 'WEB','MNT', N'PM 일정',     N'PM Schedule',      'mnt/pm-schedule',    'MNT-05', 5, 1, 'admin'),
  ('MNT-06', 'WEB','MNT', N'비가동 이력', N'Downtime Log',     'mnt/downtime',       'MNT-06', 6, 1, 'admin'),
  ('MNT-07', 'WEB','MNT', N'작업 지시',   N'Work Order',       'mnt/work-order',     'MNT-07', 7, 1, 'admin'),
  ('MNT-08', 'WEB','MNT', N'예비 부품',   N'Spare Parts',      'mnt/spare-parts',    'MNT-08', 8, 1, 'admin'),
  ('MNT-09', 'WEB','MNT', N'대시보드',    N'Dashboard',        'mnt/dashboard',      'MNT-09', 9, 1, 'admin');
GO

-- ── 4. Seed: RPT · 보고서 ────────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('RPT-01', 'WEB','RPT', N'일별 생산 실적', N'Daily Production',   'rpt/daily-production',   'RPT-01',  1, 1, 'admin'),
  ('RPT-02', 'WEB','RPT', N'불량 파레토',   N'Defect Pareto',      'rpt/defect-pareto',      'RPT-02',  2, 1, 'admin'),
  ('RPT-03', 'WEB','RPT', N'일별 출하 현황',N'Daily Shipment',     'rpt/daily-shipment',     'RPT-03',  3, 1, 'admin'),
  ('RPT-04', 'WEB','RPT', N'납기 준수율',   N'On-Time Delivery',   'rpt/on-time',            'RPT-04',  4, 1, 'admin'),
  ('RPT-05', 'WEB','RPT', N'재고 현황',     N'Inventory Status',   'rpt/inventory',          'RPT-05',  5, 1, 'admin'),
  ('RPT-06', 'WEB','RPT', N'설비 OEE',      N'Equipment OEE',      'rpt/equipment-oee',      'RPT-06',  6, 1, 'admin'),
  ('RPT-07', 'WEB','RPT', N'월간 KPI',      N'Monthly KPI',        'rpt/monthly-kpi',        'RPT-07',  7, 1, 'admin'),
  ('RPT-08', 'WEB','RPT', N'계획 준수율',   N'Schedule Adherence', 'rpt/schedule-adherence', 'RPT-08',  8, 1, 'admin'),
  ('RPT-09', 'WEB','RPT', N'리포트 센터',   N'Report Center',      'rpt/report-center',      'RPT-09',  9, 1, 'admin'),
  ('RPT-10', 'WEB','RPT', N'리포트 빌더',   N'Report Builder',     'rpt/report-builder',     'RPT-10', 10, 1, 'admin');
GO

-- ── 5. Seed: MD · 마스터데이터 ───────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('MD-001', 'WEB','MD','Rp', N'공장/라인 기준정보 관리',     N'Factory / Line Master',          'md/rp/line',                'MD-001',  1, 1, 'admin'),
  ('MD-002', 'WEB','MD','Rp', N'공정 기준정보 관리',          N'Process / Station Master',       'md/rp/station',             'MD-002',  2, 1, 'admin'),
  ('MD-003', 'WEB','MD','Fd', N'제품 기준정보 관리',          N'Product Item Master',            'md/fd/items',               'MD-003',  3, 1, 'admin'),
  ('MD-004', 'WEB','MD','Fd', N'BOM 관리',                    N'BOM Management',                 'md/fd/bom',                 'MD-004',  4, 1, 'admin'),
  ('MD-005', 'WEB','MD','Rp', N'BOP 관리',                    N'BOP Management',                 'md/rp/bop',                 'MD-005',  5, 1, 'admin'),
  ('MD-006', 'WEB','MD','Rp', N'Work Center 관리',            N'Work Center Management',         'md/rp/work-center',         'MD-006',  6, 1, 'admin'),
  ('MD-007', 'WEB','MD','Re', N'금형 기준정보 관리',          N'Mold Master',                    'md/re/mold',                'MD-007',  7, 1, 'admin'),
  ('MD-008', 'WEB','MD','Rm', N'원부자재 기준정보 관리',      N'Paint & Fabric Master',          'md/rm/paint-fabric',        'MD-008',  8, 1, 'admin'),
  ('MD-009', 'WEB','MD','Re', N'공급업체 기준정보 관리',      N'Vendor Master',                  'md/re/vendor',              'MD-009',  9, 1, 'admin'),
  ('MD-010', 'WEB','MD','Ql', N'고객사 기준정보 관리',        N'Customer Master',                'md/ql/customer',            'MD-010', 10, 1, 'admin'),
  ('MD-011', 'WEB','MD','Ql', N'출하처 기준정보 관리',        N'Shipment Destination Master',    'md/ql/shipment-dest',       'MD-011', 11, 1, 'admin'),
  ('MD-012', 'WEB','MD','Ql', N'불량유형 기준정보 관리',      N'Defect Code Master',             'md/ql/defect-code',         'MD-012', 12, 1, 'admin'),
  ('MD-013', 'WEB','MD','Ql', N'불량원인 기준정보 관리',      N'Defect Cause Master',            'md/ql/defect-cause',        'MD-013', 13, 1, 'admin'),
  ('MD-014', 'WEB','MD','Re', N'설비 기준정보 관리',          N'Equipment Master',               'md/re/equipment',           'MD-014', 14, 1, 'admin'),
  ('MD-015', 'WEB','MD','Re', N'건조로 기준정보 관리',        N'Oven Master',                    'md/re/oven',                'MD-015', 15, 1, 'admin'),
  ('MD-016', 'WEB','MD','Re', N'지그 기준정보 관리',          N'Jig Master',                     'md/re/jig',                 'MD-016', 16, 1, 'admin'),
  ('MD-017', 'WEB','MD','Ql', N'검사기준 기준정보 관리',      N'Inspection Standard Master',     'md/ql/inspection-standard', 'MD-017', 17, 1, 'admin'),
  ('MD-018', 'WEB','MD','Fd', N'창고/로케이션 기준정보 관리', N'Warehouse Location Master',      'md/fd/location',            'MD-018', 18, 1, 'admin'),
  ('MD-019', 'WEB','MD','Fd', N'단위 관리',                   N'UOM Master',                     'md/fd/uom',                 'MD-019', 19, 1, 'admin'),
  ('MD-020', 'WEB','MD','Rm', N'RFID 태그 관리',              N'RFID Tag Master',                'md/rm/rfid-tag',            'MD-020', 20, 1, 'admin'),
  ('MD-021', 'WEB','MD','Rm', N'RAL 색상 관리',               N'RAL Color Master',               'md/rm/ral-color',           'MD-021', 21, 1, 'admin'),
  ('MD-022', 'WEB','MD','Rm', N'RFID 리더 관리',              N'RFID Reader Master',             'md/rm/rfid-reader',         'MD-022', 22, 1, 'admin'),
  ('MD-023', 'WEB','MD','Ql', N'포장 사양 관리',              N'Packaging Spec Master',          'md/ql/packaging-spec',      'MD-023', 23, 1, 'admin'),
  ('MD-024', 'WEB','MD','Ql', N'라벨 템플릿 관리',            N'Label Template Master',          'md/ql/label-template',      'MD-024', 24, 1, 'admin'),
  ('MD-025', 'WEB','MD','Re', N'사유 코드 관리',              N'Reason Code Master',             'md/re/reason-code',         'MD-025', 25, 1, 'admin'),
  ('MD-026', 'WEB','MD','Re', N'예비품 마스터',               N'Spare Part Master',              'md/re/spare-part',          'MD-026', 26, 1, 'admin'),
  ('MD-027', 'WEB','MD','Rp', N'PM 템플릿 관리',              N'PM Template Master',             'md/rp/pm-template',         'MD-027', 27, 1, 'admin'),
  ('MD-028', 'WEB','MD','Rp', N'라인 시간 패턴 관리',         N'Line Time Pattern Master',       'md/rp/line-time-pattern',   'MD-028', 28, 1, 'admin'),
  ('MD-029', 'WEB','MD','Ql', N'레시피 관리',                 N'Recipe Master',                  'md/ql/recipe',              'MD-029', 29, 1, 'admin'),
  ('MD-030', 'WEB','MD','Ql', N'코드 기준정보 관리',          N'Common Code Master',             'md/ql/common-code',         'MD-030', 30, 1, 'admin');
GO

-- ── 6. Seed: SYS · 시스템 (SYS-003=화면관리, SYS-004=RBAC) ──────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('SYS-001', 'WEB','SYS', N'사용자 관리',           N'User Management',          'sys/users',         'SYS-001',  1, 1, 'admin'),
  ('SYS-002', 'WEB','SYS', N'역할 관리',             N'Role Management',          'sys/roles',         'SYS-002',  2, 1, 'admin'),
  ('SYS-003', 'WEB','SYS', N'화면 관리',             N'Screen Management',        'sys/screen',        'SYS-003',  3, 1, 'admin'),
  ('SYS-004', 'WEB','SYS', N'역할/권한 관리 (RBAC)', N'Role & Permission (RBAC)', 'sys/rbac',          'SYS-004',  4, 1, 'admin'),
  ('SYS-005', 'WEB','SYS', N'공장 캘린더',           N'Factory Calendar',         'sys/calendar',      'SYS-005',  5, 1, 'admin'),
  ('SYS-006', 'WEB','SYS', N'인터페이스 모니터',     N'Interface Monitor',        'sys/interfaces',    'SYS-006',  6, 1, 'admin'),
  ('SYS-007', 'WEB','SYS', N'감사 로그',             N'Audit Log',                'sys/audit',         'SYS-007',  7, 1, 'admin'),
  ('SYS-008', 'WEB','SYS', N'알림 관리',             N'Notification Management',  'sys/notifications', 'SYS-008',  8, 1, 'admin'),
  ('SYS-009', 'WEB','SYS', N'시스템 설정',           N'System Configuration',     'sys/config',        'SYS-009',  9, 1, 'admin'),
  ('SYS-010', 'WEB','SYS', N'시스템 상태',           N'System Health',            'sys/health',        'SYS-010', 10, 1, 'admin');
GO

PRINT '✓ SYS_Screen: 62 rows (PP:13 + MNT:9 + RPT:10 + MD:30 + SYS:10)';
GO
