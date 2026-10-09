/* ------------------------------------------------------------------
   migrate_sys_screen_permission_criteria.sql — 화면 기본 권한 SYS_Screen.PermissionCriteria (10-10 사용자 결정)
   ① SYS_Screen 에 PermissionCriteria VARCHAR(10) NOT NULL DEFAULT '___' 를 LidLabel 바로 다음에 둔다(컬럼 순서 때문에 재생성 —
      ScreenID·IDENTITY 현재값·PK_SYS_Screen·UQ_SYS_Screen 유지, 기본값 제약은 DF_SYS_Screen_* 이름으로).
      값 = 그 화면에 있는 기능, 자리 순서 R(읽기)·E(수정)·A(승인), 각 '_'(있음) / 'X'(없음), 읽기는 항상 '_'.
      처음 적용할 때만 아래 목록으로 채운다(웹 화면의 CanEdit/CanApprove 조사 — 조회 전용 28 · 수정까지 49 · 승인까지 12,
      목록 밖 화면(포탈 등)은 '___'). 그 뒤로는 SYS-003 에서 관리한다.
   ② 승인 권한(A)으로 옮긴 동작 — MD-004 BOM 승인·반려, PP-006 PO 승인, PP-002 수주 확정, PP-004 WO 릴리스·취소, PP-LSB 발행,
      PP-APS WO 생성, MNT-002 수리 완료, MNT-007 작업지시 완료, MNT-005·010 PM 완료. 처음 적용할 때 그 화면에서 E 를 가진 역할에 A 를 같이 줘
      종전 동작(수정 권한으로 하던 것)을 잇는다.
   ③ SYS_RolePermission.PermissionLevel 을 화면 기본 권한에 맞춰 3자리로 — 자리마다 부여 글자 · '_'(미부여) · 'X'(기능 없음).
      옛 형식('REA' 'RE' 'R')도 바꾼다. 남는 부여가 없는 행은 지운다. 매번 다시 맞추므로 재실행 안전.
   (10-10 오전의 migrate_role_permission_slots.sql 을 대신한다 — 그 파일이 적용된 DB 도 그대로 맞춰진다.)
   권한 판정(PermissionService)은 R/E/A 글자만 읽는다 — 신 Web 은 SYS_Screen.PermissionCriteria 를 읽으므로 이 마이그레이션 뒤에 올린다.
   적용: sqlcmd -f 65001 -I -b -d AMES_DEV -i dist\migrate_sys_screen_permission_criteria.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID('tempdb..#ScreenPermRun') IS NOT NULL DROP TABLE #ScreenPermRun;
CREATE TABLE #ScreenPermRun (Added bit NOT NULL);
GO

-- ⓪ 10-10 첫판(PermissionLevel VARCHAR(3))이 적용된 DB — 이름을 PermissionCriteria 로 바꾸고 VARCHAR(10) 으로 넓힌다(값 유지)
IF COL_LENGTH('dbo.SYS_Screen', 'PermissionLevel') IS NOT NULL AND COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria') IS NULL
BEGIN
    BEGIN TRAN;
    IF OBJECT_ID(N'dbo.DF_SYS_Screen_PermissionLevel', N'D') IS NOT NULL
        ALTER TABLE dbo.SYS_Screen DROP CONSTRAINT DF_SYS_Screen_PermissionLevel;
    EXEC sp_rename N'dbo.SYS_Screen.PermissionLevel', N'PermissionCriteria', N'COLUMN';
    EXEC (N'ALTER TABLE dbo.SYS_Screen ALTER COLUMN PermissionCriteria varchar(10) NOT NULL;
           ALTER TABLE dbo.SYS_Screen ADD CONSTRAINT DF_SYS_Screen_PermissionCriteria DEFAULT (''___'') FOR PermissionCriteria;');
    COMMIT;
    PRINT N'SYS_Screen.PermissionLevel → PermissionCriteria VARCHAR(10)';
END
GO

-- ① 컬럼 추가(재생성) — 이미 있으면 건너뛴다
IF COL_LENGTH('dbo.SYS_Screen', 'PermissionCriteria') IS NULL
BEGIN
    BEGIN TRAN;
    DECLARE @ident bigint = IDENT_CURRENT('dbo.SYS_Screen');
    EXEC sp_rename N'dbo.SYS_Screen', N'SYS_Screen_Old';
    EXEC sp_rename N'dbo.PK_SYS_Screen', N'PK_SYS_Screen_Old', N'OBJECT';
    EXEC sp_rename N'dbo.UQ_SYS_Screen', N'UQ_SYS_Screen_Old', N'OBJECT';
    -- 이 마이그레이션이 붙이는 기본값 제약 이름이 옛 표에 이미 있으면(재생성된 적 있는 표) 비켜 둔다 — 옛 표와 함께 지워진다
    IF OBJECT_ID(N'dbo.DF_SYS_Screen_IsVisible', N'D') IS NOT NULL EXEC sp_rename N'dbo.DF_SYS_Screen_IsVisible', N'DF_SYS_Screen_IsVisible_Old', N'OBJECT';
    IF OBJECT_ID(N'dbo.DF_SYS_Screen_CreatedTS', N'D') IS NOT NULL EXEC sp_rename N'dbo.DF_SYS_Screen_CreatedTS', N'DF_SYS_Screen_CreatedTS_Old', N'OBJECT';
    EXEC (N'
    CREATE TABLE dbo.SYS_Screen (
        ScreenID        int IDENTITY(1,1) NOT NULL,
        ScreenCode      varchar(20)    NOT NULL,
        ModuleCode      varchar(10)    NOT NULL,
        ProcessCode     varchar(10)    NULL,
        SubProcessCode  varchar(10)    NULL,
        ScreenName      nvarchar(100)  NOT NULL,
        ScreenNameEn    nvarchar(100)  NULL,
        HRef            varchar(200)   NULL,
        LidLabel        varchar(20)    NULL,
        PermissionCriteria varchar(10) NOT NULL CONSTRAINT DF_SYS_Screen_PermissionCriteria DEFAULT (''___''),
        SortOrder       int            NULL,
        IsVisible       bit            NULL CONSTRAINT DF_SYS_Screen_IsVisible DEFAULT ((1)),
        CreatedBy       varchar(20)    NOT NULL,
        CreatedTS       datetime2(7)   NULL CONSTRAINT DF_SYS_Screen_CreatedTS DEFAULT (sysdatetime()),
        ModifiedBy      varchar(20)    NULL,
        ModifiedTS      datetime2(7)   NULL,
        CONSTRAINT PK_SYS_Screen PRIMARY KEY CLUSTERED (ScreenID),
        CONSTRAINT UQ_SYS_Screen UNIQUE NONCLUSTERED (ScreenCode)
    );
    SET IDENTITY_INSERT dbo.SYS_Screen ON;
    INSERT dbo.SYS_Screen (ScreenID, ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel,
                           SortOrder, IsVisible, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT ScreenID, ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel,
           SortOrder, IsVisible, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.SYS_Screen_Old;
    SET IDENTITY_INSERT dbo.SYS_Screen OFF;
    DROP TABLE dbo.SYS_Screen_Old;');
    IF @ident IS NOT NULL DBCC CHECKIDENT ('dbo.SYS_Screen', RESEED, @ident) WITH NO_INFOMSGS;
    COMMIT;
    INSERT #ScreenPermRun VALUES (1);
    PRINT N'SYS_Screen.PermissionCriteria 추가(LidLabel 다음)';
END
ELSE PRINT N'SYS_Screen.PermissionCriteria 이미 있음 — 화면 기본 권한은 건드리지 않음';
GO

-- ①-2 처음 적용할 때만 화면 기본 권한 채움
IF EXISTS (SELECT 1 FROM #ScreenPermRun)
BEGIN
    UPDATE s
    SET    PermissionCriteria = f.Tpl
    FROM   dbo.SYS_Screen s
    JOIN   (VALUES
    ('FG-01', '_XX'),
    ('FG-02', '_XX'),
    ('FG-03', '_XX'),
    ('FG-04', '_XX'),
    ('MD-001', '__X'),
    ('MD-002', '__X'),
    ('MD-003', '__X'),
    ('MD-004', '___'),
    ('MD-005', '__X'),
    ('MD-006', '__X'),
    ('MD-007', '__X'),
    ('MD-008', '__X'),
    ('MD-009', '__X'),
    ('MD-010', '__X'),
    ('MD-011', '__X'),
    ('MD-012', '__X'),
    ('MD-013', '__X'),
    ('MD-014', '__X'),
    ('MD-015', '__X'),
    ('MD-016', '__X'),
    ('MD-017', '__X'),
    ('MD-018', '__X'),
    ('MD-019', '__X'),
    ('MD-020', '__X'),
    ('MD-021', '__X'),
    ('MD-022', '__X'),
    ('MD-023', '__X'),
    ('MD-024', '__X'),
    ('MD-025', '__X'),
    ('MD-026', '__X'),
    ('MD-027', '__X'),
    ('MD-028', '__X'),
    ('MD-029', '__X'),
    ('MD-030', '__X'),
    ('MD-031', '__X'),
    ('MD-032', '__X'),
    ('MD-033', '__X'),
    ('MNT-001', '__X'),
    ('MNT-002', '___'),
    ('MNT-003', '_XX'),
    ('MNT-004', '_XX'),
    ('MNT-005', '___'),
    ('MNT-006', '_XX'),
    ('MNT-007', '___'),
    ('MNT-008', '_XX'),
    ('MNT-009', '_XX'),
    ('MNT-010', '___'),
    ('PP-001', '__X'),
    ('PP-002', '___'),
    ('PP-003', '__X'),
    ('PP-004', '___'),
    ('PP-005', '__X'),
    ('PP-006', '___'),
    ('PP-APS', '___'),
    ('PP-CAL', '__X'),
    ('PP-DTL', '__X'),
    ('PP-LSB', '___'),
    ('PP-ODM', '_XX'),
    ('PP-OEE', '__X'),
    ('PP-OTD', '_XX'),
    ('RPT-001', '_XX'),
    ('RPT-002', '_XX'),
    ('RPT-003', '_XX'),
    ('RPT-004', '_XX'),
    ('RPT-005', '_XX'),
    ('RPT-006', '_XX'),
    ('RPT-007', '_XX'),
    ('RPT-008', '_XX'),
    ('RPT-009', '_XX'),
    ('RPT-010', '_XX'),
    ('SCM-001', '___'),
    ('SCM-002', '___'),
    ('SCM-003', '__X'),
    ('SCM-004', '__X'),
    ('SYS-001', '__X'),
    ('SYS-002', '__X'),
    ('SYS-003', '__X'),
    ('SYS-004', '__X'),
    ('SYS-005', '__X'),
    ('SYS-006', '_XX'),
    ('SYS-007', '_XX'),
    ('SYS-008', '__X'),
    ('SYS-009', '__X'),
    ('SYS-010', '_XX'),
    ('WH-01', '_XX'),
    ('WH-02', '__X'),
    ('WH-03', '_XX'),
    ('WH-04', '_XX'),
    ('WH-05', '_XX')
           ) f (ScreenCode, Tpl) ON f.ScreenCode = s.ScreenCode;
    PRINT CONCAT(N'화면 기본 권한 채움: ', @@ROWCOUNT, N' 화면');
END
GO

-- ②·③ 역할 권한을 화면 기본 권한에 맞춘다
BEGIN TRAN;
;WITH src AS (
    SELECT rp.RolePermissionID, rp.PermissionLevel,
           ISNULL(s.PermissionCriteria, '___') AS Tpl,
           UPPER(rp.PermissionLevel)
         + CASE WHEN EXISTS (SELECT 1 FROM #ScreenPermRun)
                 AND rp.ScreenCode IN ('MD-004', 'PP-006', 'PP-002', 'PP-004', 'PP-LSB', 'PP-APS', 'MNT-002', 'MNT-005', 'MNT-007', 'MNT-010')
                 AND CHARINDEX('E', UPPER(rp.PermissionLevel)) > 0
                THEN 'A' ELSE '' END AS Granted
    FROM   dbo.SYS_RolePermission rp
    LEFT   JOIN dbo.SYS_Screen s ON s.ScreenCode = rp.ScreenCode
    WHERE  ISNULL(rp.PermissionLevel, '') <> ''
), calc AS (
    SELECT RolePermissionID, PermissionLevel,
           CASE WHEN CHARINDEX('R', Granted) > 0 THEN 'R' ELSE '_' END
         + CASE WHEN SUBSTRING(Tpl, 2, 1) = 'X' THEN 'X' WHEN CHARINDEX('E', Granted) > 0 THEN 'E' ELSE '_' END
         + CASE WHEN SUBSTRING(Tpl, 3, 1) = 'X' THEN 'X' WHEN CHARINDEX('A', Granted) > 0 THEN 'A' ELSE '_' END AS NewLevel
    FROM   src
)
UPDATE p
SET    PermissionLevel = c.NewLevel,
       ModifiedBy      = 'PERM-SLOT-1010',
       ModifiedTS      = SYSDATETIME()
FROM   dbo.SYS_RolePermission p
JOIN   calc c ON c.RolePermissionID = p.RolePermissionID
WHERE  c.PermissionLevel COLLATE Latin1_General_BIN <> c.NewLevel COLLATE Latin1_General_BIN;
PRINT CONCAT(N'역할 권한 맞춤: ', @@ROWCOUNT, N' 행');

DELETE dbo.SYS_RolePermission WHERE ISNULL(PermissionLevel, '') COLLATE Latin1_General_BIN NOT LIKE '%[REA]%';
PRINT CONCAT(N'부여 없는 역할 권한 삭제: ', @@ROWCOUNT, N' 행');
COMMIT;
GO

SELECT PermissionCriteria, COUNT(*) AS Screens FROM dbo.SYS_Screen GROUP BY PermissionCriteria ORDER BY PermissionCriteria;
SELECT PermissionLevel AS RoleLevel, COUNT(*) AS Rows FROM dbo.SYS_RolePermission GROUP BY PermissionLevel ORDER BY PermissionLevel;
GO
