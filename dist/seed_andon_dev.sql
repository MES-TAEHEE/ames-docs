-- ════════════════════════════════════════════════════════════════════════
--  seed_andon_dev.sql
--  MD_LineSupervisor 개발 시드 — 활성 라인 × Supervisor 역할 웹 사용자(SYS_UserProfile + AspNetUserRoles)
--
--  · 기존 행은 전부 지우고 다시 만든다(역할 기준으로 재생성). 운영 금지 — 운영은 MD-033 화면에서 라인별로 넣는다.
--  · 개발 편의: Supervisor 역할 보유자가 한 명도 없으면 S001(Supervisor Choi)에 역할을 준다.
--  전제: migrate_andon_workflow.sql · migrate_employee_no_rename.sql 적용 후. 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/seed_andon_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('dbo.MD_LineSupervisor', 'U') IS NULL OR COL_LENGTH('dbo.MD_LineSupervisor', 'EmployeeNo') IS NULL
BEGIN
    RAISERROR('MD_LineSupervisor(EmployeeNo) 가 없습니다. dist/migrate_andon_workflow.sql · migrate_employee_no_rename.sql 을 먼저 적용하세요.', 16, 1);
    RETURN;
END
GO

-- ── 1) 개발 편의: Supervisor 역할 보유자가 없으면 S001 에 부여 ───────────────
DECLARE @role nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Supervisor');
IF @role IS NULL
BEGIN
    RAISERROR('AspNetRoles 에 Supervisor 역할이 없습니다.', 16, 1);
    RETURN;
END
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE RoleId = @role)
BEGIN
    DECLARE @s001 nvarchar(450) = (SELECT TOP 1 UserID FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'S001');
    IF @s001 IS NOT NULL
    BEGIN
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (@s001, @role);
        PRINT 'S001 <- Supervisor role (dev seed)';
    END
END
GO

-- ── 2) 활성 라인 × Supervisor 역할 사용자 로 재생성 ─────────────────────────
BEGIN TRANSACTION;
DELETE FROM dbo.MD_LineSupervisor;
INSERT INTO dbo.MD_LineSupervisor (LineID, EmployeeNo, ActiveFlag, CreatedBy, CreatedTS)
SELECT l.LineID, u.EmployeeNo, 1, 'seed', SYSDATETIME()
FROM   dbo.MD_Line l
CROSS JOIN (SELECT DISTINCT p.EmployeeNo
            FROM   dbo.SYS_UserProfile p
            JOIN   dbo.AspNetUserRoles ur ON ur.UserId = p.UserID
            JOIN   dbo.AspNetRoles     r  ON r.Id = ur.RoleId AND r.Name = 'Supervisor'
            WHERE  p.EmployeeNo IS NOT NULL AND p.EmployeeNo <> '') u
WHERE  COALESCE(l.Status, 'ACTIVE') <> 'INACTIVE';
PRINT CONCAT('MD_LineSupervisor rebuilt: ', @@ROWCOUNT, ' rows');
COMMIT TRANSACTION;
GO

SELECT s.LineID, s.EmployeeNo, p.EmployeeName, s.ActiveFlag
FROM   dbo.MD_LineSupervisor s
LEFT JOIN dbo.SYS_UserProfile p ON p.EmployeeNo = s.EmployeeNo
ORDER BY s.LineID, s.EmployeeNo;
GO
