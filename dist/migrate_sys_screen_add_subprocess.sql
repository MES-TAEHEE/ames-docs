-- SYS_Screen 테이블 재생성 — ProcessCode 다음에 SubProcessCode 삽입
-- 전략: 기존 테이블 이름 변경 → 새 테이블 생성 → 데이터 복사 → 기존 삭제

USE AMES_DEV;
GO

-- 이미 완료된 경우 스킵
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.SYS_Screen')
      AND name = N'SubProcessCode'
      AND column_id = (
          SELECT column_id + 1
          FROM sys.columns
          WHERE object_id = OBJECT_ID(N'dbo.SYS_Screen')
            AND name = N'ProcessCode'
      )
)
BEGIN
    PRINT 'SubProcessCode가 이미 ProcessCode 다음에 있습니다. 스킵.';
    RETURN;
END
GO

-- 1단계: 기존 테이블 이름 변경
EXEC sp_rename N'dbo.SYS_Screen', N'SYS_Screen_Old';
GO

-- 2단계: 올바른 컬럼 순서로 새 테이블 생성
CREATE TABLE dbo.SYS_Screen (
  [ScreenID]       INT IDENTITY     NOT NULL,
  [ScreenCode]     VARCHAR(20)      NOT NULL,
  [ModuleCode]     VARCHAR(10)      NOT NULL,
  [ProcessCode]    VARCHAR(10)          NULL,
  [SubProcessCode] VARCHAR(10)          NULL,
  [ScreenName]     NVARCHAR(100)    NOT NULL,
  [ScreenNameEn]   NVARCHAR(100)        NULL,
  [HRef]           VARCHAR(200)         NULL,
  [LidLabel]       VARCHAR(20)          NULL,
  [SortOrder]      INT                  NULL,
  [IsVisible]      BIT                  NULL  DEFAULT 1,
  [CreatedBy]      VARCHAR(50)      NOT NULL,
  [CreatedTS]      DATETIME2            NULL  DEFAULT SYSDATETIME(),
  [ModifiedBy]     VARCHAR(50)          NULL,
  [ModifiedTS]     DATETIME2            NULL,
  CONSTRAINT PK_SYS_Screen PRIMARY KEY CLUSTERED ([ScreenID]),
  CONSTRAINT UQ_SYS_Screen UNIQUE ([ScreenCode])
);
GO

-- 3단계: 데이터 복사 (IDENTITY 값 유지)
SET IDENTITY_INSERT dbo.SYS_Screen ON;

INSERT INTO dbo.SYS_Screen
  (ScreenID, ScreenCode, ModuleCode, ProcessCode, SubProcessCode,
   ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible,
   CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
SELECT
  ScreenID, ScreenCode, ModuleCode, ProcessCode,
  -- 이전에 ALTER TABLE로 추가됐을 경우 값 복사, 없으면 NULL
  CASE WHEN EXISTS (
      SELECT 1 FROM sys.columns
      WHERE object_id = OBJECT_ID(N'dbo.SYS_Screen_Old')
        AND name = N'SubProcessCode'
  ) THEN SubProcessCode ELSE NULL END,
  ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible,
  CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
FROM dbo.SYS_Screen_Old;

SET IDENTITY_INSERT dbo.SYS_Screen OFF;
GO

-- 4단계: 기존(이름 변경된) 테이블 삭제
DROP TABLE dbo.SYS_Screen_Old;
GO

-- 검증
SELECT ordinal_position, column_name, data_type, character_maximum_length, is_nullable
FROM information_schema.columns
WHERE table_name = 'SYS_Screen'
ORDER BY ordinal_position;
GO

PRINT '✓ SYS_Screen 재생성 완료 — SubProcessCode가 ProcessCode(4번) 다음에 위치합니다.';
GO
