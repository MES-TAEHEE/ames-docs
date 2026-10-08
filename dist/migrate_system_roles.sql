/* ------------------------------------------------------------------
   migrate_system_roles.sql — 기본 역할 ID 고정(ROLE-xxx) + 역할 참조를 ID 로(10-09, 사용자 결정)

   기본 역할 여섯 개의 ID 를 고정한다(AMES.Data.Services.SystemRoles 와 같은 값). 새 DB 에는 이 이름으로 만든다.
     ROLE-SYSADMIN    System Administrator (옛 이름 Admin)       — SYS-001 Admin 전용 동작, POP·PDA·API 관리자 판정
     ROLE-SUPERVISOR  Supervisor                                 — MD-033 라인 책임자 후보, 안돈 슈퍼바이저
     ROLE-OPERATOR    Operator
     ROLE-MAINTENANCE Maintenance Manager  (옛 이름 Maintenance)
     ROLE-QUALITY     Quality Manager      (옛 이름 QC)
     ROLE-PRODUCTION  Production Manager   (옛 이름 Planner)
   이름은 SYS-002 에서 바꿀 수 있고(코드는 ID 로만 판정) 기본 역할은 삭제할 수 없다.
   그 밖의 역할(SYS-002 에서 새로 만든 역할)은 새 GUID 를 받는다.

   §1 역할마다
      · 고정 ID 행이 있으면 건너뜀
      · 기본 이름이나 옛 이름의 역할이 다른 ID(GUID)로 있으면 고정 ID 로 옮긴다 — 옛 행 이름을 잠시 바꿔 두고 새 행을 넣은 뒤
        AspNetUserRoles·AspNetRoleClaims·SYS_RolePermission.RoleID·알림 규칙 수신 역할(JSON 안의 ID)을 새 ID 로 바꾸고
        옛 행을 지운다(외래키가 있는 DB 에서도 깨지지 않는 순서). 쿠키에는 역할 이름만 있어 로그인한 사람은 끊기지 않는다.
      · 둘 다 없으면 기본 이름으로 새로 넣는다(새 DB)
      · RoleID 가 빈 권한 행 중 그 역할 이름인 행에 RoleID 를 채운다
   §2 알림 규칙 SYS_NotificationRule.RecipientRolesJSON 에 남은 역할 이름을 역할 ID 로 바꾼다
      (이미 ID 인 값은 그대로, 어느 역할에도 없는 값은 그대로 두고 목록으로 보고). 순서 유지.
   재실행 안전. 한 트랜잭션. 감사 로그(SYS_AuditLog)에 남은 옛 ID 는 이력이라 건드리지 않는다.
   배포 순서: 이 마이그레이션 → 신 Web·Pop·Api.
   적용: sqlcmd -S <서버> -d AMES_DEV ... -f 65001 -I -b -i dist\migrate_system_roles.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

-- ── §1 고정 ID 역할 ──────────────────────────────────────────────────────
DECLARE @sys TABLE (Seq int, Id nvarchar(450), DefName nvarchar(256), OldName nvarchar(256));
INSERT @sys VALUES
    (1, N'ROLE-SYSADMIN',    N'System Administrator', N'Admin'),
    (2, N'ROLE-SUPERVISOR',  N'Supervisor',           NULL),
    (3, N'ROLE-OPERATOR',    N'Operator',             NULL),
    (4, N'ROLE-MAINTENANCE', N'Maintenance Manager',  N'Maintenance'),
    (5, N'ROLE-QUALITY',     N'Quality Manager',      N'QC'),
    (6, N'ROLE-PRODUCTION',  N'Production Manager',   N'Planner');

DECLARE @seq int = 1, @n int = (SELECT COUNT(*) FROM @sys), @id nvarchar(450), @def nvarchar(256), @oldDef nvarchar(256),
        @old nvarchar(450), @oldName nvarchar(256), @oldNorm nvarchar(256), @oldStamp nvarchar(max);
WHILE @seq <= @n
BEGIN
    SELECT @id = Id, @def = DefName, @oldDef = OldName FROM @sys WHERE Seq = @seq;

    IF EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = @id)
        PRINT CONCAT(N'· ', @id, N' 이미 있음');
    ELSE
    BEGIN
        SET @old = NULL;
        SELECT TOP (1) @old = Id, @oldName = Name, @oldNorm = NormalizedName, @oldStamp = ConcurrencyStamp
        FROM   dbo.AspNetRoles
        WHERE  NormalizedName IN (UPPER(@def), UPPER(@oldDef)) AND Id NOT LIKE N'ROLE-%'
        ORDER  BY CASE WHEN NormalizedName = UPPER(@def) THEN 0 ELSE 1 END, Id;

        IF @old IS NULL
        BEGIN
            INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
            VALUES (@id, @def, UPPER(@def), CONVERT(nvarchar(36), NEWID()));
            PRINT CONCAT(N'✓ ', @id, N' 생성 (', @def, N')');
        END
        ELSE
        BEGIN
            UPDATE dbo.AspNetRoles SET Name = CONCAT(Name, N'~', Id), NormalizedName = CONCAT(NormalizedName, N'~', Id) WHERE Id = @old;
            INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) VALUES (@id, @oldName, @oldNorm, @oldStamp);
            UPDATE dbo.AspNetUserRoles   SET RoleId = @id WHERE RoleId = @old;
            PRINT CONCAT(N'  AspNetUserRoles ', @@ROWCOUNT, N'건');
            UPDATE dbo.AspNetRoleClaims  SET RoleId = @id WHERE RoleId = @old;
            UPDATE dbo.SYS_RolePermission SET RoleID = @id WHERE RoleID = @old;
            PRINT CONCAT(N'  SYS_RolePermission ', @@ROWCOUNT, N'건');
            UPDATE dbo.SYS_NotificationRule
            SET    RecipientRolesJSON = REPLACE(RecipientRolesJSON, N'"' + @old + N'"', N'"' + @id + N'"')
            WHERE  RecipientRolesJSON LIKE N'%"' + @old + N'"%';
            PRINT CONCAT(N'  SYS_NotificationRule ', @@ROWCOUNT, N'건');
            DELETE dbo.AspNetRoles WHERE Id = @old;
            PRINT CONCAT(N'✓ ', @oldName, N' : ', @old, N' → ', @id);
        END
    END

    UPDATE p SET RoleID = r.Id
    FROM   dbo.SYS_RolePermission p JOIN dbo.AspNetRoles r ON r.Id = @id AND r.Name = p.RoleName
    WHERE  p.RoleID IS NULL;

    SET @seq += 1;
END

-- ── §2 알림 규칙 수신 역할: 이름 → ID ─────────────────────────────────────
DECLARE @conv TABLE (NotificationRuleID int PRIMARY KEY, NewJson nvarchar(max));
INSERT @conv (NotificationRuleID, NewJson)
SELECT r.NotificationRuleID,
       N'[' + STRING_AGG(CAST(N'"' + STRING_ESCAPE(COALESCE(byId.Id, byName.Id, j.value), 'json') + N'"' AS nvarchar(max)), N',')
              WITHIN GROUP (ORDER BY CAST(j.[key] AS int)) + N']'
FROM   dbo.SYS_NotificationRule r
CROSS APPLY OPENJSON(r.RecipientRolesJSON) j
OUTER APPLY (SELECT TOP (1) a.Id FROM dbo.AspNetRoles a WHERE a.Id = j.value) byId
OUTER APPLY (SELECT TOP (1) a.Id FROM dbo.AspNetRoles a WHERE a.NormalizedName = UPPER(j.value) ORDER BY a.Id) byName
WHERE  ISJSON(r.RecipientRolesJSON) = 1
GROUP  BY r.NotificationRuleID;

IF EXISTS (SELECT 1 FROM @conv WHERE LEN(NewJson) > 500)
    THROW 50001, 'RecipientRolesJSON would exceed nvarchar(500). No changes applied.', 1;

UPDATE r SET RecipientRolesJSON = c.NewJson
FROM   dbo.SYS_NotificationRule r JOIN @conv c ON c.NotificationRuleID = r.NotificationRuleID
WHERE  r.RecipientRolesJSON <> c.NewJson;
PRINT CONCAT(N'✓ 알림 규칙 수신 역할 이름 → ID: ', @@ROWCOUNT, N'건');

COMMIT;
GO

SELECT Id, Name FROM dbo.AspNetRoles WHERE Id LIKE N'ROLE-%' ORDER BY Id;
SELECT r.NotificationRuleID, j.value AS UnknownRole
FROM   dbo.SYS_NotificationRule r CROSS APPLY OPENJSON(r.RecipientRolesJSON) j
WHERE  ISJSON(r.RecipientRolesJSON) = 1 AND NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles a WHERE a.Id = j.value);
