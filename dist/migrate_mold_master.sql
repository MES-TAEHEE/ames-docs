-- ════════════════════════════════════════════════════════════════════════
-- migrate_mold_master.sql — 금형 마스터 FK 전용 (구조 DDL 은 AMES_Schema.sql, 데이터는 seed_md_mold_master.sql)
--
--   MD_MoldColor / MD_MoldItem / MD_MoldLine 테이블과 MD_Mold 확장 컬럼
--   (CumulativeShots·ShotsUpdatedTS·CarType·RefCode·AssyInjResultFlag·계산컬럼
--    MoldCodeClean + UX_MD_Mold_MoldCodeClean)의 CREATE/ALTER 는 2026-07-20 부터
--   dist/AMES_Schema.sql 에 포함된다. 이 스크립트는 이제 다음만 수행한다:
--     1) MD_Mold.CumulativeShots 초기화 (장착 후 타수 → 수명 누적 시작값)
--     2) 자식 3종 → MD_Mold FK 3종 (스키마는 FK 를 주석 처리하므로 여기서 실제 생성)
--   시뮬레이터 금형 4종의 매핑·색상·라인·수지 dev 시드(구 §3~§6)는 2026-10-02 에 폐지됐다 —
--   금형 데이터 정본은 dist/seed_md_mold_master.sql(MD_Item 사출 성형 SUB 기준, 금형 4테이블 전체 재적재)이다.
--
-- 선행: AMES_Schema.sql(구조). 비파괴·재실행 가능(idempotent). 적용 (오류 시 중단을 위해 -b 권장):
--   sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_mold_master.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1. MD_Mold 수명 누적 시작값 ─────────────────────────────────────────
-- 아직 0 인데 장착 후 타수(CurrentShots)가 있으면 그 값으로 1회 초기화.
-- (CurrentShots = Inj06 교체 시 0 리셋 / CumulativeShots = 수명 누적, 리셋 금지)
UPDATE dbo.MD_Mold SET CumulativeShots = CurrentShots, ShotsUpdatedTS = SYSDATETIME()
WHERE CumulativeShots = 0 AND ISNULL(CurrentShots,0) > 0;
GO

-- ── 2. FK 3종 (자식 → MD_Mold) ──────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldColor_Mold')
  ALTER TABLE dbo.MD_MoldColor ADD CONSTRAINT FK_MD_MoldColor_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldItem_Mold')
  ALTER TABLE dbo.MD_MoldItem ADD CONSTRAINT FK_MD_MoldItem_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MD_MoldLine_Mold')
  ALTER TABLE dbo.MD_MoldLine ADD CONSTRAINT FK_MD_MoldLine_Mold
      FOREIGN KEY ([MoldID]) REFERENCES dbo.MD_Mold([MoldID]);
GO

PRINT N'✓ migrate_mold_master.sql (FK) applied';
GO
