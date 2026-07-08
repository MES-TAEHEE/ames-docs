-- ============================================================
-- SYS_RolePermission 컬럼 순서 변경
--   1) ProcessCode 추가 (기존 ALTER TABLE로 맨 끝에 있던 것을 정위치로)
--   2) ModifiedBy 를 ModifiedTS 바로 앞으로 이동
-- ============================================================

BEGIN TRANSACTION;

-- 1. 신규 테이블 (원하는 컬럼 순서)
CREATE TABLE dbo.SYS_RolePermission_New (
  [RolePermissionID]  INT IDENTITY         NOT NULL,
  [RoleID]            NVARCHAR(450)            NULL,
  [RoleName]          VARCHAR(40)              NULL,
  [ModuleCode]        VARCHAR(10)              NULL,
  [ProcessCode]       VARCHAR(10)              NULL,
  [ScreenCode]        VARCHAR(20)              NULL,
  [PermissionLevel]   VARCHAR(10)              NULL,
  [IsSystemRole]      BIT                      NULL,
  [EffectiveTS]       DATETIME2                NULL,
  [CreatedBy]         VARCHAR(50)          NOT NULL,
  [CreatedTS]         DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]        NVARCHAR(450)            NULL,
  [ModifiedTS]        DATETIME2                NULL,
  CONSTRAINT PK_SYS_RolePermission_New PRIMARY KEY CLUSTERED ([RolePermissionID])
);

-- 2. IDENTITY INSERT 허용 후 데이터 복사
SET IDENTITY_INSERT dbo.SYS_RolePermission_New ON;

INSERT INTO dbo.SYS_RolePermission_New
       (RolePermissionID, RoleID, RoleName, ModuleCode, ProcessCode,
        ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS,
        CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
SELECT  RolePermissionID, RoleID, RoleName, ModuleCode, ProcessCode,
        ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS,
        CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
FROM    dbo.SYS_RolePermission;

SET IDENTITY_INSERT dbo.SYS_RolePermission_New OFF;

-- 3. 기존 테이블 삭제 후 이름 변경
DROP TABLE dbo.SYS_RolePermission;

EXEC sp_rename N'dbo.SYS_RolePermission_New',     N'SYS_RolePermission';
EXEC sp_rename N'dbo.PK_SYS_RolePermission_New',  N'PK_SYS_RolePermission';

COMMIT TRANSACTION;
