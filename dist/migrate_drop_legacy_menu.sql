-- ════════════════════════════════════════════════════════════════════════
-- migrate_drop_legacy_menu.sql — 레거시 메뉴 테이블 정리
--   MD_Menu / MD_MenuRole 은 SYS_Screen(+SYS_RolePermission)으로 완전 대체됨.
--   애플리케이션 코드(src) 참조 0건 — 데이터만 잔존하는 죽은 테이블이라 DROP.
--   FK: MD_MenuRole → MD_Menu 이므로 MenuRole 을 먼저 삭제.
-- idempotent. 적용: sqlcmd -f 65001 -b -i dist/migrate_drop_legacy_menu.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
IF OBJECT_ID('dbo.MD_MenuRole', 'U') IS NOT NULL
BEGIN DROP TABLE dbo.MD_MenuRole; PRINT N'✓ MD_MenuRole 삭제'; END
ELSE PRINT N'· MD_MenuRole 없음';
GO
IF OBJECT_ID('dbo.MD_Menu', 'U') IS NOT NULL
BEGIN DROP TABLE dbo.MD_Menu; PRINT N'✓ MD_Menu 삭제'; END
ELSE PRINT N'· MD_Menu 없음';
GO
