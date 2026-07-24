-- ============================================================
-- Seed: MD_RoutingStep (라우팅 템플릿)
-- Generated: 2026-07-24 from live DB AMES_DEV
--   A: INJ → IMG → QC → FG     B: INJ → PNT → QC → FG
--   QcRequiredFlag: QC 스텝만 1
-- ============================================================
USE AMES_DEV;
GO
SET NOCOUNT ON;

DELETE dbo.MD_RoutingStep;

INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('A', 1, 'INJ', 0, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('A', 2, 'IMG', 0, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('A', 3, 'QC',  1, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('A', 4, 'FG',  0, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('B', 1, 'INJ', 0, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('B', 2, 'PNT', 0, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('B', 3, 'QC',  1, 1, 'seed', SYSDATETIME());
INSERT INTO dbo.MD_RoutingStep (RoutingType, StepSeq, ProcessCode, QcRequiredFlag, ActiveFlag, CreatedBy, CreatedTS) VALUES ('B', 4, 'FG',  0, 1, 'seed', SYSDATETIME());
GO

SELECT RoutingType, StepSeq, ProcessCode, QcRequiredFlag FROM dbo.MD_RoutingStep ORDER BY RoutingType, StepSeq;
GO
