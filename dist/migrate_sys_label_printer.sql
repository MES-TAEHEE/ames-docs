-- ════════════════════════════════════════════════════════════════════════
--  migrate_sys_label_printer.sql
--  라벨 프린터 마스터 SYS_LabelPrinter 와 관리 화면 SYS-011(sys/label-printers) — 11-09 사용자 결정
--
--  웹 서버가 ZPL 을 바로 보내는 네트워크 라벨 프린터 목록(여러 대). MD-034 팔레트 라벨 출력 창의
--  「서버에서 네트워크 프린터로 출력」 방식이 사용 중인 프린터를 목록으로 보이고, PC 마다 마지막에 고른 프린터를 기억한다.
--  Address = 172.29.4.10 · 172.29.4.10:9100 · Tcp:host:port(네트워크 Zebra, 포트 생략 9100) · Spooler:프린터 이름(웹 서버 PC 에 설치된 프린터)
--  외래키 없음(프린터를 참조하는 테이블 없음 — 고른 프린터는 브라우저에만 기억). 이름은 유일.
--  순서 무관, 재실행 안전.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES -C -f 65001 -I -b -i dist\migrate_sys_label_printer.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.SYS_LabelPrinter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SYS_LabelPrinter (
        PrinterID   int IDENTITY(1,1) NOT NULL,
        PrinterName nvarchar(50)  COLLATE DATABASE_DEFAULT NOT NULL,
        Address     varchar(100)  COLLATE DATABASE_DEFAULT NOT NULL,
        Location    nvarchar(100) COLLATE DATABASE_DEFAULT NULL,
        Remark      nvarchar(200) COLLATE DATABASE_DEFAULT NULL,
        ActiveFlag  bit NOT NULL CONSTRAINT DF_SYS_LabelPrinter_ActiveFlag DEFAULT (1),
        CreatedBy   varchar(20)   COLLATE DATABASE_DEFAULT NULL,
        CreatedTS   datetime2(7)  NOT NULL CONSTRAINT DF_SYS_LabelPrinter_CreatedTS DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(20)   COLLATE DATABASE_DEFAULT NULL,
        ModifiedTS  datetime2(7)  NULL,
        CONSTRAINT PK_SYS_LabelPrinter PRIMARY KEY CLUSTERED (PrinterID)
    );
    CREATE UNIQUE INDEX UX_SYS_LabelPrinter_Name ON dbo.SYS_LabelPrinter (PrinterName);

    EXEC sys.sp_addextendedproperty N'MS_Description', N'라벨 프린터 마스터 (SYS-011) — 웹 서버가 ZPL 을 보내는 네트워크 라벨 프린터',
         N'SCHEMA', N'dbo', N'TABLE', N'SYS_LabelPrinter';
    DECLARE @d TABLE (c sysname, t nvarchar(400));
    INSERT @d VALUES
        (N'PrinterID',   N'프린터 ID (자동)'),
        (N'PrinterName', N'프린터 이름 — 출력 창 목록에 보임, 유일'),
        (N'Address',     N'주소 — host[:port](포트 생략 9100) · Tcp:host:port · Spooler:웹 서버 PC 프린터 이름'),
        (N'Location',    N'설치 위치 메모'),
        (N'Remark',      N'비고'),
        (N'ActiveFlag',  N'사용 여부 — 끄면 출력 창 목록에서 빠짐');
    DECLARE @c sysname, @t nvarchar(400);
    DECLARE dc CURSOR LOCAL FAST_FORWARD FOR SELECT c, t FROM @d;
    OPEN dc; FETCH NEXT FROM dc INTO @c, @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC sys.sp_addextendedproperty N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'SYS_LabelPrinter', N'COLUMN', @c;
        FETCH NEXT FROM dc INTO @c, @t;
    END
    CLOSE dc; DEALLOCATE dc;
    PRINT N'✓ SYS_LabelPrinter 생성';
END
ELSE PRINT N'· SYS_LabelPrinter 이미 있음';
GO

-- 화면 등록 — SYS 마지막 다음(승인 기능 없음 __X)
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'SYS-011')
BEGIN
    DECLARE @after int = ISNULL((SELECT MAX(SortOrder) FROM dbo.SYS_Screen WHERE ProcessCode = 'SYS'), 0);
    INSERT dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel,
                           PermissionCriteria, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES ('SYS-011', 'WEB', 'SYS', NULL, N'라벨 프린터 관리', N'Label Printer Management', 'sys/label-printers', 'SYS-011',
            '__X', @after + 1, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen SYS-011 (sys/label-printers)';
END
GO

DECLARE @AdminRoleName varchar(40) = ISNULL((SELECT Name FROM dbo.AspNetRoles WHERE Id = N'ROLE-SYSADMIN'), 'System Administrator');
INSERT dbo.SYS_RolePermission (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
SELECT N'ROLE-SYSADMIN', @AdminRoleName, 'WEB', 'SYS-011', 'REX', 1, SYSDATETIME(), 'seed', SYSDATETIME()
WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleID = N'ROLE-SYSADMIN' AND p.ScreenCode = 'SYS-011');
GO

SELECT ScreenCode, HRef, SortOrder, PermissionCriteria FROM dbo.SYS_Screen WHERE ScreenCode = 'SYS-011';
SELECT COUNT(*) AS Printers FROM dbo.SYS_LabelPrinter;
GO
