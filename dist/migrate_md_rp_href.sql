-- MD-001, MD-002, MD-005, MD-006, MD-028, MD-029 HRef 경로 변경: md/* → md/rp/*
-- 적용 대상: AMES_DEV.dbo.SYS_Screen
-- 작성일: 2026-07-08

UPDATE dbo.SYS_Screen SET HRef = 'md/rp/line'              WHERE ScreenCode = 'MD-001';
UPDATE dbo.SYS_Screen SET HRef = 'md/rp/station'           WHERE ScreenCode = 'MD-002';
UPDATE dbo.SYS_Screen SET HRef = 'md/rp/bop'               WHERE ScreenCode = 'MD-005';
UPDATE dbo.SYS_Screen SET HRef = 'md/rp/work-center'       WHERE ScreenCode = 'MD-006';
UPDATE dbo.SYS_Screen SET HRef = 'md/rp/pm-template'       WHERE ScreenCode = 'MD-028';
UPDATE dbo.SYS_Screen SET HRef = 'md/rp/line-time-pattern' WHERE ScreenCode = 'MD-029';

-- 확인
SELECT ScreenCode, HRef FROM dbo.SYS_Screen
WHERE ScreenCode IN ('MD-001','MD-002','MD-005','MD-006','MD-028','MD-029')
ORDER BY ScreenCode;
