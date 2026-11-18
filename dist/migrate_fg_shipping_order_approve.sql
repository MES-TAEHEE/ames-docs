-- ════════════════════════════════════════════════════════════════════════
--  migrate_fg_shipping_order_approve.sql  (2026-11-17)
--  FG 출하 오더(fg/shipping-orders, 화면 코드 FG-05) — 승인(A) 기능 추가
--
--  출하 오더 생성 때 재고 검사를 넣으면서, 재고가 부족해도 승인 권한자가 사유와 함께 승인하면 만들 수 있게 했다
--  (감사 로그 FG-05/CREATE_OVERRIDE). 그 승인 권한을 쓰려면 화면 기본 권한에 승인 기능이 있어야 한다:
--    · SYS_Screen.PermissionCriteria  __X → ___  (승인 기능 있음)
--    · System Administrator 의 이 화면 권한 REX → REA (다른 역할은 SYS-004 에서 승인을 열어 준다)
--  데이터만 바꾼다(스키마 변경 없음). 재실행 안전.
--  적용:  sqlcmd -S <서버> -U ames_app -d <DB> -f 65001 -I -b -i dist/migrate_fg_shipping_order_approve.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria') IS NOT NULL
    EXEC(N'UPDATE dbo.SYS_Screen SET PermissionCriteria = ''___'' WHERE HRef = ''fg/shipping-orders'' AND PermissionCriteria = ''__X''');
GO

UPDATE rp
   SET rp.PermissionLevel = 'REA'
  FROM dbo.SYS_RolePermission rp
  JOIN dbo.SYS_Screen s ON s.ScreenCode = rp.ScreenCode AND s.HRef = 'fg/shipping-orders'
 WHERE rp.RoleID = N'ROLE-SYSADMIN' AND rp.PermissionLevel IN ('REX', 'RXX', 'R_X');
GO

SELECT s.ScreenCode, s.HRef, s.PermissionCriteria, rp.RoleName, rp.PermissionLevel
  FROM dbo.SYS_Screen s LEFT JOIN dbo.SYS_RolePermission rp ON rp.ScreenCode = s.ScreenCode
 WHERE s.HRef = 'fg/shipping-orders';
GO
