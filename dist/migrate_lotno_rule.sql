-- ════════════════════════════════════════════════════════════════════════
-- migrate_lotno_rule.sql — INJ LotNo 9자리 신규칙 채번 기반
--
--   LotCode 를 타임스탬프 40자에서 9자리([년1][월1][일1][라인코드2][순번4])로
--   전환한다. 순번은 SYS_LotSeq 카운터의 원자 증가 — MAX+1 스캔을 쓰지 않는다.
--   년은 (연도-2026) mod 26 → A~Z 26년 순환. 월 1~9/A~C, 일 1~9/A~V.
--
-- 비파괴·재실행 가능(idempotent). 적용 (-b 필수):
--   sqlcmd(ODBC17 전체경로) -S <server>,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_lotno_rule.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;   -- 필터드 인덱스 생성에 필수
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.MD_Line') AND name = N'LotPrefix')
  ALTER TABLE dbo.MD_Line ADD [LotPrefix] CHAR(2) NULL;  -- LotNo 라인코드 2자
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MD_Line') AND name = N'UX_MD_Line_LotPrefix')
  CREATE UNIQUE INDEX UX_MD_Line_LotPrefix
      ON dbo.MD_Line([LotPrefix]) WHERE [LotPrefix] IS NOT NULL;
GO

-- INJ 라인 시드. 신규 라인은 마스터 등록 시 부여한다.
UPDATE dbo.MD_Line SET LotPrefix = 'I1' WHERE LineID = 'LINE-INJ-01' AND LotPrefix IS NULL;
UPDATE dbo.MD_Line SET LotPrefix = 'I2' WHERE LineID = 'LINE-INJ-02' AND LotPrefix IS NULL;
GO

-- 채번 카운터. Header = 년월일(3) + 라인코드(2). 롤백 시 카운터도 롤백 → 결번 없음.
IF OBJECT_ID(N'dbo.SYS_LotSeq', N'U') IS NULL
CREATE TABLE dbo.SYS_LotSeq (
  [Header]     CHAR(5)   NOT NULL,
  [LastSeq]    INT       NOT NULL,
  [ModifiedTS] DATETIME2 NOT NULL CONSTRAINT DF_SYS_LotSeq_ModifiedTS DEFAULT SYSDATETIME(),
  CONSTRAINT PK_SYS_LotSeq PRIMARY KEY CLUSTERED ([Header])
);
GO

-- 지금까지 중복은 코드로만 막았다 — DB 를 최종 방어선으로.
-- 기존 데이터에 중복 LotCode 가 있으면 여기서 실패한다. 그 경우 중복을 먼저 정리할 것.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.tbl_Lot') AND name = N'UX_tbl_Lot_LotCode')
  CREATE UNIQUE INDEX UX_tbl_Lot_LotCode
      ON dbo.tbl_Lot([LotCode]) WHERE [LotCode] IS NOT NULL;
GO

PRINT N'✓ migrate_lotno_rule.sql applied';
GO
