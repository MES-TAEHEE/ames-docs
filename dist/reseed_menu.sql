-- SYS_Screen 전체 재시드 (TRUNCATE + INSERT)
-- MD_Menu / MD_MenuRole 대체 스크립트

USE AMES_DEV;
GO

TRUNCATE TABLE dbo.SYS_Screen;
GO

-- PP · 생산계획
INSERT INTO dbo.SYS_Screen (ScreenCode,ModuleCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy) VALUES
  ('PP-01',  'PP', N'수요 예측',        N'Forecast',          'pp/forecast',         'PP-01',   1,1,'admin'),
  ('PP-02',  'PP', N'SAP 연동',         N'SAP Import',        'pp/sap-import',       'PP-02',   2,1,'admin'),
  ('PP-03',  'PP', N'계획 확정',        N'Plan Confirm',      'pp/plan-confirm',     'PP-03',   3,1,'admin'),
  ('PP-04',  'PP', N'작업 지시',        N'Work Order',        'pp/work-order',       'PP-04',   4,1,'admin'),
  ('PP-05',  'PP', N'MRP',              N'MRP',               'pp/mrp',              'PP-05',   5,1,'admin'),
  ('PP-06',  'PP', N'구매 요청',        N'Purchase Req',      'pp/purchase-req',     'PP-06',   6,1,'admin'),
  ('PP-07',  'PP', N'작업 지시 릴리스', N'WO Release',        'pp/wo-release',       'PP-07',   7,1,'admin'),
  ('PP-CAL', 'PP', N'캘린더',           N'Calendar',          'pp/calendar',         'CAL',     8,1,'admin'),
  ('PP-LSB', 'PP', N'라인 일정',        N'Line Schedule',     'pp/line-schedule',    'LSB',     9,1,'admin'),
  ('PP-OEE', 'PP', N'라인 OEE',         N'Line OEE',          'pp/oee',              'OEE',    10,1,'admin'),
  ('PP-DTL', 'PP', N'비가동 이력',      N'Downtime Log',      'pp/downtime',         'DTL',    11,1,'admin'),
  ('PP-ODM', 'PP', N'비가동 모니터',    N'Downtime Monitor',  'pp/downtime-monitor', 'ODM',    12,1,'admin'),
  ('PP-OTD', 'PP', N'납기 준수율',      N'On-Time Delivery',  'pp/delivery',         'OTD',    13,1,'admin');
GO

-- MNT · 설비보전
INSERT INTO dbo.SYS_Screen (ScreenCode,ModuleCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy) VALUES
  ('MNT-01','MNT',N'설비 카드',   N'Equipment Card',   'mnt/equipment-card','MNT-01',1,1,'admin'),
  ('MNT-02','MNT',N'고장 등록',   N'Failure Register', 'mnt/failure',       'MNT-02',2,1,'admin'),
  ('MNT-03','MNT',N'OEE 분석',    N'OEE Analysis',     'mnt/oee-analysis',  'MNT-03',3,1,'admin'),
  ('MNT-04','MNT',N'금형 관리',   N'Mold Management',  'mnt/mold',          'MNT-04',4,1,'admin'),
  ('MNT-05','MNT',N'PM 일정',     N'PM Schedule',      'mnt/pm-schedule',   'MNT-05',5,1,'admin'),
  ('MNT-06','MNT',N'비가동 이력', N'Downtime Log',     'mnt/downtime',      'MNT-06',6,1,'admin'),
  ('MNT-07','MNT',N'작업 지시',   N'Work Order',       'mnt/work-order',    'MNT-07',7,1,'admin'),
  ('MNT-08','MNT',N'예비 부품',   N'Spare Parts',      'mnt/spare-parts',   'MNT-08',8,1,'admin'),
  ('MNT-09','MNT',N'대시보드',    N'Dashboard',        'mnt/dashboard',     'MNT-09',9,1,'admin');
GO

-- RPT · 보고서
INSERT INTO dbo.SYS_Screen (ScreenCode,ModuleCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy) VALUES
  ('RPT-01','RPT',N'일별 생산 실적', N'Daily Production',   'rpt/daily-production',   'RPT-01', 1,1,'admin'),
  ('RPT-02','RPT',N'불량 파레토',   N'Defect Pareto',      'rpt/defect-pareto',      'RPT-02', 2,1,'admin'),
  ('RPT-03','RPT',N'일별 출하 현황',N'Daily Shipment',     'rpt/daily-shipment',     'RPT-03', 3,1,'admin'),
  ('RPT-04','RPT',N'납기 준수율',   N'On-Time Delivery',   'rpt/on-time',            'RPT-04', 4,1,'admin'),
  ('RPT-05','RPT',N'재고 현황',     N'Inventory Status',   'rpt/inventory',          'RPT-05', 5,1,'admin'),
  ('RPT-06','RPT',N'설비 OEE',      N'Equipment OEE',      'rpt/equipment-oee',      'RPT-06', 6,1,'admin'),
  ('RPT-07','RPT',N'월간 KPI',      N'Monthly KPI',        'rpt/monthly-kpi',        'RPT-07', 7,1,'admin'),
  ('RPT-08','RPT',N'계획 준수율',   N'Schedule Adherence', 'rpt/schedule-adherence', 'RPT-08', 8,1,'admin'),
  ('RPT-09','RPT',N'리포트 센터',   N'Report Center',      'rpt/report-center',      'RPT-09', 9,1,'admin'),
  ('RPT-10','RPT',N'리포트 빌더',   N'Report Builder',     'rpt/report-builder',     'RPT-10',10,1,'admin');
GO

-- MD · 마스터데이터
INSERT INTO dbo.SYS_Screen (ScreenCode,ModuleCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy) VALUES
  ('MD-001','MD',N'공장/라인 기준정보 관리',     N'Factory / Line Master',          'md/line',                'MD-001', 1,1,'admin'),
  ('MD-002','MD',N'공정 기준정보 관리',          N'Process / Station Master',       'md/station',             'MD-002', 2,1,'admin'),
  ('MD-003','MD',N'제품 기준정보 관리',          N'Product Item Master',            'md/items',               'MD-003', 3,1,'admin'),
  ('MD-004','MD',N'BOM 관리',                    N'BOM Management',                 'md/bom',                 'MD-004', 4,1,'admin'),
  ('MD-005','MD',N'BOP 관리',                    N'BOP Management',                 'md/bop',                 'MD-005', 5,1,'admin'),
  ('MD-006','MD',N'Work Center 관리',            N'Work Center Management',         'md/work-center',         'MD-006', 6,1,'admin'),
  ('MD-007','MD',N'금형 기준정보 관리',          N'Mold Master',                    'md/mold',                'MD-007', 7,1,'admin'),
  ('MD-008','MD',N'원부자재 기준정보 관리',      N'Paint & Fabric Master',          'md/paint-fabric',        'MD-008', 8,1,'admin'),
  ('MD-009','MD',N'공급업체 기준정보 관리',      N'Vendor Master',                  'md/vendor',              'MD-009', 9,1,'admin'),
  ('MD-010','MD',N'고객사 기준정보 관리',        N'Customer Master',                'md/customer',            'MD-010',10,1,'admin'),
  ('MD-011','MD',N'출하처 기준정보 관리',        N'Shipment Destination Master',    'md/shipment-dest',       'MD-011',11,1,'admin'),
  ('MD-012','MD',N'불량유형 기준정보 관리',      N'Defect Code Master',             'md/defect-code',         'MD-012',12,1,'admin'),
  ('MD-013','MD',N'불량원인 기준정보 관리',      N'Defect Cause Master',            'md/defect-cause',        'MD-013',13,1,'admin'),
  ('MD-014','MD',N'설비 기준정보 관리',          N'Equipment Master',               'md/equipment',           'MD-014',14,1,'admin'),
  ('MD-015','MD',N'건조로 기준정보 관리',        N'Oven Master',                    'md/oven',                'MD-015',15,1,'admin'),
  ('MD-016','MD',N'지그 기준정보 관리',          N'Jig Master',                     'md/jig',                 'MD-016',16,1,'admin'),
  ('MD-017','MD',N'검사기준 기준정보 관리',      N'Inspection Standard Master',     'md/inspection-standard', 'MD-017',17,1,'admin'),
  ('MD-018','MD',N'창고/로케이션 기준정보 관리', N'Warehouse Location Master',      'md/location',            'MD-018',18,1,'admin'),
  ('MD-019','MD',N'단위 관리',                   N'UOM Master',                     'md/uom',                 'MD-019',19,1,'admin'),
  ('MD-020','MD',N'RFID 태그 관리',              N'RFID Tag Master',                'md/rfid-tag',            'MD-020',20,1,'admin'),
  ('MD-021','MD',N'RAL 색상 관리',               N'RAL Color Master',               'md/ral-color',           'MD-021',21,1,'admin'),
  ('MD-022','MD',N'RFID 리더 관리',              N'RFID Reader Master',             'md/rfid-reader',         'MD-022',22,1,'admin'),
  ('MD-023','MD',N'포장 사양 관리',              N'Packaging Spec Master',          'md/packaging-spec',      'MD-023',23,1,'admin'),
  ('MD-024','MD',N'라벨 템플릿 관리',            N'Label Template Master',          'md/label-template',      'MD-024',24,1,'admin'),
  ('MD-025','MD',N'코드 기준정보 관리',          N'Common Code Master',             'md/common-code',         'MD-025',25,1,'admin'),
  ('MD-026','MD',N'사유 코드 관리',              N'Reason Code Master',             'md/reason-code',         'MD-026',26,1,'admin'),
  ('MD-027','MD',N'예비품 마스터',               N'Spare Part Master',              'md/spare-part',          'MD-027',27,1,'admin'),
  ('MD-028','MD',N'PM 템플릿 관리',              N'PM Template Master',             'md/pm-template',         'MD-028',28,1,'admin'),
  ('MD-029','MD',N'라인 시간 패턴 관리',         N'Line Time Pattern Master',       'md/line-time-pattern',   'MD-029',29,1,'admin'),
  ('MD-030','MD',N'레시피 관리',                 N'Recipe Master',                  'md/recipe',              'MD-030',30,1,'admin');
GO

-- SYS · 시스템 (SYS-003=화면관리, SYS-004=RBAC)
INSERT INTO dbo.SYS_Screen (ScreenCode,ModuleCode,ScreenName,ScreenNameEn,HRef,LidLabel,SortOrder,IsVisible,CreatedBy) VALUES
  ('SYS-001','SYS',N'사용자 관리',           N'User Management',          'sys/users',         'SYS-001', 1,1,'admin'),
  ('SYS-002','SYS',N'역할 관리',             N'Role Management',          'sys/roles',         'SYS-002', 2,1,'admin'),
  ('SYS-003','SYS',N'화면 관리',             N'Screen Management',        'sys/screen',        'SYS-003', 3,1,'admin'),
  ('SYS-004','SYS',N'역할/권한 관리 (RBAC)', N'Role & Permission (RBAC)', 'sys/rbac',          'SYS-004', 4,1,'admin'),
  ('SYS-005','SYS',N'공장 캘린더',           N'Factory Calendar',         'sys/calendar',      'SYS-005', 5,1,'admin'),
  ('SYS-006','SYS',N'인터페이스 모니터',     N'Interface Monitor',        'sys/interfaces',    'SYS-006', 6,1,'admin'),
  ('SYS-007','SYS',N'감사 로그',             N'Audit Log',                'sys/audit',         'SYS-007', 7,1,'admin'),
  ('SYS-008','SYS',N'알림 관리',             N'Notification Management',  'sys/notifications', 'SYS-008', 8,1,'admin'),
  ('SYS-009','SYS',N'시스템 설정',           N'System Configuration',     'sys/config',        'SYS-009', 9,1,'admin'),
  ('SYS-010','SYS',N'시스템 상태',           N'System Health',            'sys/health',        'SYS-010',10,1,'admin');
GO

SELECT COUNT(*) AS ScreenRows FROM dbo.SYS_Screen;
SELECT ScreenCode, ScreenName, ScreenNameEn FROM dbo.SYS_Screen ORDER BY ModuleCode, SortOrder;
GO
