-- PP_WorkOrder.WoNumber 유니크 인덱스 — 동시 채번 중복 방지 최종 방어선.
-- 적용: sqlcmd -S localhost -d AMES_DEV -f 65001 -i dist\migrate_wonumber_unique.sql
SET QUOTED_IDENTIFIER ON;  -- 필터드 인덱스 필수 (sqlcmd 기본값 OFF)
GO

-- 기존 중복 WoNumber가 있으면 아래 인덱스 생성이 실패한다. 결과가 나오면 수동 정리 후 재실행.
SELECT WoNumber, COUNT(*) AS DupCount
FROM   dbo.PP_WorkOrder
WHERE  WoNumber IS NOT NULL
GROUP  BY WoNumber
HAVING COUNT(*) > 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_PP_WorkOrder_WoNumber'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrder'))
CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrder_WoNumber
  ON dbo.PP_WorkOrder (WoNumber)
  WHERE WoNumber IS NOT NULL;
GO
