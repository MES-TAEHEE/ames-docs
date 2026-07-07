USE AMES_DEV;
GO

-- ── PP-001: SRM 주간 구매계획(ZMM30004) 업로드용 주간 컬럼 추가
--    기존 dbo.PP_Forecast 확장 (새 테이블 아님). 기존 월별 행은 WeekStartDate NULL 유지.
IF COL_LENGTH('dbo.PP_Forecast', 'WeekStartDate') IS NULL
BEGIN
    ALTER TABLE dbo.PP_Forecast ADD
      [WeekStartDate]           DATE                     NULL,  -- 주 시작일 (파일 헤더 날짜)
      [WeekLabel]               VARCHAR(10)              NULL,  -- 예: '[28/1W]'
      [BaseInv]                 DECIMAL(14,3)            NULL,  -- Base Inv. (품목당, 비정규화)
      [PartName]                NVARCHAR(100)            NULL,  -- 업체 품명 (MD_Item 미등록 대비)
      [Unit]                    VARCHAR(10)              NULL;
END
GO

-- 업서트 키: 고객 + 품목 + 주차당 1행. 기존 월별 행(WeekStartDate NULL)은 제외.
-- 필터드 인덱스는 QUOTED_IDENTIFIER ON 필수 (sqlcmd 기본값 OFF)
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_PP_Forecast_Cust_Item_Week'
                 AND object_id = OBJECT_ID('dbo.PP_Forecast'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_Forecast_Cust_Item_Week
      ON dbo.PP_Forecast (CustomerID, ItemNo, WeekStartDate)
      WHERE WeekStartDate IS NOT NULL;
END
GO
