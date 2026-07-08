-- MD-007, MD-009, MD-014, MD-015, MD-016, MD-026, MD-027 HRef 경로 변경: md/* → md/re/*
-- 적용 대상: AMES_DEV.dbo.SYS_Screen
-- 작성일: 2026-07-09

UPDATE dbo.SYS_Screen SET HRef = 'md/re/mold'        WHERE ScreenCode = 'MD-007';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/vendor'       WHERE ScreenCode = 'MD-009';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/equipment'    WHERE ScreenCode = 'MD-014';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/oven'         WHERE ScreenCode = 'MD-015';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/jig'          WHERE ScreenCode = 'MD-016';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/reason-code'  WHERE ScreenCode = 'MD-026';
UPDATE dbo.SYS_Screen SET HRef = 'md/re/spare-part'   WHERE ScreenCode = 'MD-027';

-- 확인
SELECT ScreenCode, HRef FROM dbo.SYS_Screen
WHERE ScreenCode IN ('MD-007','MD-009','MD-014','MD-015','MD-016','MD-026','MD-027')
ORDER BY ScreenCode;
