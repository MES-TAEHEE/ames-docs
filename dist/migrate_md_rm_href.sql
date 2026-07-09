-- MD Rm 화면 HRef 경로 마이그레이션
-- MD-008 (PaintFabric), MD-020 (RfidTag), MD-021 (RalColor), MD-022 (RfidReader)
-- 이전 경로: md/paint-fabric, md/rfid-tag, md/ral-color, md/rfid-reader
-- 변경 경로: md/rm/paint-fabric, md/rm/rfid-tag, md/rm/ral-color, md/rm/rfid-reader

USE AMES_DEV;
GO

UPDATE dbo.SYS_Screen
SET    HRef = 'md/rm/paint-fabric'
WHERE  ScreenCode = 'MD-008'
  AND  HRef = 'md/paint-fabric';

UPDATE dbo.SYS_Screen
SET    HRef = 'md/rm/rfid-tag'
WHERE  ScreenCode = 'MD-020'
  AND  HRef = 'md/rfid-tag';

UPDATE dbo.SYS_Screen
SET    HRef = 'md/rm/ral-color'
WHERE  ScreenCode = 'MD-021'
  AND  HRef = 'md/ral-color';

UPDATE dbo.SYS_Screen
SET    HRef = 'md/rm/rfid-reader'
WHERE  ScreenCode = 'MD-022'
  AND  HRef = 'md/rfid-reader';

-- SYS_RolePermission 동기화 (ScreenCode 기준이므로 HRef 변경 불필요, 확인용)
PRINT '✓ MD-008/020/021/022 HRef → md/rm/* 업데이트 완료';
GO
