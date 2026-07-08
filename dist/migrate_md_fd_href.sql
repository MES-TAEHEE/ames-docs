-- MD-003, MD-004, MD-019 HRef 경로 변경: md/* → md/fd/*
-- 적용 대상: AMES_DEV.dbo.SYS_Screen
-- 작성일: 2026-07-08

UPDATE dbo.SYS_Screen SET HRef = 'md/fd/items' WHERE ScreenCode = 'MD-003';
UPDATE dbo.SYS_Screen SET HRef = 'md/fd/bom'   WHERE ScreenCode = 'MD-004';
UPDATE dbo.SYS_Screen SET HRef = 'md/fd/uom'   WHERE ScreenCode = 'MD-019';

-- 확인
SELECT ScreenCode, HRef FROM dbo.SYS_Screen
WHERE ScreenCode IN ('MD-003','MD-004','MD-019')
ORDER BY ScreenCode;
