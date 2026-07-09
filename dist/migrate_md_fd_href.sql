-- MD Fd 화면 HRef 경로 변경: md/* → md/fd/*
-- MD-003 (Items), MD-004 (BOM), MD-018 (Location), MD-019 (UOM)
-- 적용 대상: AMES_DEV.dbo.SYS_Screen

USE AMES_DEV;
GO

UPDATE dbo.SYS_Screen SET HRef = 'md/fd/items'    WHERE ScreenCode = 'MD-003';
UPDATE dbo.SYS_Screen SET HRef = 'md/fd/bom'      WHERE ScreenCode = 'MD-004';
UPDATE dbo.SYS_Screen SET HRef = 'md/fd/location' WHERE ScreenCode = 'MD-018' AND HRef IN ('md/location','md/ql/location');
UPDATE dbo.SYS_Screen SET HRef = 'md/fd/uom'      WHERE ScreenCode = 'MD-019';

-- SubProcessCode 동기화
UPDATE dbo.SYS_Screen SET SubProcessCode = 'Fd'
WHERE  ProcessCode = 'MD' AND HRef LIKE 'md/fd/%';

-- 확인
SELECT ScreenCode, SubProcessCode, HRef FROM dbo.SYS_Screen
WHERE  ScreenCode IN ('MD-003','MD-004','MD-018','MD-019')
ORDER  BY ScreenCode;
GO
