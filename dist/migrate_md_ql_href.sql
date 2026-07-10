-- MD Ql 화면 HRef 경로 마이그레이션
-- MD-010/011/012/013/017/023/024/025/030
-- 이전 경로: md/customer, md/shipment-dest 등
-- 변경 경로: md/ql/customer, md/ql/shipment-dest 등

USE AMES_DEV;
GO

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/customer'
WHERE ScreenCode = 'MD-010' AND HRef = 'md/customer';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/shipment-dest'
WHERE ScreenCode = 'MD-011' AND HRef = 'md/shipment-dest';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/defect-code'
WHERE ScreenCode = 'MD-012' AND HRef = 'md/defect-code';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/defect-cause'
WHERE ScreenCode = 'MD-013' AND HRef = 'md/defect-cause';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/inspection-standard'
WHERE ScreenCode = 'MD-017' AND HRef = 'md/inspection-standard';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/packaging-spec'
WHERE ScreenCode = 'MD-023' AND HRef = 'md/packaging-spec';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/label-template'
WHERE ScreenCode = 'MD-024' AND HRef = 'md/label-template';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/common-code'
WHERE ScreenCode = 'MD-025' AND HRef = 'md/common-code';

UPDATE dbo.SYS_Screen SET HRef = 'md/ql/recipe'
WHERE ScreenCode = 'MD-030' AND HRef = 'md/recipe';

PRINT '✓ MD-010/011/012/013/017/023/024/025/030 HRef → md/ql/* 업데이트 완료';
GO
