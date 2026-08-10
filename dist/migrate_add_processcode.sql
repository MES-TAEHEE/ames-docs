-- ════════════════════════════════════════════════════════════════════════
-- migrate_add_processcode.sql
-- SYS_Screen           : ProcessCode 컬럼 추가
-- SYS_AuditLog         : ProcessCode 컬럼 추가
-- SYS_NotificationRule : SourceModule → ModuleCode 이름변경 + ProcessCode 추가
-- 실행 대상 DB : AMES_DEV
--
-- ⚠ 2026-08-03: 세 컬럼 모두 AMES_Schema.sql 에 흡수됨(SYS_RolePermission.ProcessCode 포함).
--    신규 재구축에서는 전부 "already exists, skipped" 로 끝나는 no-op — 기존 DB 보정용으로만 남김.
-- ════════════════════════════════════════════════════════════════════════

-- ── SYS_Screen : ProcessCode 추가 ────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.SYS_Screen')
      AND  name      = 'ProcessCode'
)
BEGIN
    ALTER TABLE dbo.SYS_Screen
        ADD [ProcessCode] VARCHAR(10) NULL;
    PRINT '✓ SYS_Screen.ProcessCode added';
END
ELSE
    PRINT '– SYS_Screen.ProcessCode already exists, skipped';
GO

-- ── SYS_AuditLog : ProcessCode 추가 ─────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.SYS_AuditLog')
      AND  name      = 'ProcessCode'
)
BEGIN
    ALTER TABLE dbo.SYS_AuditLog
        ADD [ProcessCode] VARCHAR(10) NULL;
    PRINT '✓ SYS_AuditLog.ProcessCode added';
END
ELSE
    PRINT '– SYS_AuditLog.ProcessCode already exists, skipped';
GO

-- ── SYS_NotificationRule : SourceModule → ModuleCode ────────────────────
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.SYS_NotificationRule')
      AND  name      = 'SourceModule'
)
BEGIN
    EXEC sp_rename 'dbo.SYS_NotificationRule.SourceModule', 'ModuleCode', 'COLUMN';
    PRINT '✓ SYS_NotificationRule.SourceModule renamed to ModuleCode';
END
ELSE
    PRINT '– SYS_NotificationRule.SourceModule not found (already renamed?), skipped';
GO

-- ── SYS_NotificationRule : ProcessCode 추가 ─────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.SYS_NotificationRule')
      AND  name      = 'ProcessCode'
)
BEGIN
    ALTER TABLE dbo.SYS_NotificationRule
        ADD [ProcessCode] VARCHAR(10) NULL;
    PRINT '✓ SYS_NotificationRule.ProcessCode added';
END
ELSE
    PRINT '– SYS_NotificationRule.ProcessCode already exists, skipped';
GO
