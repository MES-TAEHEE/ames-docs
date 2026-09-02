-- =============================================================================
-- seed_work_shift_time.sql
-- WORK_SHIFT 공통코드(A/B/C)에 교대 기본 시간대를 Attribute1='HHMM-HHMM' 형식으로 시드.
-- MD-28 LineTimePattern 편집기의 'Shift 프리셋 밴드' 기준값.
-- 가드: 값이 비어있을 때만 설정(이미 지정된 시간대는 보존). CommonCode 화면 Attr1에서 수정 가능.
-- =============================================================================
SET NOCOUNT ON;

UPDATE dbo.MD_CodeItem SET Attribute1 = '0800-1600'
 WHERE GroupCode = 'WORK_SHIFT' AND CodeValue = 'A' AND (Attribute1 IS NULL OR Attribute1 = '');

UPDATE dbo.MD_CodeItem SET Attribute1 = '1600-2400'
 WHERE GroupCode = 'WORK_SHIFT' AND CodeValue = 'B' AND (Attribute1 IS NULL OR Attribute1 = '');

UPDATE dbo.MD_CodeItem SET Attribute1 = '0000-0800'
 WHERE GroupCode = 'WORK_SHIFT' AND CodeValue = 'C' AND (Attribute1 IS NULL OR Attribute1 = '');

PRINT N'✓ WORK_SHIFT 기본 시간대(Attribute1) 시드 완료';
GO
