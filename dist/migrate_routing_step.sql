-- ════════════════════════════════════════════════════════════════════════
-- migrate_routing_step.sql — 라우팅 템플릿 마스터(MD_RoutingStep) 도입
--   · MD_RoutingStep 생성 + A/B 라우팅 공정 시퀀스 시드
--       A: INJ → IMG → QC → FG     B: INJ → PNT → QC → FG
--   · SYS_Screen MD-031(md/rp/routing) 등록 (Bop=MD-005 바로 이전) + Admin 권한
--   · PP_WorkOrder.Routing → RoutingType 리네임 (타 테이블 명명 규칙 통일, 스냅샷용)
-- idempotent(멱등). 적용: sqlcmd(ODBC17) -f 65001 -b -i dist/migrate_routing_step.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) MD_RoutingStep 마스터 ─────────────────────────────────────────────
IF OBJECT_ID('dbo.MD_RoutingStep', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_RoutingStep (
        RoutingType    char(1)       NOT NULL,
        StepSeq        int           NOT NULL,
        ProcessCode    varchar(10)   NOT NULL,
        QcRequiredFlag bit           NOT NULL CONSTRAINT DF_MD_RoutingStep_Qc     DEFAULT (0),
        ActiveFlag     bit           NOT NULL CONSTRAINT DF_MD_RoutingStep_Active DEFAULT (1),
        CreatedBy      varchar(50)   NULL,
        CreatedTS      datetime2     NOT NULL CONSTRAINT DF_MD_RoutingStep_CTS    DEFAULT (SYSDATETIME()),
        ModifiedBy     nvarchar(900) NULL,
        ModifiedTS     datetime2     NULL,
        CONSTRAINT PK_MD_RoutingStep PRIMARY KEY (RoutingType, StepSeq)
    );
    PRINT N'✓ MD_RoutingStep 생성';
END
ELSE
    PRINT N'· MD_RoutingStep 이미 존재';
GO

-- ── 2) A/B 라우팅 시퀀스 시드 (멱등 MERGE) ───────────────────────────────
MERGE dbo.MD_RoutingStep AS tgt
USING (VALUES
    ('A', 1, 'INJ', 0),
    ('A', 2, 'IMG', 0),
    ('A', 3, 'QC',  1),
    ('A', 4, 'FG',  0),
    ('B', 1, 'INJ', 0),
    ('B', 2, 'PNT', 0),
    ('B', 3, 'QC',  1),
    ('B', 4, 'FG',  0)
) AS src (RoutingType, StepSeq, ProcessCode, QcRequiredFlag)
ON  tgt.RoutingType = src.RoutingType AND tgt.StepSeq = src.StepSeq
WHEN MATCHED THEN
    UPDATE SET tgt.ProcessCode    = src.ProcessCode,
               tgt.QcRequiredFlag = src.QcRequiredFlag,
               tgt.ModifiedBy     = 'seed',
               tgt.ModifiedTS     = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS)
    VALUES (src.RoutingType, src.StepSeq, src.ProcessCode, src.QcRequiredFlag, 1, 'seed', SYSDATETIME());
PRINT CONCAT(N'✓ MD_RoutingStep 시드: ', @@ROWCOUNT, N'행 처리됨');
GO

-- ── 3) SYS_Screen MD-031 등록 (Bop=MD-005 바로 이전 위치) ────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-031')
BEGIN
    DECLARE @bopOrder int = (SELECT SortOrder FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-005');

    -- Bop 및 그 이후 MD 화면을 한 칸 뒤로 밀고, 그 자리에 MD-031 삽입
    UPDATE dbo.SYS_Screen
       SET SortOrder = SortOrder + 1
     WHERE ProcessCode = 'MD' AND SortOrder >= @bopOrder;

    INSERT INTO dbo.SYS_Screen
        (ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
         ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        ('MD-031', 'WEB', 'MD', 'RP',
         N'라우팅 기준정보 관리', N'Routing Master', 'md/rp/routing', 'MD-031', @bopOrder, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MD-031 등록 (md/rp/routing, Bop 이전)';
END
ELSE
    PRINT N'· SYS_Screen MD-031 이미 존재';
GO

-- ── 4) Admin 롤 MD-031 FULL 권한 ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission WHERE RoleName = 'Admin' AND ScreenCode = 'MD-031')
BEGIN
    DECLARE @AdminRoleId NVARCHAR(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Name = 'Admin');
    INSERT INTO dbo.SYS_RolePermission
        (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
    VALUES
        (@AdminRoleId, 'Admin', 'WEB', 'MD-031', 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME());
    PRINT N'✓ SYS_RolePermission Admin/MD-031 (REA)';
END
ELSE
    PRINT N'· SYS_RolePermission Admin/MD-031 이미 존재';
GO

-- ── 5) PP_WorkOrder.Routing → RoutingType 리네임 ─────────────────────────
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PP_WorkOrder') AND name = 'Routing')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PP_WorkOrder') AND name = 'RoutingType')
BEGIN
    EXEC sp_rename 'dbo.PP_WorkOrder.Routing', 'RoutingType', 'COLUMN';
    PRINT N'✓ PP_WorkOrder.Routing → RoutingType 리네임';
END
ELSE
    PRINT N'· PP_WorkOrder.RoutingType 이미 적용됨(또는 Routing 컬럼 없음)';
GO

-- ── 확인 ─────────────────────────────────────────────────────────────────
SELECT RoutingType, StepSeq, ProcessCode, QcRequiredFlag FROM dbo.MD_RoutingStep ORDER BY RoutingType, StepSeq;
SELECT ScreenCode, ProcessCode, SubProcessCode, HRef, SortOrder FROM dbo.SYS_Screen
 WHERE ProcessCode='MD' AND SortOrder BETWEEN (SELECT SortOrder FROM dbo.SYS_Screen WHERE ScreenCode='MD-031')-1
                                          AND (SELECT SortOrder FROM dbo.SYS_Screen WHERE ScreenCode='MD-005')
 ORDER BY SortOrder;
SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.PP_WorkOrder') AND name IN ('Routing','RoutingType');
GO
