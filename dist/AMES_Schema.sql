-- ════════════════════════════════════════════════════════════════════════
-- A-MES Database Schema (Auto-generated)
-- Generated: 2026-06-08 16:33:54
-- Source: AMES_ERD_data.js
-- Total tables: 149
-- Engine: SQL Server 2022/2025
-- Pattern: Stored Procedure + ADO.NET (per VOL01 Tech Stack)
-- FK constraints: not applied (commented as -- FK -> Target.Col)
-- ════════════════════════════════════════════════════════════════════════

USE [AMES_DEV];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- ────────────────────────────────────────────────────────────────────────
-- DROP existing tables (idempotent re-run)
-- ────────────────────────────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.AspNetRoleClaims', N'U') IS NOT NULL DROP TABLE dbo.AspNetRoleClaims;
IF OBJECT_ID(N'dbo.AspNetUserTokens', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserTokens;
IF OBJECT_ID(N'dbo.AspNetUserLogins', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserLogins;
IF OBJECT_ID(N'dbo.AspNetUserClaims', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserClaims;
IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL DROP TABLE dbo.AspNetUserRoles;
IF OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL DROP TABLE dbo.AspNetRoles;
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL DROP TABLE dbo.AspNetUsers;
IF OBJECT_ID(N'dbo.SYS_FactoryCalendar', N'U') IS NOT NULL DROP TABLE dbo.SYS_FactoryCalendar;
IF OBJECT_ID(N'dbo.SYS_InterfaceMonitor', N'U') IS NOT NULL DROP TABLE dbo.SYS_InterfaceMonitor;
IF OBJECT_ID(N'dbo.SYS_Config', N'U') IS NOT NULL DROP TABLE dbo.SYS_Config;
IF OBJECT_ID(N'dbo.SYS_NotificationHistory', N'U') IS NOT NULL DROP TABLE dbo.SYS_NotificationHistory;
IF OBJECT_ID(N'dbo.SYS_NotificationChannel', N'U') IS NOT NULL DROP TABLE dbo.SYS_NotificationChannel;
IF OBJECT_ID(N'dbo.SYS_NotificationRule', N'U') IS NOT NULL DROP TABLE dbo.SYS_NotificationRule;
IF OBJECT_ID(N'dbo.SYS_AuditLog', N'U') IS NOT NULL DROP TABLE dbo.SYS_AuditLog;
IF OBJECT_ID(N'dbo.SYS_RolePermission', N'U') IS NOT NULL DROP TABLE dbo.SYS_RolePermission;
IF OBJECT_ID(N'dbo.SYS_UserProfile', N'U') IS NOT NULL DROP TABLE dbo.SYS_UserProfile;
IF OBJECT_ID(N'dbo.MNT_MoldShotCount', N'U') IS NOT NULL DROP TABLE dbo.MNT_MoldShotCount;
IF OBJECT_ID(N'dbo.MNT_SparePartsTxn', N'U') IS NOT NULL DROP TABLE dbo.MNT_SparePartsTxn;
IF OBJECT_ID(N'dbo.MNT_WorkOrderTask', N'U') IS NOT NULL DROP TABLE dbo.MNT_WorkOrderTask;
IF OBJECT_ID(N'dbo.MNT_WorkOrder', N'U') IS NOT NULL DROP TABLE dbo.MNT_WorkOrder;
IF OBJECT_ID(N'dbo.MNT_PMExecution', N'U') IS NOT NULL DROP TABLE dbo.MNT_PMExecution;
IF OBJECT_ID(N'dbo.MNT_PMSchedule', N'U') IS NOT NULL DROP TABLE dbo.MNT_PMSchedule;
IF OBJECT_ID(N'dbo.MNT_OEELog', N'U') IS NOT NULL DROP TABLE dbo.MNT_OEELog;
IF OBJECT_ID(N'dbo.MNT_FailureAction', N'U') IS NOT NULL DROP TABLE dbo.MNT_FailureAction;
IF OBJECT_ID(N'dbo.MNT_FailureRegister', N'U') IS NOT NULL DROP TABLE dbo.MNT_FailureRegister;
IF OBJECT_ID(N'dbo.MNT_EquipmentStatus', N'U') IS NOT NULL DROP TABLE dbo.MNT_EquipmentStatus;
IF OBJECT_ID(N'dbo.FG_ReturnDisposition', N'U') IS NOT NULL DROP TABLE dbo.FG_ReturnDisposition;
IF OBJECT_ID(N'dbo.FG_CustomerReturn', N'U') IS NOT NULL DROP TABLE dbo.FG_CustomerReturn;
IF OBJECT_ID(N'dbo.FG_DayEndClose', N'U') IS NOT NULL DROP TABLE dbo.FG_DayEndClose;
IF OBJECT_ID(N'dbo.FG_DeliveryNote', N'U') IS NOT NULL DROP TABLE dbo.FG_DeliveryNote;
IF OBJECT_ID(N'dbo.FG_LoadingConfirm', N'U') IS NOT NULL DROP TABLE dbo.FG_LoadingConfirm;
IF OBJECT_ID(N'dbo.FG_PickingFifo', N'U') IS NOT NULL DROP TABLE dbo.FG_PickingFifo;
IF OBJECT_ID(N'dbo.FG_ShipmentOrderLine', N'U') IS NOT NULL DROP TABLE dbo.FG_ShipmentOrderLine;
IF OBJECT_ID(N'dbo.FG_ShipmentOrder', N'U') IS NOT NULL DROP TABLE dbo.FG_ShipmentOrder;
IF OBJECT_ID(N'dbo.FG_PutAway', N'U') IS NOT NULL DROP TABLE dbo.FG_PutAway;
IF OBJECT_ID(N'dbo.FG_Stock', N'U') IS NOT NULL DROP TABLE dbo.FG_Stock;
IF OBJECT_ID(N'dbo.QC_Disposition', N'U') IS NOT NULL DROP TABLE dbo.QC_Disposition;
IF OBJECT_ID(N'dbo.QC_CAPA_Action', N'U') IS NOT NULL DROP TABLE dbo.QC_CAPA_Action;
IF OBJECT_ID(N'dbo.QC_CAPA', N'U') IS NOT NULL DROP TABLE dbo.QC_CAPA;
IF OBJECT_ID(N'dbo.QC_HoldRelease', N'U') IS NOT NULL DROP TABLE dbo.QC_HoldRelease;
IF OBJECT_ID(N'dbo.QC_Hold', N'U') IS NOT NULL DROP TABLE dbo.QC_Hold;
IF OBJECT_ID(N'dbo.QC_NCR_Action', N'U') IS NOT NULL DROP TABLE dbo.QC_NCR_Action;
IF OBJECT_ID(N'dbo.QC_NCR', N'U') IS NOT NULL DROP TABLE dbo.QC_NCR;
IF OBJECT_ID(N'dbo.QC_InspectionStd', N'U') IS NOT NULL DROP TABLE dbo.QC_InspectionStd;
IF OBJECT_ID(N'dbo.QC_InspectionItem', N'U') IS NOT NULL DROP TABLE dbo.QC_InspectionItem;
IF OBJECT_ID(N'dbo.QC_Inspection', N'U') IS NOT NULL DROP TABLE dbo.QC_Inspection;
IF OBJECT_ID(N'dbo.PNT_QcQueue', N'U') IS NOT NULL DROP TABLE dbo.PNT_QcQueue;
IF OBJECT_ID(N'dbo.PNT_DailyReport', N'U') IS NOT NULL DROP TABLE dbo.PNT_DailyReport;
IF OBJECT_ID(N'dbo.PNT_ShiftReportAudit', N'U') IS NOT NULL DROP TABLE dbo.PNT_ShiftReportAudit;
IF OBJECT_ID(N'dbo.PNT_ShiftReportLineItem', N'U') IS NOT NULL DROP TABLE dbo.PNT_ShiftReportLineItem;
IF OBJECT_ID(N'dbo.PNT_ShiftReport', N'U') IS NOT NULL DROP TABLE dbo.PNT_ShiftReport;
IF OBJECT_ID(N'dbo.PNT_LabelScanLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_LabelScanLog;
IF OBJECT_ID(N'dbo.PNT_LabelPrintJob', N'U') IS NOT NULL DROP TABLE dbo.PNT_LabelPrintJob;
IF OBJECT_ID(N'dbo.PNT_LotLabel', N'U') IS NOT NULL DROP TABLE dbo.PNT_LotLabel;
IF OBJECT_ID(N'dbo.PNT_StationStatsCache', N'U') IS NOT NULL DROP TABLE dbo.PNT_StationStatsCache;
IF OBJECT_ID(N'dbo.PNT_PartLossLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_PartLossLog;
IF OBJECT_ID(N'dbo.PNT_JigUnload', N'U') IS NOT NULL DROP TABLE dbo.PNT_JigUnload;
IF OBJECT_ID(N'dbo.PNT_OvenSpikeLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_OvenSpikeLog;
IF OBJECT_ID(N'dbo.PNT_OvenDeviationLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_OvenDeviationLog;
IF OBJECT_ID(N'dbo.PNT_OvenTempSample', N'U') IS NOT NULL DROP TABLE dbo.PNT_OvenTempSample;
IF OBJECT_ID(N'dbo.PNT_OvenLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_OvenLog;
IF OBJECT_ID(N'dbo.PNT_TagFailureLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_TagFailureLog;
IF OBJECT_ID(N'dbo.PNT_LineEvent', N'U') IS NOT NULL DROP TABLE dbo.PNT_LineEvent;
IF OBJECT_ID(N'dbo.PNT_JigLoad', N'U') IS NOT NULL DROP TABLE dbo.PNT_JigLoad;
IF OBJECT_ID(N'dbo.PNT_SeqAllocator', N'U') IS NOT NULL DROP TABLE dbo.PNT_SeqAllocator;
IF OBJECT_ID(N'dbo.PNT_JigBindingLog', N'U') IS NOT NULL DROP TABLE dbo.PNT_JigBindingLog;
IF OBJECT_ID(N'dbo.PNT_VirtualLot', N'U') IS NOT NULL DROP TABLE dbo.PNT_VirtualLot;
IF OBJECT_ID(N'dbo.PNT_DailyPlan', N'U') IS NOT NULL DROP TABLE dbo.PNT_DailyPlan;
IF OBJECT_ID(N'dbo.PR_BondSetupAudit', N'U') IS NOT NULL DROP TABLE dbo.PR_BondSetupAudit;
IF OBJECT_ID(N'dbo.PR_BondCycleLog', N'U') IS NOT NULL DROP TABLE dbo.PR_BondCycleLog;
IF OBJECT_ID(N'dbo.PR_BondSetup', N'U') IS NOT NULL DROP TABLE dbo.PR_BondSetup;
IF OBJECT_ID(N'dbo.PR_FabricDeductionLog', N'U') IS NOT NULL DROP TABLE dbo.PR_FabricDeductionLog;
IF OBJECT_ID(N'dbo.PR_FabricIssueAttempt', N'U') IS NOT NULL DROP TABLE dbo.PR_FabricIssueAttempt;
IF OBJECT_ID(N'dbo.PR_FabricIssue', N'U') IS NOT NULL DROP TABLE dbo.PR_FabricIssue;
IF OBJECT_ID(N'dbo.PR_DefectRateCache', N'U') IS NOT NULL DROP TABLE dbo.PR_DefectRateCache;
IF OBJECT_ID(N'dbo.PR_DashTileCache', N'U') IS NOT NULL DROP TABLE dbo.PR_DashTileCache;
IF OBJECT_ID(N'dbo.PR_ShiftHandover', N'U') IS NOT NULL DROP TABLE dbo.PR_ShiftHandover;
IF OBJECT_ID(N'dbo.PR_PlcInterlock', N'U') IS NOT NULL DROP TABLE dbo.PR_PlcInterlock;
IF OBJECT_ID(N'dbo.PR_AndonPush', N'U') IS NOT NULL DROP TABLE dbo.PR_AndonPush;
IF OBJECT_ID(N'dbo.PR_AndonCall', N'U') IS NOT NULL DROP TABLE dbo.PR_AndonCall;
IF OBJECT_ID(N'dbo.PR_EquipStatusLog', N'U') IS NOT NULL DROP TABLE dbo.PR_EquipStatusLog;
IF OBJECT_ID(N'dbo.PR_ShotCount', N'U') IS NOT NULL DROP TABLE dbo.PR_ShotCount;
IF OBJECT_ID(N'dbo.PR_MoldChange', N'U') IS NOT NULL DROP TABLE dbo.PR_MoldChange;
IF OBJECT_ID(N'dbo.PR_CycleAnomalyLog', N'U') IS NOT NULL DROP TABLE dbo.PR_CycleAnomalyLog;
IF OBJECT_ID(N'dbo.PR_DefectAutoLink', N'U') IS NOT NULL DROP TABLE dbo.PR_DefectAutoLink;
IF OBJECT_ID(N'dbo.PR_DefectDetail', N'U') IS NOT NULL DROP TABLE dbo.PR_DefectDetail;
IF OBJECT_ID(N'dbo.PR_ProductionResult', N'U') IS NOT NULL DROP TABLE dbo.PR_ProductionResult;
IF OBJECT_ID(N'dbo.PR_WoAcceptance', N'U') IS NOT NULL DROP TABLE dbo.PR_WoAcceptance;
IF OBJECT_ID(N'dbo.PR_PopAuthLog', N'U') IS NOT NULL DROP TABLE dbo.PR_PopAuthLog;
IF OBJECT_ID(N'dbo.PR_PopSession', N'U') IS NOT NULL DROP TABLE dbo.PR_PopSession;
IF OBJECT_ID(N'dbo.PR_RobotInspection', N'U') IS NOT NULL DROP TABLE dbo.PR_RobotInspection;
IF OBJECT_ID(N'dbo.PR_InjLot', N'U') IS NOT NULL DROP TABLE dbo.PR_InjLot;
IF OBJECT_ID(N'dbo.PR_InjCondLog', N'U') IS NOT NULL DROP TABLE dbo.PR_InjCondLog;
IF OBJECT_ID(N'dbo.MD_InjCondItem', N'U') IS NOT NULL DROP TABLE dbo.MD_InjCondItem;
IF OBJECT_ID(N'dbo.MD_MoldItemMap', N'U') IS NOT NULL DROP TABLE dbo.MD_MoldItemMap;
IF OBJECT_ID(N'dbo.tbl_Lot', N'U') IS NOT NULL DROP TABLE dbo.tbl_Lot;
IF OBJECT_ID(N'dbo.PP_ProductionCalendarOverride', N'U') IS NOT NULL DROP TABLE dbo.PP_ProductionCalendarOverride;
IF OBJECT_ID(N'dbo.PP_LineOEE', N'U') IS NOT NULL DROP TABLE dbo.PP_LineOEE;
IF OBJECT_ID(N'dbo.PP_LineDowntimeLog', N'U') IS NOT NULL DROP TABLE dbo.PP_LineDowntimeLog;
IF OBJECT_ID(N'dbo.PP_LineStateLog', N'U') IS NOT NULL DROP TABLE dbo.PP_LineStateLog;
IF OBJECT_ID(N'dbo.PP_LineSchedule', N'U') IS NOT NULL DROP TABLE dbo.PP_LineSchedule;
IF OBJECT_ID(N'dbo.PP_MRPLog', N'U') IS NOT NULL DROP TABLE dbo.PP_MRPLog;
IF OBJECT_ID(N'dbo.PP_PRSendLog', N'U') IS NOT NULL DROP TABLE dbo.PP_PRSendLog;
IF OBJECT_ID(N'dbo.PP_PurchaseRequest', N'U') IS NOT NULL DROP TABLE dbo.PP_PurchaseRequest;
IF OBJECT_ID(N'dbo.PP_MaterialReservation', N'U') IS NOT NULL DROP TABLE dbo.PP_MaterialReservation;
IF OBJECT_ID(N'dbo.PP_WorkOrderRouting', N'U') IS NOT NULL DROP TABLE dbo.PP_WorkOrderRouting;
IF OBJECT_ID(N'dbo.PP_WorkOrder', N'U') IS NOT NULL DROP TABLE dbo.PP_WorkOrder;
IF OBJECT_ID(N'dbo.PP_SupplyPlanDetail', N'U') IS NOT NULL DROP TABLE dbo.PP_SupplyPlanDetail;
IF OBJECT_ID(N'dbo.PP_SupplyPlan', N'U') IS NOT NULL DROP TABLE dbo.PP_SupplyPlan;
IF OBJECT_ID(N'dbo.PP_CustomerOrder', N'U') IS NOT NULL DROP TABLE dbo.PP_CustomerOrder;
IF OBJECT_ID(N'dbo.PP_ForecastHistory', N'U') IS NOT NULL DROP TABLE dbo.PP_ForecastHistory;
IF OBJECT_ID(N'dbo.PP_Forecast', N'U') IS NOT NULL DROP TABLE dbo.PP_Forecast;
IF OBJECT_ID(N'dbo.WH_TransactionHistory', N'U') IS NOT NULL DROP TABLE dbo.WH_TransactionHistory;
IF OBJECT_ID(N'dbo.WH_ReleasePicking', N'U') IS NOT NULL DROP TABLE dbo.WH_ReleasePicking;
IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NOT NULL DROP TABLE dbo.WH_ReleaseSchedule;
IF OBJECT_ID(N'dbo.WH_InventoryAdjust', N'U') IS NOT NULL DROP TABLE dbo.WH_InventoryAdjust;
IF OBJECT_ID(N'dbo.WH_InventorySnapshot', N'U') IS NOT NULL DROP TABLE dbo.WH_InventorySnapshot;
IF OBJECT_ID(N'dbo.WH_Inventory', N'U') IS NOT NULL DROP TABLE dbo.WH_Inventory;
IF OBJECT_ID(N'dbo.WH_Receiving', N'U') IS NOT NULL DROP TABLE dbo.WH_Receiving;
IF OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL DROP TABLE dbo.WH_PurchaseOrder;
IF OBJECT_ID(N'dbo.MD_Recipe', N'U') IS NOT NULL DROP TABLE dbo.MD_Recipe;
IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL DROP TABLE dbo.MD_Location;
IF OBJECT_ID(N'dbo.MD_LineTimeSegment', N'U') IS NOT NULL DROP TABLE dbo.MD_LineTimeSegment;
IF OBJECT_ID(N'dbo.MD_LineTimePattern', N'U') IS NOT NULL DROP TABLE dbo.MD_LineTimePattern;
IF OBJECT_ID(N'dbo.MD_PmTemplateStep', N'U') IS NOT NULL DROP TABLE dbo.MD_PmTemplateStep;
IF OBJECT_ID(N'dbo.MD_PmTemplate', N'U') IS NOT NULL DROP TABLE dbo.MD_PmTemplate;
IF OBJECT_ID(N'dbo.MD_SparePart', N'U') IS NOT NULL DROP TABLE dbo.MD_SparePart;
IF OBJECT_ID(N'dbo.MD_CodeItem', N'U') IS NOT NULL DROP TABLE dbo.MD_CodeItem;
IF OBJECT_ID(N'dbo.MD_CodeGroup', N'U') IS NOT NULL DROP TABLE dbo.MD_CodeGroup;
IF OBJECT_ID(N'dbo.MD_ReasonCode', N'U') IS NOT NULL DROP TABLE dbo.MD_ReasonCode;
IF OBJECT_ID(N'dbo.MD_LabelTemplate', N'U') IS NOT NULL DROP TABLE dbo.MD_LabelTemplate;
IF OBJECT_ID(N'dbo.MD_PackagingSpec', N'U') IS NOT NULL DROP TABLE dbo.MD_PackagingSpec;
IF OBJECT_ID(N'dbo.MD_DefectCause', N'U') IS NOT NULL DROP TABLE dbo.MD_DefectCause;
IF OBJECT_ID(N'dbo.MD_DefectCode', N'U') IS NOT NULL DROP TABLE dbo.MD_DefectCode;
IF OBJECT_ID(N'dbo.MD_Station', N'U') IS NOT NULL DROP TABLE dbo.MD_Station;
IF OBJECT_ID(N'dbo.MD_Line', N'U') IS NOT NULL DROP TABLE dbo.MD_Line;
IF OBJECT_ID(N'dbo.MD_RfidReader', N'U') IS NOT NULL DROP TABLE dbo.MD_RfidReader;
IF OBJECT_ID(N'dbo.MD_Oven', N'U') IS NOT NULL DROP TABLE dbo.MD_Oven;
IF OBJECT_ID(N'dbo.MD_RalColor', N'U') IS NOT NULL DROP TABLE dbo.MD_RalColor;
IF OBJECT_ID(N'dbo.MD_RfidTag', N'U') IS NOT NULL DROP TABLE dbo.MD_RfidTag;
IF OBJECT_ID(N'dbo.MD_Jig', N'U') IS NOT NULL DROP TABLE dbo.MD_Jig;
IF OBJECT_ID(N'dbo.MD_Calendar', N'U') IS NOT NULL DROP TABLE dbo.MD_Calendar;
IF OBJECT_ID(N'dbo.MD_Uom', N'U') IS NOT NULL DROP TABLE dbo.MD_Uom;
IF OBJECT_ID(N'dbo.MD_Customer', N'U') IS NOT NULL DROP TABLE dbo.MD_Customer;
IF OBJECT_ID(N'dbo.MD_ShipmentDest', N'U') IS NOT NULL DROP TABLE dbo.MD_ShipmentDest;
IF OBJECT_ID(N'dbo.MD_PaintFabric', N'U') IS NOT NULL DROP TABLE dbo.MD_PaintFabric;
IF OBJECT_ID(N'dbo.MD_Mold', N'U') IS NOT NULL DROP TABLE dbo.MD_Mold;
IF OBJECT_ID(N'dbo.MD_Equipment', N'U') IS NOT NULL DROP TABLE dbo.MD_Equipment;
IF OBJECT_ID(N'dbo.MD_Vendor', N'U') IS NOT NULL DROP TABLE dbo.MD_Vendor;
IF OBJECT_ID(N'dbo.MD_InspectionStandard', N'U') IS NOT NULL DROP TABLE dbo.MD_InspectionStandard;
IF OBJECT_ID(N'dbo.MD_WorkCenter', N'U') IS NOT NULL DROP TABLE dbo.MD_WorkCenter;
IF OBJECT_ID(N'dbo.MD_Bop', N'U') IS NOT NULL DROP TABLE dbo.MD_Bop;
IF OBJECT_ID(N'dbo.MD_BomVersion', N'U') IS NOT NULL DROP TABLE dbo.MD_BomVersion;
IF OBJECT_ID(N'dbo.MD_Bom', N'U') IS NOT NULL DROP TABLE dbo.MD_Bom;
IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL DROP TABLE dbo.MD_Item;
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: MD                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── MD_Item  (품목 (MD-01))
CREATE TABLE dbo.MD_Item (
  [ItemNo]                    VARCHAR(20)          NOT NULL,
  [ItemName]                  NVARCHAR(80)         NOT NULL,
  [ItemType]                  VARCHAR(10)              NULL,
  [ItemCategory]              VARCHAR(30)              NULL,
  [CarType]                   VARCHAR(10)              NULL,
  [DefaultUOM]                VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [RoutingType]               CHAR(1)                  NULL,
  [MinStock]                  DECIMAL(14,4)            NULL,
  [MaxStock]                  DECIMAL(14,4)            NULL,
  [SafetyStock]               DECIMAL(14,4)            NULL,
  [UnitCost]                  DECIMAL(14,2)            NULL,
  [DrawingNo]                 VARCHAR(30)              NULL,
  [PGN]                       VARCHAR(4)               NULL,
  [ALC]                       VARCHAR(10)              NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(20)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Item PRIMARY KEY CLUSTERED ([ItemNo])
);
GO

-- ── MD_Bom  (BOM (MD-02))
CREATE TABLE dbo.MD_Bom (
  [BOMID]                     VARCHAR(24)          NOT NULL,
  [ParentItemNo]              VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CompItemNo]                VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [BOMLevel]                  INT                      NULL,
  [QtyPer]                    DECIMAL(12,4)            NULL,
  [UOM]                       VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [ScrapPct]                  DECIMAL(5,2)             NULL,
  [VersionID]                 VARCHAR(24)              NULL,  -- FK -> MD_BomVersion.VersionID
  [Position]                  INT                      NULL,
  [Note]                      NVARCHAR(120)            NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Bom PRIMARY KEY CLUSTERED ([BOMID])
);
GO

-- ── MD_BomVersion  (BOM 버전 (MD-03))
CREATE TABLE dbo.MD_BomVersion (
  [VersionID]                 VARCHAR(24)          NOT NULL,
  [RootItemNo]                VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VersionNo]                 VARCHAR(10)              NULL,
  [EffFrom]                   DATE                     NULL,
  [EffTo]                     DATE                     NULL,
  [ChangeType]                VARCHAR(16)              NULL,
  [ChangeReason]              NVARCHAR(200)            NULL,
  [RequestedBy]               VARCHAR(20)              NULL,
  [ApprovedBy]                VARCHAR(20)              NULL,
  [ApprovedTS]                DATETIME2                NULL,
  [Status]                    VARCHAR(12)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_BomVersion PRIMARY KEY CLUSTERED ([VersionID])
);
GO

-- ── MD_Bop  (BOP 라우팅 (MD-04))
CREATE TABLE dbo.MD_Bop (
  [BOPID]                     VARCHAR(24)          NOT NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RoutingType]               CHAR(1)                  NULL,
  [StepSeq]                   INT                      NULL,
  [ProcessCode]               VARCHAR(10)              NULL,
  [WorkCenterID]              VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [StdCycleTime]              DECIMAL(8,2)             NULL,
  [StdSetupTime]              DECIMAL(8,2)             NULL,
  [QcRequiredFlag]            BIT                      NULL,
  [StepDescription]           NVARCHAR(120)            NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Bop PRIMARY KEY CLUSTERED ([BOPID])
);
GO

-- ── MD_WorkCenter  (작업장 (MD-05))
CREATE TABLE dbo.MD_WorkCenter (
  [WCID]                      VARCHAR(20)          NOT NULL,
  [WCName]                    NVARCHAR(50)             NULL,
  [ProcessType]               VARCHAR(16)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [DailyCapacity]             INT                      NULL,
  [StdManpower]               INT                      NULL,
  [CostCenterCode]            VARCHAR(20)              NULL,
  [LocationDesc]              NVARCHAR(60)             NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_WorkCenter PRIMARY KEY CLUSTERED ([WCID])
);
GO

-- ── MD_InspectionStandard  (검사 기준 (MD-06))
CREATE TABLE dbo.MD_InspectionStandard (
  [InspStdID]                 VARCHAR(20)          NOT NULL,
  [ItemID]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [ProcessCode]               VARCHAR(10)              NULL,
  [InspType]                  VARCHAR(10)              NULL,
  [CharName]                  NVARCHAR(60)             NULL,
  [SpecNominal]               DECIMAL(12,4)            NULL,
  [SpecLSL]                   DECIMAL(12,4)            NULL,
  [SpecUSL]                   DECIMAL(12,4)            NULL,
  [UOM]                       VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [SamplingPlan]              VARCHAR(20)              NULL,
  [InspMethod]                NVARCHAR(40)             NULL,
  [IsCTQ]                     BIT                      NULL,
  [EffectiveDate]             DATE                     NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_InspectionStandard PRIMARY KEY CLUSTERED ([InspStdID])
);
GO

-- ── MD_Vendor  (거래선 (MD-07))
CREATE TABLE dbo.MD_Vendor (
  [VendorID]                  VARCHAR(20)          NOT NULL,
  [VendorName]                NVARCHAR(80)             NULL,
  [VendorType]                VARCHAR(10)              NULL,
  [VendorCategory]            NVARCHAR(30)             NULL,
  [BizRegNo]                  VARCHAR(20)              NULL,
  [ContactPerson]             NVARCHAR(40)             NULL,
  [Phone]                     VARCHAR(20)              NULL,
  [Email]                     VARCHAR(60)              NULL,
  [ScmURL]                    VARCHAR(255)             NULL,
  [EdiFlag]                   BIT                      NULL,
  [OtdTargetRate]             DECIMAL(5,2)             NULL,
  [PaymentTerms]              VARCHAR(30)              NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Vendor PRIMARY KEY CLUSTERED ([VendorID])
);
GO

-- ── MD_Equipment  (설비 (MD-08))
CREATE TABLE dbo.MD_Equipment (
  [EquipID]                   VARCHAR(20)          NOT NULL,
  [EquipName]                 NVARCHAR(50)             NULL,
  [EquipType]                 VARCHAR(16)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [WCID]                      VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [MakerModel]                NVARCHAR(60)             NULL,
  [InstallDate]               DATE                     NULL,
  [TheoreticalCycle]          DECIMAL(8,2)             NULL,
  [TargetOEE]                 DECIMAL(5,2)             NULL,
  [MoldCompatJSON]            NVARCHAR(MAX)            NULL,
  [PlcAddress]                VARCHAR(40)              NULL,
  [Status]                    VARCHAR(8)               NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Equipment PRIMARY KEY CLUSTERED ([EquipID])
);
GO

-- ── MD_Mold  (금형 (MD-09))
CREATE TABLE dbo.MD_Mold (
  [MoldID]                    VARCHAR(20)          NOT NULL,
  [MoldName]                  NVARCHAR(50)             NULL,
  [CompatItemsJSON]           NVARCHAR(MAX)            NULL,
  [RatedShots]                INT                      NULL,
  [CurrentShots]              INT                      NULL,
  [CavityCount]               INT                      NULL,
  [Tonnage]                   INT                      NULL,
  [StorageLoc]                VARCHAR(20)              NULL,
  [LastMaintDate]             DATE                     NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Mold PRIMARY KEY CLUSTERED ([MoldID])
);
GO

-- ── MD_PaintFabric  (도료·원단 LOT (MD-10))
CREATE TABLE dbo.MD_PaintFabric (
  [MatLotID]                  VARCHAR(24)          NOT NULL,
  [MatCode]                   VARCHAR(20)              NULL,
  [MatName]                   NVARCHAR(60)             NULL,
  [MatType]                   VARCHAR(14)              NULL,
  [LotNo]                     VARCHAR(24)              NULL,
  [SupplierID]                VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [UOM]                       VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [QtyOnHand]                 DECIMAL(12,3)            NULL,
  [ReceiptDate]               DATE                     NULL,
  [ExpDate]                   DATE                     NULL,
  [StorageReq]                NVARCHAR(40)             NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_PaintFabric PRIMARY KEY CLUSTERED ([MatLotID])
);
GO

-- ── MD_ShipmentDest  (출하처 (MD-11))
CREATE TABLE dbo.MD_ShipmentDest (
  [ShipDestID]                VARCHAR(20)          NOT NULL,
  [CustomerID]                VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [DestName]                  NVARCHAR(80)             NULL,
  [DestType]                  VARCHAR(10)              NULL,
  [Address]                   NVARCHAR(200)            NULL,
  [Country]                   CHAR(3)                  NULL,
  [DeliveryDock]              VARCHAR(20)              NULL,
  [LeadTimeDays]              INT                      NULL,
  [DefaultCarrier]            NVARCHAR(40)             NULL,
  [DeliveryWindow]            VARCHAR(40)              NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_ShipmentDest PRIMARY KEY CLUSTERED ([ShipDestID])
);
GO

-- ── MD_Customer  (고객사 (MD-12))
CREATE TABLE dbo.MD_Customer (
  [CustomerID]                VARCHAR(20)          NOT NULL,
  [CustomerCode]              VARCHAR(20)              NULL,
  [CustomerName]              NVARCHAR(80)             NULL,
  [CustomerNameEn]            NVARCHAR(80)             NULL,
  [CustomerType]              VARCHAR(12)              NULL,
  [BizRegNo]                  VARCHAR(20)              NULL,
  [Country]                   CHAR(3)                  NULL,
  [ContactPerson]             NVARCHAR(40)             NULL,
  [ContactPhone]              VARCHAR(20)              NULL,
  [ContactEmail]              VARCHAR(60)              NULL,
  [EDIFlag]                   BIT                      NULL,
  [CurrencyCode]              CHAR(3)                  NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Customer PRIMARY KEY CLUSTERED ([CustomerID])
);
GO

-- ── MD_Uom  (단위 (MD-13))
CREATE TABLE dbo.MD_Uom (
  [UOMCode]                   VARCHAR(10)          NOT NULL,
  [UOMName]                   NVARCHAR(30)             NULL,
  [UOMCategory]               VARCHAR(10)              NULL,
  [BaseFlag]                  BIT                      NULL,
  [BaseUOM]                   VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [ConvFactor]                DECIMAL(18,8)            NULL,
  [DecimalPrec]               INT                      NULL,
  [Symbol]                    NVARCHAR(8)              NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Uom PRIMARY KEY CLUSTERED ([UOMCode])
);
GO

-- ── MD_Calendar  (공장 캘린더 (MD-14))
CREATE TABLE dbo.MD_Calendar (
  [PlantCode]                 VARCHAR(20)          NOT NULL,
  [CalendarDate]              DATE                 NOT NULL,
  [DayType]                   VARCHAR(10)              NULL,
  [HolidayName]               NVARCHAR(40)             NULL,
  [ShiftCount]                INT                      NULL,
  [ShiftPattern]              VARCHAR(20)              NULL,
  [WorkHours]                 DECIMAL(5,2)             NULL,
  [CalendarYear]              INT                      NULL,
  [Note]                      NVARCHAR(120)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Calendar PRIMARY KEY CLUSTERED ([PlantCode], [CalendarDate])
);
GO

-- ── MD_Jig  (지그 (MD-15))
CREATE TABLE dbo.MD_Jig (
  [JigID]                     VARCHAR(20)          NOT NULL,
  [JigName]                   NVARCHAR(50)             NULL,
  [HangerCount]               INT                      NULL,
  [CompatItemsJSON]           NVARCHAR(MAX)            NULL,
  [RatedCycle]                INT                      NULL,
  [CycleCount]                INT                      NULL,
  [ReadFailRate]              DECIMAL(5,2)             NULL,
  [HealthStatus]              VARCHAR(8)               NULL,
  [LastServiceDate]           DATE                     NULL,
  [LastUsedTS]                DATETIME2                NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Jig PRIMARY KEY CLUSTERED ([JigID])
);
GO

-- ── MD_RfidTag  (RFID 태그 (MD-16))
CREATE TABLE dbo.MD_RfidTag (
  [TagID]                     VARCHAR(24)          NOT NULL,
  [EPC]                       VARCHAR(32)              NULL,
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [TagRole]                   VARCHAR(6)               NULL,
  [HeatRating]                INT                      NULL,
  [MountPos]                  NVARCHAR(20)             NULL,
  [InstallDate]               DATE                     NULL,
  [CycleCount]                INT                      NULL,
  [ReplaceSchedule]           DATE                     NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_RfidTag PRIMARY KEY CLUSTERED ([TagID])
);
GO

-- ── MD_RalColor  (RAL 컬러 (MD-17))
CREATE TABLE dbo.MD_RalColor (
  [RALCode]                   VARCHAR(12)          NOT NULL,
  [ColorName]                 NVARCHAR(40)             NULL,
  [HexValue]                  VARCHAR(7)               NULL,
  [CurrentPowderLot]          VARCHAR(24)              NULL,  -- FK -> MD_PaintFabric.MatLotID
  [CureTemp]                  INT                      NULL,
  [CureDuration]              INT                      NULL,
  [ElectroV]                  INT                      NULL,
  [ParticleUm]                DECIMAL(5,1)             NULL,
  [CustomerMapJSON]           NVARCHAR(MAX)            NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_RalColor PRIMARY KEY CLUSTERED ([RALCode])
);
GO

-- ── MD_Oven  (오븐 (MD-18))
CREATE TABLE dbo.MD_Oven (
  [OvenID]                    VARCHAR(20)          NOT NULL,
  [OvenName]                  NVARCHAR(50)             NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ZoneCount]                 INT                      NULL,
  [TargetTemp]                INT                      NULL,
  [Tolerance]                 INT                      NULL,
  [DwellSec]                  INT                      NULL,
  [ConveyorSpeed]             DECIMAL(6,2)             NULL,
  [MaxLoadKg]                 DECIMAL(8,1)             NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Oven PRIMARY KEY CLUSTERED ([OvenID])
);
GO

-- ── MD_RfidReader  (RFID 리더 (MD-19))
CREATE TABLE dbo.MD_RfidReader (
  [ReaderID]                  VARCHAR(20)          NOT NULL,
  [ReaderName]                NVARCHAR(50)             NULL,
  [GateLocation]              VARCHAR(4)               NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [AntennaCount]              INT                      NULL,
  [PowerDbm]                  INT                      NULL,
  [PeTriggerFlag]             BIT                      NULL,
  [WindowMs]                  INT                      NULL,
  [IpAddress]                 VARCHAR(45)              NULL,
  [FirmwareVer]               VARCHAR(16)              NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_RfidReader PRIMARY KEY CLUSTERED ([ReaderID])
);
GO

-- ── MD_Line  (생산 라인 (MD-20))
CREATE TABLE dbo.MD_Line (
  [LineID]                    VARCHAR(20)          NOT NULL,
  [LineName]                  NVARCHAR(50)             NULL,
  [LineNameEn]                NVARCHAR(50)             NULL,
  [LineType]                  VARCHAR(16)              NULL,
  [PlantCode]                 VARCHAR(20)              NULL,
  [DefaultWCID]               VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [DailyCap]                  INT                      NULL,
  [ShiftPattern]              VARCHAR(20)              NULL,
  [RfidEnabledFlag]           BIT                      NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Line PRIMARY KEY CLUSTERED ([LineID])
);
GO

-- ── MD_Station  (공정 기준정보 (MD-02))
CREATE TABLE dbo.MD_Station (
  [StationCode]               VARCHAR(20)          NOT NULL,
  [StationName]               NVARCHAR(60)             NULL,
  [StationNameEn]             NVARCHAR(60)             NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [StationType]               VARCHAR(20)              NULL,
  [ProcessCode]               VARCHAR(10)              NULL,
  [OrderSeq]                  INT                      NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]                NVARCHAR(450)            NULL,
  [ModifiedTS]                DATETIME2                NULL,
  CONSTRAINT PK_MD_Station PRIMARY KEY CLUSTERED ([StationCode])
);
GO

-- ── MD_DefectCode  (불량 코드 (MD-21))
CREATE TABLE dbo.MD_DefectCode (
  [DefectCode]                VARCHAR(16)          NOT NULL,
  [DefectName]                NVARCHAR(60)             NULL,
  [DefectNameEn]              NVARCHAR(60)             NULL,
  [ProcessCode]               VARCHAR(10)              NULL,
  [DefectCategory]            VARCHAR(14)              NULL,
  [SeverityLevel]             VARCHAR(8)               NULL,
  [DispositionDefault]        VARCHAR(10)              NULL,
  [DefaultCauseCode]          VARCHAR(16)              NULL,  -- FK -> MD_DefectCause.CauseCode
  [ParetoFlag]                BIT                      NULL,
  [ImageRef]                  VARCHAR(120)             NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_DefectCode PRIMARY KEY CLUSTERED ([DefectCode])
);
GO

-- ── MD_DefectCause  (불량 원인 (MD-22))
CREATE TABLE dbo.MD_DefectCause (
  [CauseCode]                 VARCHAR(16)          NOT NULL,
  [CauseName]                 NVARCHAR(60)             NULL,
  [CauseCategory]             VARCHAR(9)               NULL,
  [ParentCauseCode]           VARCHAR(16)              NULL,  -- FK -> MD_DefectCause.CauseCode
  [ProcessCode]               VARCHAR(10)              NULL,
  [RootCauseFlag]             BIT                      NULL,
  [CorrectiveGuide]           NVARCHAR(200)            NULL,
  [ResponsibleDept]           NVARCHAR(30)             NULL,
  [SortOrder]                 INT                      NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_DefectCause PRIMARY KEY CLUSTERED ([CauseCode])
);
GO

-- ── MD_PackagingSpec  (포장 사양 (MD-23))
CREATE TABLE dbo.MD_PackagingSpec (
  [PackSpecID]                VARCHAR(20)          NOT NULL,
  [ItemID]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [PackType]                  VARCHAR(12)              NULL,
  [QtyPerInner]               INT                      NULL,
  [InnerPerOuter]             INT                      NULL,
  [OuterPerPallet]            INT                      NULL,
  [NetWeightKg]               DECIMAL(8,3)             NULL,
  [GrossWeightKg]             DECIMAL(8,3)             NULL,
  [DimLxWxH]                  VARCHAR(30)              NULL,
  [ReturnableFlag]            BIT                      NULL,
  [LabelTemplateID]           VARCHAR(20)              NULL,  -- FK -> MD_LabelTemplate.LabelTemplateID
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_PackagingSpec PRIMARY KEY CLUSTERED ([PackSpecID])
);
GO

-- ── MD_LabelTemplate  (라벨 템플릿 (MD-24))
CREATE TABLE dbo.MD_LabelTemplate (
  [LabelTemplateID]           VARCHAR(20)          NOT NULL,
  [TemplateName]              NVARCHAR(60)             NULL,
  [LabelType]                 VARCHAR(12)              NULL,
  [PaperSize]                 VARCHAR(12)              NULL,
  [BarcodeType]               VARCHAR(12)              NULL,
  [LayoutZPL]                 NVARCHAR(MAX)            NULL,
  [FieldMapJSON]              NVARCHAR(MAX)            NULL,
  [CustomerID]                VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [Version]                   INT                      NULL,
  [PrinterModel]              VARCHAR(30)              NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_LabelTemplate PRIMARY KEY CLUSTERED ([LabelTemplateID])
);
GO

-- ── MD_ReasonCode  (사유 코드 (MD-25))
CREATE TABLE dbo.MD_ReasonCode (
  [ReasonCode]                VARCHAR(16)          NOT NULL,
  [ReasonName]                NVARCHAR(60)             NULL,
  [ReasonType]                VARCHAR(12)              NULL,
  [AppliesToModule]           VARCHAR(10)              NULL,
  [RequiresComment]           BIT                      NULL,
  [PlannedFlag]               BIT                      NULL,
  [DisplayOrder]              INT                      NULL,
  [Description]               NVARCHAR(120)            NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_ReasonCode PRIMARY KEY CLUSTERED ([ReasonCode])
);
GO

-- ── MD_CodeGroup  (공통코드 그룹 (MD-26a))
CREATE TABLE dbo.MD_CodeGroup (
  [GroupCode]                 VARCHAR(20)          NOT NULL,
  [GroupName]                 NVARCHAR(60)             NULL,
  [GroupNameEn]               NVARCHAR(60)             NULL,
  [Description]               NVARCHAR(200)            NULL,
  [UseFlag]                   BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_CodeGroup PRIMARY KEY CLUSTERED ([GroupCode])
);
GO

-- ── MD_CodeItem  (공통코드 항목 (MD-26b))
CREATE TABLE dbo.MD_CodeItem (
  [CodeID]                    VARCHAR(41)          NOT NULL,  -- GroupCode(20)+'_'+CodeValue(20)
  [GroupCode]                 VARCHAR(20)              NULL,  -- FK -> MD_CodeGroup.GroupCode
  [CodeValue]                 VARCHAR(20)              NULL,
  [CodeName]                  NVARCHAR(60)             NULL,
  [CodeNameEn]                NVARCHAR(60)             NULL,
  [ParentCodeID]              VARCHAR(41)              NULL,  -- FK -> MD_CodeItem.CodeID
  [SortOrder]                 INT                      NULL,
  [Attribute1]                NVARCHAR(40)             NULL,
  [UseFlag]                   BIT                      NULL DEFAULT 1,
  [Description]               NVARCHAR(120)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_CodeItem PRIMARY KEY CLUSTERED ([CodeID])
);
GO

-- ── MD_SparePart  (정비 자재 (MD-27))
CREATE TABLE dbo.MD_SparePart (
  [PartNo]                    VARCHAR(20)          NOT NULL,
  [PartName]                  NVARCHAR(60)             NULL,
  [Category]                  VARCHAR(16)              NULL,
  [CompatEquipJSON]           NVARCHAR(MAX)            NULL,
  [UnitCost]                  DECIMAL(12,2)            NULL,
  [UOM]                       VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [SafetyStock]               INT                      NULL,
  [ReorderPoint]              INT                      NULL,
  [ReorderQty]                INT                      NULL,
  [LeadTimeDays]              INT                      NULL,
  [SupplierID]                VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [StorageLoc]                VARCHAR(20)              NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_SparePart PRIMARY KEY CLUSTERED ([PartNo])
);
GO

-- ── MD_PmTemplate  (PM 템플릿 (MD-28a))
CREATE TABLE dbo.MD_PmTemplate (
  [PMTemplateID]              VARCHAR(20)          NOT NULL,
  [TemplateName]              NVARCHAR(60)             NULL,
  [EquipType]                 VARCHAR(16)              NULL,
  [CycleBasis]                VARCHAR(10)              NULL,
  [IntervalValue]             INT                      NULL,
  [IntervalUnit]              VARCHAR(8)               NULL,
  [StdDurationMin]            INT                      NULL,
  [SafetyLOTOFlag]            BIT                      NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_PmTemplate PRIMARY KEY CLUSTERED ([PMTemplateID])
);
GO

-- ── MD_PmTemplateStep  (PM 점검 항목 (MD-28b))
CREATE TABLE dbo.MD_PmTemplateStep (
  [PMStepID]                  VARCHAR(24)          NOT NULL,
  [PMTemplateID]              VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [StepSeq]                   INT                      NULL,
  [StepDescription]           NVARCHAR(200)            NULL,
  [AcceptanceCriteria]        NVARCHAR(200)            NULL,
  [RequiredPartNo]            VARCHAR(20)              NULL,  -- FK -> MD_SparePart.PartNo
  [RequiredQty]               DECIMAL(10,3)            NULL,
  [StepDurationMin]           INT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_PmTemplateStep PRIMARY KEY CLUSTERED ([PMStepID])
);
GO

-- ── MD_LineTimePattern  (시간패턴 헤더 (MD-29a))
CREATE TABLE dbo.MD_LineTimePattern (
  [PatternID]                 VARCHAR(20)          NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PatternName]               NVARCHAR(50)             NULL,
  [DayType]                   VARCHAR(10)              NULL,
  [ShiftModel]                VARCHAR(12)              NULL,
  [EffectiveFrom]             DATE                     NULL,
  [EffectiveTo]               DATE                     NULL,
  [TotalOperatingMin]         INT                      NULL,
  [TotalPlannedDownMin]       INT                      NULL,
  [TimeZone]                  VARCHAR(20)              NULL,
  [Status]                    VARCHAR(8)               NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_LineTimePattern PRIMARY KEY CLUSTERED ([PatternID])
);
GO

-- ── MD_LineTimeSegment  (시간 세그먼트 (MD-29b))
CREATE TABLE dbo.MD_LineTimeSegment (
  [SegmentID]                 VARCHAR(24)          NOT NULL,
  [PatternID]                 VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [SeqNo]                     INT                      NULL,
  [StartMin]                  SMALLINT                 NULL,
  [EndMin]                    SMALLINT                 NULL,
  [SegmentState]              VARCHAR(14)              NULL,
  [ReasonCode]                VARCHAR(16)              NULL,  -- FK -> MD_ReasonCode.ReasonCode
  [ShiftCode]                 VARCHAR(10)              NULL,
  [Description]               NVARCHAR(60)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_LineTimeSegment PRIMARY KEY CLUSTERED ([SegmentID])
);
GO

-- ── MD_Location  (로케이션 (보조))
CREATE TABLE dbo.MD_Location (
  [LocationID]                VARCHAR(20)          NOT NULL,
  [LocationName]              NVARCHAR(60)             NULL,
  [ZoneCode]                  VARCHAR(10)              NULL,
  [Aisle]                     VARCHAR(5)               NULL,
  [Bay]                       VARCHAR(5)               NULL,
  [Slot]                      VARCHAR(5)               NULL,
  [Capacity]                  DECIMAL(10,3)            NULL,
  [LocationType]              VARCHAR(20)              NULL,
  [PlantCode]                 VARCHAR(20)              NULL,
  [ActiveFlag]                BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Location PRIMARY KEY CLUSTERED ([LocationID])
);
GO

-- ── MD_Recipe  (레시피 (보조))
CREATE TABLE dbo.MD_Recipe (
  [RecipeID]                  VARCHAR(20)          NOT NULL,
  [RecipeName]                NVARCHAR(60)             NULL,
  [RecipeType]                VARCHAR(15)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CycleTime]                 INT                      NULL,
  [ParamsJSON]                NVARCHAR(MAX)            NULL,
  [Version]                   VARCHAR(10)              NULL,
  [EffectiveDate]             DATE                     NULL,
  [Status]                    VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_Recipe PRIMARY KEY CLUSTERED ([RecipeID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: WH                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── WH_PurchaseOrder  (구매발주 (SCM))
CREATE TABLE dbo.WH_PurchaseOrder (
  [PoID]                      INT IDENTITY         NOT NULL,
  [PoNumber]                  VARCHAR(20)              NULL,
  [PoLineNo]                  INT                      NULL,
  [VendorID]                  VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty]                  DECIMAL(12,3)            NULL,
  [ReceivedQty]               DECIMAL(12,3)            NULL,
  [UnitCode]                  VARCHAR(10)              NULL,
  [UnitPrice]                 DECIMAL(14,4)            NULL,
  [Currency]                  CHAR(3)                  NULL,
  [OrderDate]                 DATE                     NULL,
  [DueDate]                   DATE                     NULL,
  [Status]                    VARCHAR(20)              NULL,
  [SapSyncedAt]               DATETIME2                NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_PurchaseOrder PRIMARY KEY CLUSTERED ([PoID])
);
GO

-- ── WH_Receiving  (입고 실적)
CREATE TABLE dbo.WH_Receiving (
  [ReceivingID]               INT IDENTITY         NOT NULL,
  [ReceivingNo]               VARCHAR(24)              NULL,
  [PoID]                      INT                      NULL,  -- FK -> WH_PurchaseOrder.PoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VendorID]                  VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [ReceivedQty]               DECIMAL(12,3)            NULL,
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotCode]                   VARCHAR(40)              NULL,
  [ReceivedAt]                DATETIME2                NULL,
  [ReceivedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID]                VARCHAR(20)              NULL,
  [QcStatus]                  VARCHAR(20)              NULL,
  [LabelPrinted]              BIT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_Receiving PRIMARY KEY CLUSTERED ([ReceivingID])
);
GO

-- ── WH_Inventory  (현재고)
CREATE TABLE dbo.WH_Inventory (
  [InventoryID]               INT IDENTITY         NOT NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [OnHandQty]                 DECIMAL(14,3)            NULL,
  [ReservedQty]               DECIMAL(14,3)            NULL,
  [UnitCost]                  DECIMAL(14,4)            NULL,
  [LastReceivedAt]            DATETIME2                NULL,
  [ExpiryDate]                DATE                     NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_Inventory PRIMARY KEY CLUSTERED ([InventoryID])
);
GO

-- ── WH_InventorySnapshot  (재고 일일 스냅샷)
CREATE TABLE dbo.WH_InventorySnapshot (
  [SnapshotID]                BIGINT IDENTITY      NOT NULL,
  [SnapshotDate]              DATE                     NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [OnHandQty]                 DECIMAL(14,3)            NULL,
  [UnitCost]                  DECIMAL(14,4)            NULL,
  [TotalValue]                DECIMAL(16,2)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_InventorySnapshot PRIMARY KEY CLUSTERED ([SnapshotID])
);
GO

-- ── WH_InventoryAdjust  (재고 조정)
CREATE TABLE dbo.WH_InventoryAdjust (
  [AdjustID]                  INT IDENTITY         NOT NULL,
  [AdjustNo]                  VARCHAR(24)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [QtyBefore]                 DECIMAL(14,3)            NULL,
  [Delta]                     DECIMAL(14,3)            NULL,
  [QtyAfter]                  DECIMAL(14,3)            NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [ReasonNote]                NVARCHAR(500)            NULL,
  [Status]                    VARCHAR(20)              NULL,
  [RequestedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_InventoryAdjust PRIMARY KEY CLUSTERED ([AdjustID])
);
GO

-- ── WH_ReleaseSchedule  (출고 예정 (WO 수요))
CREATE TABLE dbo.WH_ReleaseSchedule (
  [ReleaseScheduleID]         INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [DemandQty]                 DECIMAL(14,3)            NULL,
  [PickedQty]                 DECIMAL(14,3)            NULL,
  [RequiredAt]                DATETIME2                NULL,
  [Priority]                  TINYINT                  NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_ReleaseSchedule PRIMARY KEY CLUSTERED ([ReleaseScheduleID])
);
GO

-- ── WH_ReleasePicking  (출고 피킹)
CREATE TABLE dbo.WH_ReleasePicking (
  [PickingID]                 INT IDENTITY         NOT NULL,
  [PickingNo]                 VARCHAR(24)              NULL,
  [ReleaseScheduleID]         INT                      NULL,  -- FK -> WH_ReleaseSchedule.ReleaseScheduleID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PickedQty]                 DECIMAL(14,3)            NULL,
  [DestLineID]                VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PickedAt]                  DATETIME2                NULL,
  [PickedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID]                VARCHAR(20)              NULL,
  [FifoOverride]              BIT                      NULL,
  [OverrideReason]            NVARCHAR(200)            NULL,
  [OverrideApprover]          NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_ReleasePicking PRIMARY KEY CLUSTERED ([PickingID])
);
GO

-- ── WH_TransactionHistory  (입출고 트랜잭션 (append-only))
CREATE TABLE dbo.WH_TransactionHistory (
  [TxnID]                     BIGINT IDENTITY      NOT NULL,
  [TxnTime]                   DATETIME2                NULL DEFAULT SYSDATETIME(),
  [TxnType]                   VARCHAR(10)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID]                VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [QtyBefore]                 DECIMAL(14,3)            NULL,
  [Delta]                     DECIMAL(14,3)            NULL,
  [QtyAfter]                  DECIMAL(14,3)            NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [RefDocType]                VARCHAR(20)              NULL,
  [RefDocID]                  INT                      NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApproverID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Note]                      NVARCHAR(500)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_WH_TransactionHistory PRIMARY KEY CLUSTERED ([TxnID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PP                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── PP_Forecast  (수요예측)
CREATE TABLE dbo.PP_Forecast (
  [ForecastID]                INT IDENTITY         NOT NULL,
  [ForecastBatch]             VARCHAR(20)              NULL,
  [CustomerID]                VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [ForecastMonth]             DATE                     NULL,
  [ForecastQty]               DECIMAL(14,3)            NULL,
  [Confidence]                VARCHAR(10)              NULL,
  [Source]                    VARCHAR(20)              NULL,
  [ImportedAt]                DATETIME2                NULL,
  [ImportedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [WeekStartDate]             DATE                     NULL,  -- 주간 계획: 주 시작일 (월별 행은 NULL)
  [WeekLabel]                 VARCHAR(10)              NULL,  -- 예: '[28/1W]'
  [BaseInv]                   DECIMAL(14,3)            NULL,  -- Base Inv. (품목당, 비정규화)
  [PartName]                  NVARCHAR(100)            NULL,  -- 업체 품명 (MD_Item 미등록 대비)
  [Unit]                      VARCHAR(10)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_Forecast PRIMARY KEY CLUSTERED ([ForecastID])
);
GO
SET QUOTED_IDENTIFIER ON;  -- 필터드 인덱스 필수 (sqlcmd 기본값 OFF)
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_PP_Forecast_Cust_Item_Week
  ON dbo.PP_Forecast (CustomerID, ItemNo, WeekStartDate)
  WHERE WeekStartDate IS NOT NULL;
GO

-- ── PP_ForecastHistory  (예측 이력)
CREATE TABLE dbo.PP_ForecastHistory (
  [HistoryID]                 BIGINT IDENTITY      NOT NULL,
  [ForecastID]                INT                      NULL,  -- FK -> PP_Forecast.ForecastID
  [PrevBatch]                 VARCHAR(20)              NULL,
  [PrevQty]                   DECIMAL(14,3)            NULL,
  [NewQty]                    DECIMAL(14,3)            NULL,
  [ChangedAt]                 DATETIME2                NULL,
  [ChangedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_ForecastHistory PRIMARY KEY CLUSTERED ([HistoryID])
);
GO

-- ── PP_CustomerOrder  (수주 (SO))
CREATE TABLE dbo.PP_CustomerOrder (
  [SoID]                      INT IDENTITY         NOT NULL,
  [SoNumber]                  VARCHAR(20)              NULL,
  [SoLineNo]                  INT                      NULL,
  [CustomerID]                VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty]                  DECIMAL(14,3)            NULL,
  [ShippedQty]                DECIMAL(14,3)            NULL,
  [OrderDate]                 DATE                     NULL,
  [RequestedDeliveryDate]     DATE                     NULL,
  [PromisedDate]              DATE                     NULL,
  [Status]                    VARCHAR(20)              NULL,
  [SapSyncedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_CustomerOrder PRIMARY KEY CLUSTERED ([SoID])
);
GO

-- ── PP_SupplyPlan  (공급계획 헤더)
CREATE TABLE dbo.PP_SupplyPlan (
  [PlanID]                    INT IDENTITY         NOT NULL,
  [PlanCode]                  VARCHAR(20)              NULL,
  [PlanPeriod]                DATE                     NULL,
  [Status]                    VARCHAR(20)              NULL,
  [ConfirmedAt]               DATETIME2                NULL,
  [ConfirmedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SapImportBatch]            VARCHAR(40)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_SupplyPlan PRIMARY KEY CLUSTERED ([PlanID])
);
GO

-- ── PP_SupplyPlanDetail  (공급계획 상세)
CREATE TABLE dbo.PP_SupplyPlanDetail (
  [PlanDetailID]              INT IDENTITY         NOT NULL,
  [PlanID]                    INT                      NULL,  -- FK -> PP_SupplyPlan.PlanID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [PlannedQty]                DECIMAL(14,3)            NULL,
  [FgOnHand]                  DECIMAL(14,3)            NULL,
  [NetRequirement]            DECIMAL(14,3)            NULL,
  [DueDate]                   DATE                     NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_SupplyPlanDetail PRIMARY KEY CLUSTERED ([PlanDetailID])
);
GO

-- ── PP_WorkOrder  (★ 작업지시 (WO))
CREATE TABLE dbo.PP_WorkOrder (
  [WoID]                      INT IDENTITY         NOT NULL,
  [WoNumber]                  VARCHAR(20)              NULL,
  [PlanID]                    INT                      NULL,  -- FK -> PP_SupplyPlan.PlanID
  [SoID]                      INT                      NULL,  -- FK -> PP_CustomerOrder.SoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty]                  DECIMAL(14,3)            NULL,
  [OpenQty]                   DECIMAL(14,3)            NULL,
  [CompletedQty]              DECIMAL(14,3)            NULL,
  [ScrapQty]                  DECIMAL(14,3)            NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [RecipeID]                  VARCHAR(20)              NULL,  -- FK -> MD_Recipe.RecipeID
  [BomVersion]                VARCHAR(10)              NULL,
  [BopVersion]                VARCHAR(10)              NULL,
  [Routing]                   CHAR(1)                  NULL,
  [PlannedStart]              DATETIME2                NULL,
  [PlannedEnd]                DATETIME2                NULL,
  [ActualStart]               DATETIME2                NULL,
  [ActualEnd]                 DATETIME2                NULL,
  [DueDate]                   DATE                     NULL,
  [Status]                    VARCHAR(20)              NULL,
  [TerminalLock]              VARCHAR(20)              NULL,
  [Priority]                  TINYINT                  NULL,
  [ReleasedAt]                DATETIME2                NULL,
  [ReleasedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_WorkOrder PRIMARY KEY CLUSTERED ([WoID])
);
GO
SET QUOTED_IDENTIFIER ON;  -- 필터드 인덱스 필수 (sqlcmd 기본값 OFF)
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrder_WoNumber
  ON dbo.PP_WorkOrder (WoNumber)
  WHERE WoNumber IS NOT NULL;
GO

-- ── PP_WorkOrderRouting  (WO 라우팅 (BOP 스냅샷))
CREATE TABLE dbo.PP_WorkOrderRouting (
  [RoutingLineID]             INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StepSeq]                   TINYINT                  NULL,
  [ProcessCode]               VARCHAR(10)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [StdCycleSec]               INT                      NULL,
  [StdYieldPct]               DECIMAL(5,2)             NULL,
  [Status]                    VARCHAR(20)              NULL,
  [ActualStart]               DATETIME2                NULL,
  [ActualEnd]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_WorkOrderRouting PRIMARY KEY CLUSTERED ([RoutingLineID])
);
GO

-- ── PP_MaterialReservation  (WO 자재 예약)
CREATE TABLE dbo.PP_MaterialReservation (
  [ReservationID]             INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RequiredQty]               DECIMAL(14,3)            NULL,
  [ReservedQty]               DECIMAL(14,3)            NULL,
  [IssuedQty]                 DECIMAL(14,3)            NULL,
  [RequiredAt]                DATETIME2                NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_MaterialReservation PRIMARY KEY CLUSTERED ([ReservationID])
);
GO

-- ── PP_PurchaseRequest  (구매요청 (MRP 결과))
CREATE TABLE dbo.PP_PurchaseRequest (
  [PrID]                      INT IDENTITY         NOT NULL,
  [PrNumber]                  VARCHAR(20)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VendorID]                  VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [RequiredQty]               DECIMAL(14,3)            NULL,
  [RequiredDate]              DATE                     NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [Status]                    VARCHAR(20)              NULL,
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt]                DATETIME2                NULL,
  [SapPoNumber]               VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_PurchaseRequest PRIMARY KEY CLUSTERED ([PrID])
);
GO

-- ── PP_PRSendLog  (PR SAP 송신 로그)
CREATE TABLE dbo.PP_PRSendLog (
  [SendLogID]                 BIGINT IDENTITY      NOT NULL,
  [PrID]                      INT                      NULL,  -- FK -> PP_PurchaseRequest.PrID
  [AttemptNo]                 TINYINT                  NULL,
  [SentAt]                    DATETIME2                NULL,
  [Endpoint]                  VARCHAR(200)             NULL,
  [RequestPayload]            NVARCHAR(MAX)            NULL,
  [ResponseCode]              INT                      NULL,
  [ResponsePayload]           NVARCHAR(MAX)            NULL,
  [Result]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_PRSendLog PRIMARY KEY CLUSTERED ([SendLogID])
);
GO

-- ── PP_MRPLog  (MRP 실행 로그)
CREATE TABLE dbo.PP_MRPLog (
  [MrpRunID]                  INT IDENTITY         NOT NULL,
  [RunAt]                     DATETIME2                NULL,
  [RunBy]                     NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [HorizonStart]              DATE                     NULL,
  [HorizonEnd]                DATE                     NULL,
  [WosConsidered]             INT                      NULL,
  [PrsCreated]                INT                      NULL,
  [ShortageCount]             INT                      NULL,
  [DurationMs]                INT                      NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_MRPLog PRIMARY KEY CLUSTERED ([MrpRunID])
);
GO

-- ── PP_LineSchedule  (라인 스케줄 (LSB))
CREATE TABLE dbo.PP_LineSchedule (
  [ScheduleID]                INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ScheduleDate]              DATE                     NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StartMin]                  SMALLINT                 NULL,
  [EndMin]                    SMALLINT                 NULL,
  [PlannedQty]                DECIMAL(14,3)            NULL,
  [PatternID]                 VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [Status]                    VARCHAR(20)              NULL,
  [PublishedAt]               DATETIME2                NULL,
  [PublishedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_LineSchedule PRIMARY KEY CLUSTERED ([ScheduleID])
);
GO

-- ── PP_LineStateLog  (라인 상태 분단위 (ODM))
CREATE TABLE dbo.PP_LineStateLog (
  [StateLogID]                BIGINT IDENTITY      NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [MinuteTS]                  DATETIME2                NULL,
  [State]                     VARCHAR(20)              NULL,
  [PlanState]                 VARCHAR(20)              NULL,
  [RunFlag]                   BIT                      NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ClassifiedAt]              DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_LineStateLog PRIMARY KEY CLUSTERED ([StateLogID])
);
GO

-- ── PP_LineDowntimeLog  (비가동 사유 (DTL))
CREATE TABLE dbo.PP_LineDowntimeLog (
  [DowntimeID]                INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [StartTS]                   DATETIME2                NULL,
  [EndTS]                     DATETIME2                NULL,
  [DurationMin]               INT                      NULL,
  [ReasonCode]                VARCHAR(20)              NULL,
  [CauseCode]                 VARCHAR(30)              NULL,
  [Comment]                   NVARCHAR(500)            NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LoggedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AndonID]                   INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_LineDowntimeLog PRIMARY KEY CLUSTERED ([DowntimeID])
);
GO

-- ── PP_LineOEE  (OEE 스냅샷)
CREATE TABLE dbo.PP_LineOEE (
  [OeeSnapshotID]             INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PeriodDate]                DATE                     NULL,
  [ShiftCode]                 VARCHAR(10)              NULL,
  [LoadingMin]                INT                      NULL,
  [PlannedDownMin]            INT                      NULL,
  [UnplannedDownMin]          INT                      NULL,
  [OperatingMin]              INT                      NULL,
  [TotalProducedQty]          DECIMAL(14,3)            NULL,
  [GoodQty]                   DECIMAL(14,3)            NULL,
  [Availability]              DECIMAL(5,4)             NULL,
  [Performance]               DECIMAL(5,4)             NULL,
  [Quality]                   DECIMAL(5,4)             NULL,
  [OEE]                       DECIMAL(5,4)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_LineOEE PRIMARY KEY CLUSTERED ([OeeSnapshotID])
);
GO

-- ── PP_ProductionCalendarOverride  (캘린더 변경)
CREATE TABLE dbo.PP_ProductionCalendarOverride (
  [OverrideID]                INT IDENTITY         NOT NULL,
  [OverrideDate]              DATE                     NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [DayType]                   VARCHAR(20)              NULL,
  [PatternID]                 VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [CapacityFactor]            DECIMAL(5,2)             NULL,
  [Reason]                    NVARCHAR(200)            NULL,
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PP_ProductionCalendarOverride PRIMARY KEY CLUSTERED ([OverrideID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PR                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── tbl_Lot  (★ LOT 마스터 (전 모듈 앵커))
CREATE TABLE dbo.tbl_Lot (
  [LotID]                     INT IDENTITY         NOT NULL,
  [LotCode]                   VARCHAR(40)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode]               VARCHAR(10)              NULL,
  [BatchSize]                 DECIMAL(14,3)            NULL,
  [RemainingQty]              DECIMAL(14,3)            NULL,
  [ParentLotID]               INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ProducedAt]                DATETIME2                NULL,
  [Status]                    VARCHAR(20)              NULL,
  [QualityFlag]               VARCHAR(10)              NULL,
  [CurrentLocationID]         VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [ExpiryDate]                DATE                     NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_tbl_Lot PRIMARY KEY CLUSTERED ([LotID])
);
GO

-- ── PR_PopSession  (POP 로그인 세션)
CREATE TABLE dbo.PR_PopSession (
  [SessionID]                 INT IDENTITY         NOT NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID]                VARCHAR(20)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ShiftCode]                 VARCHAR(10)              NULL,
  [AuthMethod]                VARCHAR(20)              NULL,
  [StartedAt]                 DATETIME2                NULL,
  [ExpiresAt]                 DATETIME2                NULL,
  [LoggedOutAt]               DATETIME2                NULL,
  [LogoutReason]              VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_PopSession PRIMARY KEY CLUSTERED ([SessionID])
);
GO

-- ── PR_PopAuthLog  (POP 인증 감사)
CREATE TABLE dbo.PR_PopAuthLog (
  [AuthLogID]                 BIGINT IDENTITY      NOT NULL,
  [TerminalID]                VARCHAR(20)              NULL,
  [AttemptedID]               VARCHAR(50)              NULL,
  [AuthMethod]                VARCHAR(20)              NULL,
  [Result]                    VARCHAR(10)              NULL,
  [FailReason]                VARCHAR(40)              NULL,
  [AttemptedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_PopAuthLog PRIMARY KEY CLUSTERED ([AuthLogID])
);
GO

-- ── PR_WoAcceptance  (WO 수락 (INJ-03))
CREATE TABLE dbo.PR_WoAcceptance (
  [AcceptID]                  INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [TerminalID]                VARCHAR(20)              NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AcceptedAt]                DATETIME2                NULL,
  [CheckResults]              NVARCHAR(MAX)            NULL,
  [CheckPassed]               BIT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_WoAcceptance PRIMARY KEY CLUSTERED ([AcceptID])
);
GO

-- ── PR_ProductionResult  (★ 생산실적 (사이클별))
CREATE TABLE dbo.PR_ProductionResult (
  [ResultID]                  INT IDENTITY         NOT NULL,
  [EntryNo]                   VARCHAR(28)              NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode]               VARCHAR(10)              NULL,
  [GoodQty]                   INT                      NULL,
  [CycleSec]                  INT                      NULL,
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [FabricRollID]              INT                      NULL,  -- FK -> tbl_Lot.LotID
  [FabricConsumedM]           DECIMAL(8,3)             NULL,
  [BondTempAvg]               DECIMAL(5,1)             NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SessionID]                 INT                      NULL,  -- FK -> PR_PopSession.SessionID
  [DefectFlag]                BIT                      NULL,
  [ReviewFlag]                BIT                      NULL,
  [EntryAt]                   DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_ProductionResult PRIMARY KEY CLUSTERED ([ResultID])
);
GO

-- ── PR_DefectDetail  (불량 상세)
CREATE TABLE dbo.PR_DefectDetail (
  [DefectID]                  INT IDENTITY         NOT NULL,
  [ResultID]                  INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ProcessCode]               VARCHAR(10)              NULL,
  [DefectCode]                VARCHAR(16)              NULL,  -- FK -> MD_DefectCode.DefectCode
  [Qty]                       INT                      NULL,
  [SeqNos]                    NVARCHAR(MAX)            NULL,
  [ReasonNote]                NVARCHAR(500)            NULL,
  [PhotoUrl]                  VARCHAR(300)             NULL,
  [CorrectiveAction]          NVARCHAR(500)            NULL,
  [Disposition]               VARCHAR(20)              NULL,
  [DetectedAt]                DATETIME2                NULL,
  [RegisteredBy]              NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_DefectDetail PRIMARY KEY CLUSTERED ([DefectID])
);
GO

-- ── PR_DefectAutoLink  (불량 자동 원인)
CREATE TABLE dbo.PR_DefectAutoLink (
  [LinkID]                    INT IDENTITY         NOT NULL,
  [DefectID]                  INT                      NULL,  -- FK -> PR_DefectDetail.DefectID
  [LinkType]                  VARCHAR(30)              NULL,
  [RefDocType]                VARCHAR(20)              NULL,
  [RefDocID]                  INT                      NULL,
  [ConfidenceScore]           DECIMAL(4,3)             NULL,
  [LinkedAt]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_DefectAutoLink PRIMARY KEY CLUSTERED ([LinkID])
);
GO

-- ── PR_CycleAnomalyLog  (CT 이탈 로그)
CREATE TABLE dbo.PR_CycleAnomalyLog (
  [AnomalyID]                 INT IDENTITY         NOT NULL,
  [ResultID]                  INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [ExpectedCt]                INT                      NULL,
  [ActualCt]                  INT                      NULL,
  [DeviationPct]              DECIMAL(6,2)             NULL,
  [DetectedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_CycleAnomalyLog PRIMARY KEY CLUSTERED ([AnomalyID])
);
GO

-- ── PR_MoldChange  (금형 교체 (INJ-06))
CREATE TABLE dbo.PR_MoldChange (
  [MoldChangeID]              INT IDENTITY         NOT NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [OldMoldID]                 VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [NewMoldID]                 VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [OldMoldFinalShots]         INT                      NULL,
  [NewMoldStartShots]         INT                      NULL,
  [Reason]                    VARCHAR(20)              NULL,
  [MntWoID]                   INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [DowntimeMin]               INT                      NULL,
  [StartedAt]                 DATETIME2                NULL,
  [CompletedAt]               DATETIME2                NULL,
  [ChangedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_MoldChange PRIMARY KEY CLUSTERED ([MoldChangeID])
);
GO

-- ── PR_ShotCount  (금형 쇼트 이력)
CREATE TABLE dbo.PR_ShotCount (
  [ShotCountID]               BIGINT IDENTITY      NOT NULL,
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [RecordDate]                DATE                     NULL,
  [ShiftCode]                 VARCHAR(10)              NULL,
  [ShotsAdded]                INT                      NULL,
  [CumulativeShots]           INT                      NULL,
  [RatedShots]                INT                      NULL,
  [RecordedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_ShotCount PRIMARY KEY CLUSTERED ([ShotCountID])
);
GO

-- ── PR_EquipStatusLog  (설비 상태 로그 (PLC))
CREATE TABLE dbo.PR_EquipStatusLog (
  [EquipStatusLogID]          BIGINT IDENTITY      NOT NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [Status]                    VARCHAR(20)              NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StartedAt]                 DATETIME2                NULL,
  [DurationSec]               INT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_EquipStatusLog PRIMARY KEY CLUSTERED ([EquipStatusLogID])
);
GO

-- ── PR_AndonCall  (★ 안돈 호출 (5년))
CREATE TABLE dbo.PR_AndonCall (
  [AndonID]                   INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [TriggerSource]             VARCHAR(20)              NULL,
  [RuleID]                    VARCHAR(20)              NULL,
  [Severity]                  VARCHAR(10)              NULL,
  [TriggeredAt]               DATETIME2                NULL,
  [AckedBy]                   NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AckedAt]                   DATETIME2                NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [CorrectiveAction]          NVARCHAR(500)            NULL,
  [ResumedAt]                 DATETIME2                NULL,
  [DowntimeSec]               INT                      NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_AndonCall PRIMARY KEY CLUSTERED ([AndonID])
);
GO

-- ── PR_AndonPush  (안돈 송신 로그)
CREATE TABLE dbo.PR_AndonPush (
  [PushID]                    BIGINT IDENTITY      NOT NULL,
  [AndonID]                   INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [Recipient]                 VARCHAR(100)             NULL,
  [Channel]                   VARCHAR(20)              NULL,
  [SentAt]                    DATETIME2                NULL,
  [DeliveredAt]               DATETIME2                NULL,
  [Result]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_AndonPush PRIMARY KEY CLUSTERED ([PushID])
);
GO

-- ── PR_InjLot  (tbl_Lot 1:1 확장 — 사출 원천 LOT 속성)
CREATE TABLE dbo.PR_InjLot (
  [LotID]                     INT                  NOT NULL,  -- PK & FK -> tbl_Lot.LotID (1:1)
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [MoldCode]                  VARCHAR(20)              NULL,  -- PLC 원문 기준 금형코드 (색상 제외)
  [ColorCode]                 VARCHAR(10)              NULL,
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [CavityNo]                  INT                      NULL,  -- 1 / 2
  [CavityPos]                 VARCHAR(4)               NULL,  -- LH / RH
  [PressType]                 VARCHAR(2)               NULL,  -- 1~5 / M(에이전트)
  [MachineShotCount]          BIGINT                   NULL,  -- PLC 샷카운터 값
  [ConfirmStatus]             VARCHAR(16)          NOT NULL DEFAULT 'RAW',  -- RAW/CONFIRMED/NG_BLOCKED/NG_CONFIRMED
  [ConfirmedAt]               DATETIME2                NULL,
  [ConfirmedBy]               NVARCHAR(450)            NULL,
  [ConfirmedSessionID]        INT                      NULL,
  [PrintedCount]              INT                  NOT NULL DEFAULT 0,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_InjLot PRIMARY KEY CLUSTERED ([LotID]),
  CONSTRAINT FK_PR_InjLot_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID])
);
GO
CREATE INDEX IX_PR_InjLot_Status ON dbo.PR_InjLot([ConfirmStatus], [EquipID]);
GO
CREATE INDEX IX_PR_InjLot_Equip ON dbo.PR_InjLot([EquipID]) INCLUDE([CavityPos], [MachineShotCount]);
GO

-- ── MD_MoldItemMap  (금형코드+색상 → 품번·캐비티, SEOYON APM2120 대응)
CREATE TABLE dbo.MD_MoldItemMap (
  [MapID]                     INT IDENTITY         NOT NULL,
  [MoldCode]                  VARCHAR(20)          NOT NULL,
  [ColorCode]                 VARCHAR(10)          NOT NULL,
  [CavityNo]                  INT                  NOT NULL,  -- 1 / 2
  [CavityPos]                 VARCHAR(4)           NOT NULL,  -- LH / RH
  [ItemNo]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Item.ItemNo
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [ActiveFlag]                BIT                  NOT NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_MoldItemMap PRIMARY KEY CLUSTERED ([MapID]),
  CONSTRAINT UQ_MD_MoldItemMap UNIQUE ([MoldCode], [ColorCode], [CavityNo])
);
GO

-- ── MD_InjCondItem  (사출조건 항목 마스터, SEOYON ZINJ0150 대응)
CREATE TABLE dbo.MD_InjCondItem (
  [CondItemID]                INT IDENTITY         NOT NULL,
  [LineID]                    VARCHAR(20)          NOT NULL,  -- FK -> MD_Line.LineID
  [ItemCode]                  VARCHAR(20)          NOT NULL,
  [ItemName]                  NVARCHAR(50)             NULL,
  [SetAddress]                INT                      NULL,  -- 세팅값 Modbus 주소
  [ActualAddress]             INT                      NULL,  -- 실제값 Modbus 주소
  [DataType]                  VARCHAR(8)           NOT NULL,  -- LONG / FLOAT
  [Enabled]                   BIT                  NOT NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MD_InjCondItem PRIMARY KEY CLUSTERED ([CondItemID]),
  CONSTRAINT UQ_MD_InjCondItem UNIQUE ([LineID], [ItemCode])
);
GO

-- ── PR_InjCondLog  (샷별 사출조건 이력, SEOYON ZINJ0160 대응)
CREATE TABLE dbo.PR_InjCondLog (
  [CondLogID]                 BIGINT IDENTITY      NOT NULL,
  [LineID]                    VARCHAR(20)          NOT NULL,
  [ItemCode]                  VARCHAR(20)          NOT NULL,
  [ShotSeq]                   BIGINT                   NULL,  -- PLC 샷카운터 값
  [SetValue]                  DECIMAL(18,4)            NULL,
  [ActualValue]               DECIMAL(18,4)            NULL,
  [CollectedAt]               DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_InjCondLog PRIMARY KEY CLUSTERED ([CondLogID])
);
GO
CREATE INDEX IX_PR_InjCondLog_Line ON dbo.PR_InjCondLog([LineID], [CollectedAt]);
GO

-- ── PR_RobotInspection  (취출로봇 검사 판정 수신)
CREATE TABLE dbo.PR_RobotInspection (
  [InspectionID]              BIGINT IDENTITY      NOT NULL,
  [LotID]                     INT                  NOT NULL,  -- FK -> tbl_Lot.LotID
  [EquipID]                   VARCHAR(20)              NULL,
  [CavityPos]                 VARCHAR(4)               NULL,
  [ShortMold]                 VARCHAR(4)               NULL,  -- OK/NG/PASS (미성형)
  [WeldLine]                  VARCHAR(4)               NULL,
  [Gas]                       VARCHAR(4)               NULL,
  [Weight]                    VARCHAR(4)               NULL,
  [OverallNg]                 BIT                  NOT NULL DEFAULT 0,
  [ReceivedAt]                DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_RobotInspection PRIMARY KEY CLUSTERED ([InspectionID]),
  CONSTRAINT FK_PR_RobotInspection_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID])
);
GO
CREATE INDEX IX_PR_RobotInspection_Lot ON dbo.PR_RobotInspection([LotID]);
GO

-- ── tbl_Lot.LotCode 유니크 (스캔 확정 seek + 채번 중복 방어)
SET QUOTED_IDENTIFIER ON;  -- 필터드 인덱스 필수 (sqlcmd 기본값 OFF)
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tbl_Lot_LotCode')
  CREATE UNIQUE NONCLUSTERED INDEX UX_tbl_Lot_LotCode ON dbo.tbl_Lot([LotCode]) WHERE [LotCode] IS NOT NULL;
GO

-- ── PR_PlcInterlock  (PLC 인터록)
CREATE TABLE dbo.PR_PlcInterlock (
  [InterlockID]               INT IDENTITY         NOT NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LockedAt]                  DATETIME2                NULL,
  [UnlockedAt]                DATETIME2                NULL,
  [LockReason]                VARCHAR(40)              NULL,
  [AndonID]                   INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_PlcInterlock PRIMARY KEY CLUSTERED ([InterlockID])
);
GO

-- ── PR_ShiftHandover  (교대 인수인계)
CREATE TABLE dbo.PR_ShiftHandover (
  [HandoverID]                INT IDENTITY         NOT NULL,
  [HandoverDate]              DATE                     NULL,
  [ShiftCode]                 VARCHAR(10)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode]               VARCHAR(10)              NULL,
  [SummaryJson]               NVARCHAR(MAX)            NULL,
  [PdfUrl]                    VARCHAR(300)             NULL,
  [SignedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReceivedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SignedAt]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_ShiftHandover PRIMARY KEY CLUSTERED ([HandoverID])
);
GO

-- ── PR_DashTileCache  (POP 대시 캐시)
CREATE TABLE dbo.PR_DashTileCache (
  [LineID]                    VARCHAR(20)          NOT NULL,
  [TileID]                    VARCHAR(30)          NOT NULL,
  [Value]                     NVARCHAR(200)            NULL,
  [UpdatedAt]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [TtlSec]                    INT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_DashTileCache PRIMARY KEY CLUSTERED ([LineID], [TileID])
);
GO

-- ── PR_DefectRateCache  (불량률 캐시)
CREATE TABLE dbo.PR_DefectRateCache (
  [WoID]                      INT                  NOT NULL,
  [TotalGood]                 INT                      NULL,
  [TotalDefect]               INT                      NULL,
  [RatePct]                   DECIMAL(6,3)             NULL,
  [UpdatedAt]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_DefectRateCache PRIMARY KEY CLUSTERED ([WoID])
);
GO

-- ── PR_FabricIssue  (IMG 원단 투입)
CREATE TABLE dbo.PR_FabricIssue (
  [FabricIssueID]             INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [FabricRollLotID]           INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ColorCode]                 VARCHAR(10)              NULL,
  [MountedAt]                 DATETIME2                NULL,
  [DismountedAt]              DATETIME2                NULL,
  [InitialRemainingM]         DECIMAL(8,3)             NULL,
  [FinalRemainingM]           DECIMAL(8,3)             NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SessionID]                 INT                      NULL,  -- FK -> PR_PopSession.SessionID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_FabricIssue PRIMARY KEY CLUSTERED ([FabricIssueID])
);
GO

-- ── PR_FabricIssueAttempt  (원단 시도 감사)
CREATE TABLE dbo.PR_FabricIssueAttempt (
  [AttemptID]                 BIGINT IDENTITY      NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ScannedRollLotID]          INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ScannedColor]              VARCHAR(10)              NULL,
  [ExpectedColor]             VARCHAR(10)              NULL,
  [Result]                    VARCHAR(10)              NULL,
  [AttemptedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AttemptedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_FabricIssueAttempt PRIMARY KEY CLUSTERED ([AttemptID])
);
GO

-- ── PR_FabricDeductionLog  (원단 차감 (7년))
CREATE TABLE dbo.PR_FabricDeductionLog (
  [DeductionID]               BIGINT IDENTITY      NOT NULL,
  [FabricRollLotID]           INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ResultID]                  INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [ConsumedM]                 DECIMAL(8,3)             NULL,
  [BeforeM]                   DECIMAL(8,3)             NULL,
  [AfterM]                    DECIMAL(8,3)             NULL,
  [DeductedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_FabricDeductionLog PRIMARY KEY CLUSTERED ([DeductionID])
);
GO

-- ── PR_BondSetup  (IMG 본드 설정)
CREATE TABLE dbo.PR_BondSetup (
  [BondSetupID]               INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [RecipeID]                  VARCHAR(20)              NULL,  -- FK -> MD_Recipe.RecipeID
  [PressureSp]                DECIMAL(6,2)             NULL,
  [TempSp]                    DECIMAL(5,1)             NULL,
  [HoldSecSp]                 INT                      NULL,
  [TensionSp]                 DECIMAL(6,2)             NULL,
  [LoadedAt]                  DATETIME2                NULL,
  [LoadedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_BondSetup PRIMARY KEY CLUSTERED ([BondSetupID])
);
GO

-- ── PR_BondCycleLog  (본드 사이클 PLC)
CREATE TABLE dbo.PR_BondCycleLog (
  [BondCycleID]               BIGINT IDENTITY      NOT NULL,
  [ResultID]                  INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [BondSetupID]               INT                      NULL,  -- FK -> PR_BondSetup.BondSetupID
  [PressureAvg]               DECIMAL(6,2)             NULL,
  [TempAvg]                   DECIMAL(5,1)             NULL,
  [HoldActualSec]             INT                      NULL,
  [TensionAvg]                DECIMAL(6,2)             NULL,
  [WithinSpec]                BIT                      NULL,
  [SampledAt]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_BondCycleLog PRIMARY KEY CLUSTERED ([BondCycleID])
);
GO

-- ── PR_BondSetupAudit  (본드 변경 감사 (7년))
CREATE TABLE dbo.PR_BondSetupAudit (
  [AuditID]                   BIGINT IDENTITY      NOT NULL,
  [BondSetupID]               INT                      NULL,  -- FK -> PR_BondSetup.BondSetupID
  [FieldName]                 VARCHAR(40)              NULL,
  [OldValue]                  NVARCHAR(100)            NULL,
  [NewValue]                  NVARCHAR(100)            NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [ChangedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChangedAt]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PR_BondSetupAudit PRIMARY KEY CLUSTERED ([AuditID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PNT                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── PNT_DailyPlan  (일일 계획 (PNT-01))
CREATE TABLE dbo.PNT_DailyPlan (
  [PlanID]                    INT IDENTITY         NOT NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [PlanDate]                  DATE                     NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor]                  VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [TargetQty]                 INT                      NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [OvenID]                    VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [StartTime]                 TIME                     NULL,
  [JigsRequired]              INT                      NULL,
  [LotsRequired]              INT                      NULL,
  [ReadyFlag]                 BIT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_DailyPlan PRIMARY KEY CLUSTERED ([PlanID])
);
GO

-- ── PNT_VirtualLot  (★ 가상 LOT (PNT-02))
CREATE TABLE dbo.PNT_VirtualLot (
  [VirtualLotID]              INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PlanID]                    INT                      NULL,  -- FK -> PNT_DailyPlan.PlanID
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor]                  VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [TargetQty]                 INT                      NULL,
  [LoadedQty]                 INT                      NULL,
  [ConfirmedQty]              INT                      NULL,
  [DefectQty]                 INT                      NULL,
  [Status]                    VARCHAR(20)              NULL,
  [EnhancedInspection]        BIT                      NULL,
  [IssuedAt]                  DATETIME2                NULL,
  [IssuedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [BindAt]                    DATETIME2                NULL,
  [BindReason]                VARCHAR(40)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_VirtualLot PRIMARY KEY CLUSTERED ([VirtualLotID])
);
GO

-- ── PNT_JigBindingLog  (지그 바인딩 이력)
CREATE TABLE dbo.PNT_JigBindingLog (
  [BindingLogID]              INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [BoundAt]                   DATETIME2                NULL,
  [UnboundAt]                 DATETIME2                NULL,
  [Reason]                    VARCHAR(40)              NULL,
  [ActorID]                   NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_JigBindingLog PRIMARY KEY CLUSTERED ([BindingLogID])
);
GO

-- ── PNT_SeqAllocator  (LotID 채번 락)
CREATE TABLE dbo.PNT_SeqAllocator (
  [PlanDate]                  DATE                 NOT NULL,
  [LineID]                    VARCHAR(20)          NOT NULL,
  [NextSeq]                   INT                      NULL,
  [UpdatedAt]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_SeqAllocator PRIMARY KEY CLUSTERED ([PlanDate], [LineID])
);
GO

-- ── PNT_JigLoad  (지그 로딩 (PNT-03))
CREATE TABLE dbo.PNT_JigLoad (
  [LoadID]                    INT IDENTITY         NOT NULL,
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LoadedQty]                 INT                      NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PdaScanAt]                 DATETIME2                NULL,
  [R1ReadAt]                  DATETIME2                NULL,
  [MatchStatus]               VARCHAR(20)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_JigLoad PRIMARY KEY CLUSTERED ([LoadID])
);
GO

-- ── PNT_LineEvent  (★ RFID 통과 (R1/R2/R3, 5년))
CREATE TABLE dbo.PNT_LineEvent (
  [EventID]                   BIGINT IDENTITY      NOT NULL,
  [TagID]                     VARCHAR(24)              NULL,  -- FK -> MD_RfidTag.TagID
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ReaderID]                  VARCHAR(20)              NULL,  -- FK -> MD_RfidReader.ReaderID
  [AntennaPort]               TINYINT                  NULL,
  [TagRole]                   VARCHAR(10)              NULL,
  [EventTS]                   DATETIME2                NULL DEFAULT SYSDATETIME(),
  [Rssi]                      SMALLINT                 NULL,
  [ReadCount]                 INT                      NULL,
  [TriggerType]               VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_LineEvent PRIMARY KEY CLUSTERED ([EventID])
);
GO

-- ── PNT_TagFailureLog  (태그 실패 로그)
CREATE TABLE dbo.PNT_TagFailureLog (
  [FailureID]                 INT IDENTITY         NOT NULL,
  [TagID]                     VARCHAR(24)              NULL,  -- FK -> MD_RfidTag.TagID
  [ReaderID]                  VARCHAR(20)              NULL,  -- FK -> MD_RfidReader.ReaderID
  [FailedAt]                  DATETIME2                NULL,
  [FailType]                  VARCHAR(20)              NULL,
  [FallbackAction]            VARCHAR(30)              NULL,
  [ResolvedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_TagFailureLog PRIMARY KEY CLUSTERED ([FailureID])
);
GO

-- ── PNT_OvenLog  (오븐 체류 (PNT-05))
CREATE TABLE dbo.PNT_OvenLog (
  [OvenLogID]                 INT IDENTITY         NOT NULL,
  [OvenID]                    VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [EntryTS]                   DATETIME2                NULL,
  [ExitTS]                    DATETIME2                NULL,
  [DwellSec]                  INT                      NULL,
  [TempCurve]                 NVARCHAR(MAX)            NULL,
  [MinTemp]                   DECIMAL(5,1)             NULL,
  [MaxTemp]                   DECIMAL(5,1)             NULL,
  [AvgTemp]                   DECIMAL(5,1)             NULL,
  [WithinSpec]                BIT                      NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_OvenLog PRIMARY KEY CLUSTERED ([OvenLogID])
);
GO

-- ── PNT_OvenTempSample  (오븐 5초 샘플 (5년))
CREATE TABLE dbo.PNT_OvenTempSample (
  [SampleID]                  BIGINT IDENTITY      NOT NULL,
  [OvenID]                    VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [ZoneID]                    TINYINT                  NULL,
  [TempC]                     DECIMAL(5,1)             NULL,
  [SampledAt]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_OvenTempSample PRIMARY KEY CLUSTERED ([SampleID])
);
GO

-- ── PNT_OvenDeviationLog  (오븐 온도 이탈)
CREATE TABLE dbo.PNT_OvenDeviationLog (
  [DeviationID]               INT IDENTITY         NOT NULL,
  [OvenID]                    VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [StartTS]                   DATETIME2                NULL,
  [EndTS]                     DATETIME2                NULL,
  [MaxDelta]                  DECIMAL(5,1)             NULL,
  [AffectedLots]              NVARCHAR(MAX)            NULL,
  [MntWoID]                   INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [AndonID]                   INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_OvenDeviationLog PRIMARY KEY CLUSTERED ([DeviationID])
);
GO

-- ── PNT_OvenSpikeLog  (오븐 단일 스파이크)
CREATE TABLE dbo.PNT_OvenSpikeLog (
  [SpikeID]                   INT IDENTITY         NOT NULL,
  [OvenID]                    VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [DetectedAt]                DATETIME2                NULL,
  [TempC]                     DECIMAL(5,1)             NULL,
  [Delta]                     DECIMAL(5,1)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_OvenSpikeLog PRIMARY KEY CLUSTERED ([SpikeID])
);
GO

-- ── PNT_JigUnload  (지그 언로딩 (PNT-06))
CREATE TABLE dbo.PNT_JigUnload (
  [UnloadID]                  INT IDENTITY         NOT NULL,
  [JigID]                     VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ConfirmedQty]              INT                      NULL,
  [ExpectedQty]               INT                      NULL,
  [ShortReason]               VARCHAR(30)              NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [R3ReadAt]                  DATETIME2                NULL,
  [ConfirmedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_JigUnload PRIMARY KEY CLUSTERED ([UnloadID])
);
GO

-- ── PNT_PartLossLog  (부품 손실 로그)
CREATE TABLE dbo.PNT_PartLossLog (
  [LossID]                    INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LossQty]                   INT                      NULL,
  [ReasonCode]                VARCHAR(30)              NULL,
  [ReasonNote]                NVARCHAR(300)            NULL,
  [LoggedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [LoggedAt]                  DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_PartLossLog PRIMARY KEY CLUSTERED ([LossID])
);
GO

-- ── PNT_StationStatsCache  (라인보드 캐시)
CREATE TABLE dbo.PNT_StationStatsCache (
  [StationCode]               VARCHAR(20)          NOT NULL,
  [ActiveCount]               INT                      NULL,
  [AvgDwellSec]               INT                      NULL,
  [BottleneckFlag]            BIT                      NULL,
  [UpdatedAt]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_StationStatsCache PRIMARY KEY CLUSTERED ([StationID])
);
GO

-- ── PNT_LotLabel  (LOT 라벨 (PNT-07))
CREATE TABLE dbo.PNT_LotLabel (
  [LabelID]                   INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [SeqNo]                     INT                      NULL,
  [TotalQty]                  INT                      NULL,
  [PrintedAt]                 DATETIME2                NULL,
  [PrintedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AppliedAt]                 DATETIME2                NULL,
  [AppliedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_LotLabel PRIMARY KEY CLUSTERED ([LabelID])
);
GO

-- ── PNT_LabelPrintJob  (라벨 프린트 잡)
CREATE TABLE dbo.PNT_LabelPrintJob (
  [JobID]                     INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PrinterID]                 VARCHAR(20)              NULL,
  [Zpl]                       NVARCHAR(MAX)            NULL,
  [SubmittedAt]               DATETIME2                NULL,
  [CompletedAt]               DATETIME2                NULL,
  [Status]                    VARCHAR(20)              NULL,
  [FailReason]                VARCHAR(200)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_LabelPrintJob PRIMARY KEY CLUSTERED ([JobID])
);
GO

-- ── PNT_LabelScanLog  (라벨 스캔 이력)
CREATE TABLE dbo.PNT_LabelScanLog (
  [ScanID]                    BIGINT IDENTITY      NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LabelID]                   INT                      NULL,  -- FK -> PNT_LotLabel.LabelID
  [ScannedSeq]                INT                      NULL,
  [Position]                  VARCHAR(10)              NULL,
  [ScannedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ScannedAt]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_LabelScanLog PRIMARY KEY CLUSTERED ([ScanID])
);
GO

-- ── PNT_ShiftReport  (교대 보고서 헤더)
CREATE TABLE dbo.PNT_ShiftReport (
  [ReportID]                  INT IDENTITY         NOT NULL,
  [ShiftDate]                 DATE                     NULL,
  [ShiftType]                 VARCHAR(10)              NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [LoadedQty]                 INT                      NULL,
  [ConfirmedQty]              INT                      NULL,
  [DefectQty]                 INT                      NULL,
  [OvenDeviations]            INT                      NULL,
  [Fallbacks]                 INT                      NULL,
  [SpareTagsUsed]             INT                      NULL,
  [JigSwaps]                  INT                      NULL,
  [YieldPct]                  DECIMAL(5,2)             NULL,
  [CompiledAt]                DATETIME2                NULL,
  [SignedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SignedAt]                  DATETIME2                NULL,
  [PdfUrl]                    VARCHAR(300)             NULL,
  [Version]                   TINYINT                  NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_ShiftReport PRIMARY KEY CLUSTERED ([ReportID])
);
GO

-- ── PNT_ShiftReportLineItem  (교대 WO 명세)
CREATE TABLE dbo.PNT_ShiftReportLineItem (
  [LineItemID]                INT IDENTITY         NOT NULL,
  [ReportID]                  INT                      NULL,  -- FK -> PNT_ShiftReport.ReportID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor]                  VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [PlanQty]                   INT                      NULL,
  [LoadedQty]                 INT                      NULL,
  [ConfirmedQty]              INT                      NULL,
  [DefectQty]                 INT                      NULL,
  [YieldPct]                  DECIMAL(5,2)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_ShiftReportLineItem PRIMARY KEY CLUSTERED ([LineItemID])
);
GO

-- ── PNT_ShiftReportAudit  (교대 수정 감사 (7년))
CREATE TABLE dbo.PNT_ShiftReportAudit (
  [AuditID]                   INT IDENTITY         NOT NULL,
  [ReportID]                  INT                      NULL,  -- FK -> PNT_ShiftReport.ReportID
  [ChangedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChangedAt]                 DATETIME2                NULL,
  [FieldName]                 VARCHAR(40)              NULL,
  [OldValue]                  NVARCHAR(200)            NULL,
  [NewValue]                  NVARCHAR(200)            NULL,
  [Reason]                    NVARCHAR(300)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_ShiftReportAudit PRIMARY KEY CLUSTERED ([AuditID])
);
GO

-- ── PNT_DailyReport  (일일 합산 보고서)
CREATE TABLE dbo.PNT_DailyReport (
  [DailyID]                   INT IDENTITY         NOT NULL,
  [ReportDate]                DATE                     NULL,
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [TotalConfirmed]            INT                      NULL,
  [TotalDefect]               INT                      NULL,
  [DailyYieldPct]             DECIMAL(5,2)             NULL,
  [TwoShiftRollupJson]        NVARCHAR(MAX)            NULL,
  [GeneratedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_DailyReport PRIMARY KEY CLUSTERED ([DailyID])
);
GO

-- ── PNT_QcQueue  (QC 인계 대기열)
CREATE TABLE dbo.PNT_QcQueue (
  [QueueID]                   INT IDENTITY         NOT NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [EnqueuedAt]                DATETIME2                NULL,
  [EnhancedFlag]              BIT                      NULL,
  [SlaDueAt]                  DATETIME2                NULL,
  [Status]                    VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_PNT_QcQueue PRIMARY KEY CLUSTERED ([QueueID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: QC                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── QC_Inspection  (★ 검사 (IQC/IPQC/FQC))
CREATE TABLE dbo.QC_Inspection (
  [InspectionID]              INT IDENTITY         NOT NULL,
  [InspectionNo]              VARCHAR(24)              NULL,
  [InspectionType]            VARCHAR(15)              NULL,
  [PoID]                      INT                      NULL,  -- FK -> WH_PurchaseOrder.PoID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [StdID]                     INT                      NULL,  -- FK -> QC_InspectionStd.StdID
  [CustomerCode]              VARCHAR(20)              NULL,
  [Mode]                      VARCHAR(15)              NULL,
  [EnhanceReason]             VARCHAR(60)              NULL,
  [SampleSize]                INT                      NULL,
  [BatchQty]                  DECIMAL(12,3)            NULL,
  [CumulativeGood]            INT                      NULL,
  [DefectQtyTotal]            INT                      NULL,
  [Verdict]                   VARCHAR(15)              NULL,
  [CriticalFlag]              BIT                      NULL,
  [CorrectiveAction]          NVARCHAR(500)            NULL,
  [ResultJSON]                NVARCHAR(MAX)            NULL,
  [NcrID]                     INT                      NULL,  -- FK -> QC_NCR.NcrID
  [HoldID]                    INT                      NULL,  -- FK -> QC_Hold.HoldID
  [InspectorID]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApproverID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ResumeBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [InsStartTS]                DATETIME2                NULL DEFAULT SYSDATETIME(),
  [InsEndTS]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_Inspection PRIMARY KEY CLUSTERED ([InspectionID])
);
GO

-- ── QC_InspectionItem  (검사 항목별 측정값)
CREATE TABLE dbo.QC_InspectionItem (
  [InspectionItemID]          INT IDENTITY         NOT NULL,
  [InspectionID]              INT                      NULL,  -- FK -> QC_Inspection.InspectionID
  [ItemSeq]                   INT                      NULL,
  [ItemName]                  NVARCHAR(100)        NOT NULL,
  [Standard]                  NVARCHAR(100)            NULL,
  [Measured]                  NVARCHAR(100)            NULL,
  [Result]                    VARCHAR(10)              NULL,
  [PhotoURL]                  VARCHAR(255)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_InspectionItem PRIMARY KEY CLUSTERED ([InspectionItemID])
);
GO

-- ── QC_InspectionStd  (검사 기준서 (버전))
CREATE TABLE dbo.QC_InspectionStd (
  [StdID]                     INT IDENTITY         NOT NULL,
  [StdCode]                   VARCHAR(30)              NULL,
  [VerNo]                     VARCHAR(8)               NULL,
  [StdName]                   NVARCHAR(120)            NULL,
  [InsType]                   VARCHAR(15)              NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CustomerCode]              VARCHAR(20)              NULL,
  [Mode]                      VARCHAR(15)              NULL,
  [AQLLevel]                  DECIMAL(3,2)             NULL,
  [SampleInterval]            INT                      NULL,
  [InspItemsJSON]             NVARCHAR(MAX)            NULL,
  [KPITargetsJSON]            NVARCHAR(MAX)            NULL,
  [Status]                    VARCHAR(15)              NULL,
  [EffectiveDate]             DATE                     NULL,
  [DraftedBy]                 NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CapaLinkID]                INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_InspectionStd PRIMARY KEY CLUSTERED ([StdID])
);
GO

-- ── QC_NCR  (★ 부적합 보고서)
CREATE TABLE dbo.QC_NCR (
  [NcrID]                     INT IDENTITY         NOT NULL,
  [NcrNumber]                 VARCHAR(24)              NULL,
  [SourceType]                VARCHAR(20)              NULL,
  [SourceID]                  VARCHAR(24)              NULL,
  [InspectionID]              INT                      NULL,  -- FK -> QC_Inspection.InspectionID
  [Severity]                  VARCHAR(10)              NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [AffectedQty]               DECIMAL(12,3)            NULL,
  [CustomerCode]              VARCHAR(20)              NULL,
  [DefectsJSON]               NVARCHAR(MAX)            NULL,
  [Cause4M]                   VARCHAR(15)              NULL,
  [Disposition]               VARCHAR(15)              NULL,
  [HoldID]                    INT                      NULL,  -- FK -> QC_Hold.HoldID
  [CapaID]                    INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [Status]                    VARCHAR(15)              NULL,
  [ReportedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReportedAt]                DATETIME2                NULL,
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClosedAt]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_NCR PRIMARY KEY CLUSTERED ([NcrID])
);
GO

-- ── QC_NCR_Action  (NCR 처리 이력)
CREATE TABLE dbo.QC_NCR_Action (
  [ActionID]                  INT IDENTITY         NOT NULL,
  [NcrID]                     INT                      NULL,  -- FK -> QC_NCR.NcrID
  [ActionType]                VARCHAR(20)              NULL,
  [ActionRefID]               VARCHAR(24)              NULL,
  [ActionNote]                NVARCHAR(500)            NULL,
  [ActionTS]                  DATETIME2                NULL,
  [ActionBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_NCR_Action PRIMARY KEY CLUSTERED ([ActionID])
);
GO

-- ── QC_Hold  (보류/격리)
CREATE TABLE dbo.QC_Hold (
  [HoldID]                    INT IDENTITY         NOT NULL,
  [HoldNumber]                VARCHAR(24)              NULL,
  [SourceNcrID]               INT                      NULL,  -- FK -> QC_NCR.NcrID
  [Severity]                  VARCHAR(10)              NULL,
  [AffectedType]              VARCHAR(15)              NULL,
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [FgStockID]                 INT                      NULL,  -- FK -> FG_Stock.StockID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [HeldQty]                   DECIMAL(12,3)            NULL,
  [PhysicalLocation]          VARCHAR(20)              NULL,
  [LabelPrintedTS]            DATETIME2                NULL,
  [Status]                    VARCHAR(15)              NULL,
  [HeldBy]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [HeldAt]                    DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_Hold PRIMARY KEY CLUSTERED ([HoldID])
);
GO

-- ── QC_HoldRelease  (보류 해제 이력)
CREATE TABLE dbo.QC_HoldRelease (
  [ReleaseID]                 INT IDENTITY         NOT NULL,
  [HoldID]                    INT                      NULL,  -- FK -> QC_Hold.HoldID
  [EventType]                 VARCHAR(15)              NULL,
  [ReleaseAction]             VARCHAR(15)              NULL,
  [ReleaseReason]             NVARCHAR(500)            NULL,
  [PlantMgrApprovalID]        NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ActorPINHash]              VARCHAR(255)             NULL,
  [ReleasedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReleasedAt]                DATETIME2                NULL,
  [Note]                      NVARCHAR(500)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_HoldRelease PRIMARY KEY CLUSTERED ([ReleaseID])
);
GO

-- ── QC_CAPA  (★ 시정·예방 조치)
CREATE TABLE dbo.QC_CAPA (
  [CapaID]                    INT IDENTITY         NOT NULL,
  [CapaNumber]                VARCHAR(24)              NULL,
  [Type]                      VARCHAR(15)              NULL,
  [TriggerType]               VARCHAR(20)              NULL,
  [LinkedNcrIDs]              NVARCHAR(MAX)            NULL,
  [Phase]                     VARCHAR(10)              NULL,
  [Status]                    VARCHAR(25)              NULL,
  [FiveWhyJSON]               NVARCHAR(MAX)            NULL,
  [RootCause]                 NVARCHAR(1000)           NULL,
  [Cause4M]                   VARCHAR(15)              NULL,
  [ActionsJSON]               NVARCHAR(MAX)            NULL,
  [EffectivenessJSON]         NVARCHAR(MAX)            NULL,
  [OwnerID]                   NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [QcManagerID]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CustomerImpact]            VARCHAR(10)              NULL,
  [CustomerNotified]          BIT                      NULL,
  [OpenedAt]                  DATETIME2                NULL,
  [DueDate]                   DATE                     NULL,
  [ClosedAt]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_CAPA PRIMARY KEY CLUSTERED ([CapaID])
);
GO

-- ── QC_CAPA_Action  (CAPA 단계 이력)
CREATE TABLE dbo.QC_CAPA_Action (
  [CapaActionID]              INT IDENTITY         NOT NULL,
  [CapaID]                    INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [ActionType]                VARCHAR(20)              NULL,
  [CheckDay]                  INT                      NULL,
  [Description]               NVARCHAR(500)            NULL,
  [Metric]                    NVARCHAR(60)             NULL,
  [TargetValue]               NVARCHAR(60)             NULL,
  [ActualValue]               NVARCHAR(60)             NULL,
  [Verdict]                   VARCHAR(10)              NULL,
  [OwnerID]                   NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [DueDate]                   DATE                     NULL,
  [CompletedAt]               DATETIME2                NULL,
  [EvidenceURL]               VARCHAR(255)             NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_CAPA_Action PRIMARY KEY CLUSTERED ([CapaActionID])
);
GO

-- ── QC_Disposition  (처분 결정)
CREATE TABLE dbo.QC_Disposition (
  [DispositionID]             INT IDENTITY         NOT NULL,
  [NcrID]                     INT                      NULL,  -- FK -> QC_NCR.NcrID
  [HoldID]                    INT                      NULL,  -- FK -> QC_Hold.HoldID
  [DispositionAction]         VARCHAR(15)              NULL,
  [DispositionQty]            DECIMAL(12,3)            NULL,
  [Reason]                    NVARCHAR(500)            NULL,
  [CustomerApprovalURL]       VARCHAR(255)             NULL,
  [DownstreamRefType]         VARCHAR(15)              NULL,
  [DownstreamRefID]           VARCHAR(24)              NULL,
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_QC_Disposition PRIMARY KEY CLUSTERED ([DispositionID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: FG                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── FG_Stock  (★ 완제품 재고)
CREATE TABLE dbo.FG_Stock (
  [StockID]                   INT IDENTITY         NOT NULL,
  [StockNumber]               VARCHAR(24)              NULL,
  [FgTriggerID]               INT                      NULL,
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [CustomerCode]              VARCHAR(20)              NULL,
  [Qty]                       DECIMAL(12,3)            NULL,
  [Location]                  VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [Status]                    VARCHAR(15)              NULL,
  [HoldFlag]                  BIT                      NULL,
  [HoldID]                    INT                      NULL,  -- FK -> QC_Hold.HoldID
  [ReservationID]             INT                      NULL,
  [StockTS]                   DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_Stock PRIMARY KEY CLUSTERED ([StockID])
);
GO

-- ── FG_PutAway  (완제품 적치 (FG-01))
CREATE TABLE dbo.FG_PutAway (
  [PutAwayID]                 INT IDENTITY         NOT NULL,
  [StockID]                   INT                      NULL,  -- FK -> FG_Stock.StockID
  [WoID]                      INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [Qty]                       DECIMAL(12,3)            NULL,
  [SuggestedLoc]              VARCHAR(20)              NULL,
  [ActualLoc]                 VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LocOverrideReason]         VARCHAR(60)              NULL,
  [PalletCount]               INT                      NULL,
  [PalletQty]                 INT                      NULL,
  [LabelPrintedTS]            DATETIME2                NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status]                    VARCHAR(15)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_PutAway PRIMARY KEY CLUSTERED ([PutAwayID])
);
GO

-- ── FG_ShipmentOrder  (★ 출하 지시 헤더)
CREATE TABLE dbo.FG_ShipmentOrder (
  [ShipmentOrderID]           INT IDENTITY         NOT NULL,
  [ShipOrderNumber]           VARCHAR(24)              NULL,
  [CustomerCode]              VARCHAR(20)              NULL,
  [CustomerPO]                VARCHAR(40)              NULL,
  [Source]                    VARCHAR(10)              NULL,
  [ShipDate]                  DATE                     NULL,
  [CarrierCode]               VARCHAR(20)              NULL,
  [DestPlant]                 VARCHAR(30)              NULL,
  [DestDock]                  VARCHAR(30)              NULL,
  [ReceiverName]              VARCHAR(50)              NULL,
  [ReceiverPhone]             VARCHAR(30)              NULL,
  [Status]                    VARCHAR(15)              NULL,
  [PickslipID]                VARCHAR(24)              NULL,
  [OTDFlag]                   VARCHAR(10)              NULL,
  [ConfirmedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ConfirmedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_ShipmentOrder PRIMARY KEY CLUSTERED ([ShipmentOrderID])
);
GO

-- ── FG_ShipmentOrderLine  (출하 라인 (SO×LOT))
CREATE TABLE dbo.FG_ShipmentOrderLine (
  [ShipmentOrderLineID]       INT IDENTITY         NOT NULL,
  [ShipmentOrderID]           INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [LineSeq]                   INT                      NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderedQty]                DECIMAL(12,3)            NULL,
  [AllocatedQty]              DECIMAL(12,3)            NULL,
  [StockID]                   INT                      NULL,  -- FK -> FG_Stock.StockID
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [Location]                  VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [ReservationStatus]         VARCHAR(15)              NULL,
  [ReservedAt]                DATETIME2                NULL,
  [ReleasedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_ShipmentOrderLine PRIMARY KEY CLUSTERED ([ShipmentOrderLineID])
);
GO

-- ── FG_PickingFifo  (FIFO 피킹 세션)
CREATE TABLE dbo.FG_PickingFifo (
  [PickID]                    INT IDENTITY         NOT NULL,
  [PickNumber]                VARCHAR(24)              NULL,
  [PickslipID]                VARCHAR(24)              NULL,
  [ShipmentOrderID]           INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [PickerID]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [StartTS]                   DATETIME2                NULL,
  [EndTS]                     DATETIME2                NULL,
  [PicksJSON]                 NVARCHAR(MAX)            NULL,
  [FifoViolations]            INT                      NULL,
  [OverrideCount]             INT                      NULL,
  [OverrideApprovedBy]        NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PartialPicksJSON]          NVARCHAR(MAX)            NULL,
  [PickedQty]                 DECIMAL(12,3)            NULL,
  [OrderedQty]                DECIMAL(12,3)            NULL,
  [Status]                    VARCHAR(15)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_PickingFifo PRIMARY KEY CLUSTERED ([PickID])
);
GO

-- ── FG_LoadingConfirm  (상차 (Chain-of-Custody))
CREATE TABLE dbo.FG_LoadingConfirm (
  [LoadingID]                 INT IDENTITY         NOT NULL,
  [LoadingNumber]             VARCHAR(24)              NULL,
  [ShipmentOrderID]           INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [PickID]                    INT                      NULL,  -- FK -> FG_PickingFifo.PickID
  [LicensePlate]              VARCHAR(20)              NULL,
  [CarrierCode]               VARCHAR(20)              NULL,
  [DriverID]                  VARCHAR(30)              NULL,
  [DriverName]                VARCHAR(50)              NULL,
  [DriverPhone]               VARCHAR(30)              NULL,
  [DockNo]                    VARCHAR(10)              NULL,
  [ArrivalTS]                 DATETIME2                NULL,
  [DepartureTS]               DATETIME2                NULL,
  [PalletsLoadedJSON]         NVARCHAR(MAX)            NULL,
  [SealNo]                    VARCHAR(20)              NULL,
  [DriverSigURL]              VARCHAR(255)             NULL,
  [DriverPhotoURL]            VARCHAR(255)             NULL,
  [GPSCoord]                  VARCHAR(30)              NULL,
  [OTDStatus]                 VARCHAR(10)              NULL,
  [OperatorID]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ConfirmedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_LoadingConfirm PRIMARY KEY CLUSTERED ([LoadingID])
);
GO

-- ── FG_DeliveryNote  (★ 거래명세서 / BOL)
CREATE TABLE dbo.FG_DeliveryNote (
  [DeliveryNoteID]            INT IDENTITY         NOT NULL,
  [DnNumber]                  VARCHAR(30)              NULL,
  [ShipmentOrderID]           INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [LoadingID]                 INT                      NULL,  -- FK -> FG_LoadingConfirm.LoadingID
  [CustomerCode]              VARCHAR(20)              NULL,
  [FormatTemplate]            VARCHAR(40)              NULL,
  [Revision]                  INT                      NULL,
  [RevisionReason]            NVARCHAR(200)            NULL,
  [IssuedAt]                  DATETIME2                NULL,
  [IssuedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PdfUrl]                    VARCHAR(255)             NULL,
  [EdiMsgID]                  VARCHAR(40)              NULL,
  [EdiStatus]                 VARCHAR(15)              NULL,
  [CustomerAckTS]             DATETIME2                NULL,
  [LinesJSON]                 NVARCHAR(MAX)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_DeliveryNote PRIMARY KEY CLUSTERED ([DeliveryNoteID])
);
GO

-- ── FG_DayEndClose  (일 마감 스냅샷)
CREATE TABLE dbo.FG_DayEndClose (
  [DayEndCloseID]             INT IDENTITY         NOT NULL,
  [CloseNumber]               VARCHAR(24)              NULL,
  [CloseDate]                 DATE                     NULL,
  [ClosedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClosedAt]                  DATETIME2                NULL,
  [CloseMode]                 VARCHAR(20)              NULL,
  [ChecklistJSON]             NVARCHAR(MAX)            NULL,
  [KpiJSON]                   NVARCHAR(MAX)            NULL,
  [PendingItemsJSON]          NVARCHAR(MAX)            NULL,
  [SnapshotURL]               VARCHAR(255)             NULL,
  [ErpFeedTS]                 DATETIME2                NULL,
  [ErpFeedStatus]             VARCHAR(15)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_DayEndClose PRIMARY KEY CLUSTERED ([DayEndCloseID])
);
GO

-- ── FG_CustomerReturn  (고객 반품 (RMA))
CREATE TABLE dbo.FG_CustomerReturn (
  [ReturnID]                  INT IDENTITY         NOT NULL,
  [ReturnNumber]              VARCHAR(24)              NULL,
  [RMANo]                     VARCHAR(40)              NULL,
  [CustomerCode]              VARCHAR(20)              NULL,
  [CustomerClaimID]           VARCHAR(24)              NULL,
  [OriginalShipmentOrderID]   INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [OriginalDeliveryNoteID]    INT                      NULL,  -- FK -> FG_DeliveryNote.DeliveryNoteID
  [ReturnReason]              VARCHAR(60)              NULL,
  [ItemsJSON]                 NVARCHAR(MAX)            NULL,
  [Status]                    VARCHAR(15)              NULL,
  [ReceivedAt]                DATETIME2                NULL,
  [ReceivedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [NcrID]                     INT                      NULL,  -- FK -> QC_NCR.NcrID
  [CapaTriggered]             BIT                      NULL,
  [ClosedAt]                  DATETIME2                NULL,
  [ClosedBy]                  NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_CustomerReturn PRIMARY KEY CLUSTERED ([ReturnID])
);
GO

-- ── FG_ReturnDisposition  (반품 처분)
CREATE TABLE dbo.FG_ReturnDisposition (
  [ReturnDispositionID]       INT IDENTITY         NOT NULL,
  [ReturnID]                  INT                      NULL,  -- FK -> FG_CustomerReturn.ReturnID
  [PalletSeq]                 INT                      NULL,
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LotID]                     INT                      NULL,  -- FK -> tbl_Lot.LotID
  [Qty]                       DECIMAL(12,3)            NULL,
  [Action]                    VARCHAR(15)              NULL,
  [Reason]                    NVARCHAR(500)            NULL,
  [DownstreamRefID]           VARCHAR(24)              NULL,
  [ApprovedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_FG_ReturnDisposition PRIMARY KEY CLUSTERED ([ReturnDispositionID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: MNT                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── MNT_EquipmentStatus  (설비 실시간 상태)
CREATE TABLE dbo.MNT_EquipmentStatus (
  [EquipStatusID]             INT IDENTITY         NOT NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [Status]                    VARCHAR(10)              NULL,
  [TodayOEE]                  DECIMAL(5,2)             NULL,
  [RuntimeHours]              DECIMAL(10,1)            NULL,
  [CycleCount]                BIGINT                   NULL,
  [NextPMDate]                DATE                     NULL,
  [MountedMoldID]             VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [LastFailureID]             INT                      NULL,  -- FK -> MNT_FailureRegister.FailureID
  [OpenWoID]                  INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [PLCConnTS]                 DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_EquipmentStatus PRIMARY KEY CLUSTERED ([EquipStatusID])
);
GO

-- ── MNT_FailureRegister  (★ 고장 등록)
CREATE TABLE dbo.MNT_FailureRegister (
  [FailureID]                 INT IDENTITY         NOT NULL,
  [FailureNumber]             VARCHAR(24)              NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [FailureType]               VARCHAR(15)              NULL,
  [Symptom]                   NVARCHAR(500)            NULL,
  [Urgency]                   VARCHAR(10)              NULL,
  [PhotoURLs]                 NVARCHAR(MAX)            NULL,
  [Source]                    VARCHAR(15)              NULL,
  [AndonRefID]                VARCHAR(24)              NULL,
  [WorkOrderID]               INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [DowntimeID]                INT                      NULL,  -- FK -> PP_LineDowntimeLog.DowntimeID
  [Status]                    VARCHAR(15)              NULL,
  [ReportedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReportedAt]                DATETIME2                NULL,
  [ResolvedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_FailureRegister PRIMARY KEY CLUSTERED ([FailureID])
);
GO

-- ── MNT_FailureAction  (고장 조치 이력)
CREATE TABLE dbo.MNT_FailureAction (
  [FailureActionID]           INT IDENTITY         NOT NULL,
  [FailureID]                 INT                      NULL,  -- FK -> MNT_FailureRegister.FailureID
  [ActionType]                VARCHAR(20)              NULL,
  [Description]               NVARCHAR(500)            NULL,
  [EvidenceURL]               VARCHAR(255)             NULL,
  [TechnicianID]              NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ActionAt]                  DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_FailureAction PRIMARY KEY CLUSTERED ([FailureActionID])
);
GO

-- ── MNT_OEELog  (OEE 측정 (설비×시각))
CREATE TABLE dbo.MNT_OEELog (
  [OEELogID]                  INT IDENTITY         NOT NULL,
  [OEERecordNumber]           VARCHAR(40)              NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID]                    VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [AggLevel]                  VARCHAR(10)              NULL,
  [AggDate]                   DATE                     NULL,
  [ShiftCode]                 VARCHAR(10)              NULL,
  [PlannedTimeMin]            INT                      NULL,
  [DowntimeMin]               INT                      NULL,
  [Availability]              DECIMAL(5,2)             NULL,
  [Performance]               DECIMAL(5,2)             NULL,
  [Quality]                   DECIMAL(5,2)             NULL,
  [OEE]                       DECIMAL(5,2)             NULL,
  [GoodQty]                   DECIMAL(12,3)            NULL,
  [TotalQty]                  DECIMAL(12,3)            NULL,
  [LossBreakdownJSON]         NVARCHAR(MAX)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_OEELog PRIMARY KEY CLUSTERED ([OEELogID])
);
GO

-- ── MNT_PMSchedule  (PM 일정)
CREATE TABLE dbo.MNT_PMSchedule (
  [PMScheduleID]              INT IDENTITY         NOT NULL,
  [PMPlanNumber]              VARCHAR(30)              NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [PMType]                    VARCHAR(60)              NULL,
  [CycleBasis]                VARCHAR(10)              NULL,
  [CycleValue]                INT                      NULL,
  [LastPMDate]                DATE                     NULL,
  [NextDueDate]               DATE                     NULL,
  [ChecklistID]               VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [AssignedTechID]            NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status]                    VARCHAR(10)              NULL,
  [ActiveWoID]                INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_PMSchedule PRIMARY KEY CLUSTERED ([PMScheduleID])
);
GO

-- ── MNT_PMExecution  (PM 실행 이력)
CREATE TABLE dbo.MNT_PMExecution (
  [PMExecutionID]             INT IDENTITY         NOT NULL,
  [PMScheduleID]              INT                      NULL,  -- FK -> MNT_PMSchedule.PMScheduleID
  [WorkOrderID]               INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [CompletedAt]               DATETIME2                NULL,
  [TechnicianID]              NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Result]                    VARCHAR(15)              NULL,
  [ResultNote]                NVARCHAR(500)            NULL,
  [ChecklistResultsJSON]      NVARCHAR(MAX)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_PMExecution PRIMARY KEY CLUSTERED ([PMExecutionID])
);
GO

-- ── MNT_WorkOrder  (★ 정비 WO)
CREATE TABLE dbo.MNT_WorkOrder (
  [WorkOrderID]               INT IDENTITY         NOT NULL,
  [WoNumber]                  VARCHAR(28)              NULL,
  [WoType]                    VARCHAR(15)              NULL,
  [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [Priority]                  VARCHAR(10)              NULL,
  [SourceType]                VARCHAR(15)              NULL,
  [SourceRefID]               VARCHAR(24)              NULL,
  [AssignedTechID]            NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChecklistID]               VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [ChecklistResultsJSON]      NVARCHAR(MAX)            NULL,
  [PartsUsedJSON]             NVARCHAR(MAX)            NULL,
  [LaborMinutes]              INT                      NULL,
  [ActionDesc]                NVARCHAR(1000)           NULL,
  [Status]                    VARCHAR(15)              NULL,
  [IssuedAt]                  DATETIME2                NULL,
  [StartedAt]                 DATETIME2                NULL,
  [CompletedAt]               DATETIME2                NULL,
  [ClosedAt]                  DATETIME2                NULL,
  [DowntimeID]                INT                      NULL,  -- FK -> PP_LineDowntimeLog.DowntimeID
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_WorkOrder PRIMARY KEY CLUSTERED ([WorkOrderID])
);
GO

-- ── MNT_WorkOrderTask  (정비 WO 작업 항목)
CREATE TABLE dbo.MNT_WorkOrderTask (
  [WorkOrderTaskID]           INT IDENTITY         NOT NULL,
  [WorkOrderID]               INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [TaskSeq]                   INT                      NULL,
  [TaskName]                  NVARCHAR(120)            NULL,
  [TaskType]                  VARCHAR(20)              NULL,
  [Result]                    VARCHAR(10)              NULL,
  [Note]                      NVARCHAR(500)            NULL,
  [EvidenceURL]               VARCHAR(255)             NULL,
  [CompletedBy]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CompletedAt]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_WorkOrderTask PRIMARY KEY CLUSTERED ([WorkOrderTaskID])
);
GO

-- ── MNT_SparePartsTxn  (정비 자재 입출고)
CREATE TABLE dbo.MNT_SparePartsTxn (
  [SparePartsTxnID]           INT IDENTITY         NOT NULL,
  [PartNo]                    VARCHAR(20)              NULL,  -- FK -> MD_SparePart.PartNo
  [PartName]                  NVARCHAR(60)             NULL,
  [Category]                  VARCHAR(15)              NULL,
  [MoveType]                  VARCHAR(10)              NULL,
  [Qty]                       INT                      NULL,
  [BalanceAfter]              INT                      NULL,
  [UnitPrice]                 DECIMAL(12,2)            NULL,
  [StorageLoc]                VARCHAR(20)              NULL,
  [RefType]                   VARCHAR(15)              NULL,
  [RefID]                     VARCHAR(24)              NULL,
  [SupplierCode]              VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [Note]                      NVARCHAR(500)            NULL,
  [TxnAt]                     DATETIME2                NULL,
  [ActorID]                   NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_SparePartsTxn PRIMARY KEY CLUSTERED ([SparePartsTxnID])
);
GO

-- ── MNT_MoldShotCount  (금형 쇼트 운영 카운터)
CREATE TABLE dbo.MNT_MoldShotCount (
  [MoldShotCountID]           INT IDENTITY         NOT NULL,
  [MoldID]                    VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [ItemNo]                    VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CurrentShots]              INT                      NULL,
  [LifetimeShots]             INT                      NULL,
  [Status]                    VARCHAR(15)              NULL,
  [MountedEquipID]            VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [StorageLoc]                VARCHAR(20)              NULL,
  [ThresholdLevel]            VARCHAR(15)              NULL,
  [LastRefurbishTS]           DATETIME2                NULL,
  [RefurbishCount]            INT                      NULL,
  [HistoryJSON]               NVARCHAR(MAX)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_MNT_MoldShotCount PRIMARY KEY CLUSTERED ([MoldShotCountID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: SYS                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── SYS_UserProfile  (사용자 추가 속성)
CREATE TABLE dbo.SYS_UserProfile (
  [UserProfileID]             INT IDENTITY         NOT NULL,
  [UserID]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [EmployeeNo]                VARCHAR(20)              NULL,
  [EmployeeName]              NVARCHAR(50)             NULL,
  [Department]                VARCHAR(30)              NULL,
  [PlantCode]                 VARCHAR(20)              NULL,
  [DefaultShift]              VARCHAR(10)              NULL,
  [AssignedLines]             NVARCHAR(MAX)            NULL,
  [AccountStatus]             VARCHAR(10)              NULL,
  [FailedLoginCount]          INT                      NULL,
  [PinHash]                   NVARCHAR(200)            NULL,  -- POP 4자리 PIN (PBKDF2). Web 비번(AspNetUsers.PasswordHash)과 분리
  [LastLoginTS]               DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_UserProfile PRIMARY KEY CLUSTERED ([UserProfileID])
);
GO

-- ── SYS_RolePermission  (★ RBAC 매트릭스)
CREATE TABLE dbo.SYS_RolePermission (
  [RolePermissionID]          INT IDENTITY         NOT NULL,
  [RoleID]                    NVARCHAR(450)            NULL,  -- FK -> AspNetRoles.Id
  [RoleName]                  VARCHAR(40)              NULL,
  [ModuleCode]                VARCHAR(10)              NULL,
  [ScreenCode]                VARCHAR(20)              NULL,
  [PermissionLevel]           VARCHAR(10)              NULL,
  [IsSystemRole]              BIT                      NULL,
  [EffectiveTS]               DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  CONSTRAINT PK_SYS_RolePermission PRIMARY KEY CLUSTERED ([RolePermissionID])
);
GO

-- ── SYS_AuditLog  (★ 감사 로그 (append-only))
CREATE TABLE dbo.SYS_AuditLog (
  [LogID]                     BIGINT IDENTITY      NOT NULL,
  [EventTS]                   DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ActorUserID]               NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ModuleCode]                VARCHAR(10)              NULL,
  [ScreenCode]                VARCHAR(20)              NULL,
  [ActionType]                VARCHAR(15)              NULL,
  [TargetEntity]              VARCHAR(40)              NULL,
  [TargetID]                  VARCHAR(40)              NULL,
  [BeforeValueJSON]           NVARCHAR(MAX)            NULL,
  [AfterValueJSON]            NVARCHAR(MAX)            NULL,
  [IPAddress]                 VARCHAR(45)              NULL,
  [Result]                    VARCHAR(10)              NULL,
  [Note]                      NVARCHAR(500)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_AuditLog PRIMARY KEY CLUSTERED ([LogID])
);
GO

-- ── SYS_NotificationRule  (알림 규칙)
CREATE TABLE dbo.SYS_NotificationRule (
  [NotificationRuleID]        INT IDENTITY         NOT NULL,
  [EventTypeCode]             VARCHAR(20)              NULL,
  [EventName]                 NVARCHAR(60)             NULL,
  [ModuleCode]                VARCHAR(10)              NULL,
  [TriggerCondition]          NVARCHAR(500)            NULL,
  [IsEnabled]                 BIT                      NULL DEFAULT 1,
  [ChannelsJSON]              NVARCHAR(200)            NULL,
  [RecipientRolesJSON]        NVARCHAR(500)            NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  CONSTRAINT PK_SYS_NotificationRule PRIMARY KEY CLUSTERED ([NotificationRuleID])
);
GO

-- ── SYS_NotificationChannel  (사용자별 알림 채널)
CREATE TABLE dbo.SYS_NotificationChannel (
  [NotificationChannelID]     INT IDENTITY         NOT NULL,
  [UserID]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Channel]                   VARCHAR(10)              NULL,
  [Address]                   VARCHAR(255)             NULL,
  [IsEnabled]                 BIT                      NULL DEFAULT 1,
  [QuietHoursStart]           TIME                     NULL,
  [QuietHoursEnd]             TIME                     NULL,
  [VerifiedAt]                DATETIME2                NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_NotificationChannel PRIMARY KEY CLUSTERED ([NotificationChannelID])
);
GO

-- ── SYS_NotificationHistory  (알림 발송 이력)
CREATE TABLE dbo.SYS_NotificationHistory (
  [NotificationHistoryID]     BIGINT IDENTITY      NOT NULL,
  [NotificationRuleID]        INT                      NULL,  -- FK -> SYS_NotificationRule.NotificationRuleID
  [EventTypeCode]             VARCHAR(20)              NULL,
  [RecipientUserID]           NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Channel]                   VARCHAR(10)              NULL,
  [Address]                   VARCHAR(255)             NULL,
  [Subject]                   NVARCHAR(200)            NULL,
  [Body]                      NVARCHAR(MAX)            NULL,
  [SourceRefType]             VARCHAR(20)              NULL,
  [SourceRefID]               VARCHAR(40)              NULL,
  [Status]                    VARCHAR(15)              NULL,
  [RetryCount]                INT                      NULL,
  [SentAt]                    DATETIME2                NULL,
  [ReadAt]                    DATETIME2                NULL,
  [ErrorMsg]                  NVARCHAR(500)            NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_NotificationHistory PRIMARY KEY CLUSTERED ([NotificationHistoryID])
);
GO

-- ── SYS_Config  (환경설정 (Key-Value))
CREATE TABLE dbo.SYS_Config (
  [ConfigID]                  INT IDENTITY         NOT NULL,
  [ConfigKey]                 VARCHAR(60)              NULL,
  [ConfigType]                VARCHAR(15)              NULL,
  [Category]                  VARCHAR(30)              NULL,
  [ConfigValue]               NVARCHAR(500)            NULL,
  [CodeName]                  NVARCHAR(80)             NULL,
  [Unit]                      VARCHAR(10)              NULL,
  [UsedByModulesJSON]         NVARCHAR(500)            NULL,
  [SortOrder]                 INT                      NULL,
  [IsActive]                  BIT                      NULL DEFAULT 1,
  [ModifiedBy]                NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  CONSTRAINT PK_SYS_Config PRIMARY KEY CLUSTERED ([ConfigID])
);
GO

-- ── SYS_InterfaceMonitor  (인터페이스 상태)
CREATE TABLE dbo.SYS_InterfaceMonitor (
  [InterfaceMonitorID]        INT IDENTITY         NOT NULL,
  [InterfaceCode]             VARCHAR(20)              NULL,
  [InterfaceName]             NVARCHAR(60)             NULL,
  [Direction]                 VARCHAR(15)              NULL,
  [Endpoint]                  VARCHAR(255)             NULL,
  [Protocol]                  VARCHAR(15)              NULL,
  [ConnStatus]                VARCHAR(10)              NULL,
  [LastSyncTS]                DATETIME2                NULL,
  [MaxGapMinutes]             INT                      NULL,
  [LastRecordCount]           INT                      NULL,
  [RetryCount]                INT                      NULL,
  [LastErrorMsg]              NVARCHAR(1000)           NULL,
  [IsEnabled]                 BIT                      NULL DEFAULT 1,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_InterfaceMonitor PRIMARY KEY CLUSTERED ([InterfaceMonitorID])
);
GO

-- ── SYS_FactoryCalendar  (공장 캘린더 (교대 인스턴스))
CREATE TABLE dbo.SYS_FactoryCalendar (
  [FactoryCalendarID]         INT IDENTITY         NOT NULL,
  [CalendarDate]              DATE                     NULL,
  [DayType]                   VARCHAR(10)              NULL,
  [HolidayName]               NVARCHAR(40)             NULL,
  [ShiftCount]                INT                      NULL,
  [ShiftCode]                 VARCHAR(10)              NULL,
  [StartTime]                 TIME                     NULL,
  [EndTime]                   TIME                     NULL,
  [BreakMinutes]              INT                      NULL,
  [NetWorkHours]              DECIMAL(4,1)             NULL,
  [CalendarYear]              INT                      NULL,
  [PlantCode]                 VARCHAR(20)              NULL,
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
  [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS]                DATETIME2                NULL,
  [ModifiedBy]                NVARCHAR(450)            NULL,
  CONSTRAINT PK_SYS_FactoryCalendar PRIMARY KEY CLUSTERED ([FactoryCalendarID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: ID                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── AspNetUsers  (★ 사용자)
CREATE TABLE dbo.AspNetUsers (
  [Id]                        NVARCHAR(450)        NOT NULL,
  [UserName]                  NVARCHAR(256)            NULL,
  [NormalizedUserName]        NVARCHAR(256)            NULL,
  [Email]                     NVARCHAR(256)            NULL,
  [NormalizedEmail]           NVARCHAR(256)            NULL,
  [EmailConfirmed]            BIT                      NULL,
  [PasswordHash]              NVARCHAR(MAX)            NULL,
  [SecurityStamp]             NVARCHAR(MAX)            NULL,
  [ConcurrencyStamp]          NVARCHAR(MAX)            NULL,
  [PhoneNumber]               NVARCHAR(MAX)            NULL,
  [PhoneNumberConfirmed]      BIT                      NULL,
  [TwoFactorEnabled]          BIT                      NULL,
  [LockoutEnd]                DATETIMEOFFSET           NULL,
  [LockoutEnabled]            BIT                      NULL,
  [AccessFailedCount]         INT                      NULL,
  CONSTRAINT PK_AspNetUsers PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetRoles  (역할)
CREATE TABLE dbo.AspNetRoles (
  [Id]                        NVARCHAR(450)        NOT NULL,
  [Name]                      NVARCHAR(256)            NULL,
  [NormalizedName]            NVARCHAR(256)            NULL,
  [ConcurrencyStamp]          NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetRoles PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetUserRoles  (사용자×역할)
CREATE TABLE dbo.AspNetUserRoles (
  [UserId]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [RoleId]                    NVARCHAR(450)            NULL  -- FK -> AspNetRoles.Id
);
GO

-- ── AspNetUserClaims  (사용자 클레임)
CREATE TABLE dbo.AspNetUserClaims (
  [Id]                        INT IDENTITY         NOT NULL,
  [UserId]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClaimType]                 NVARCHAR(MAX)            NULL,
  [ClaimValue]                NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetUserClaims PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetUserLogins  (외부 로그인)
CREATE TABLE dbo.AspNetUserLogins (
  [LoginProvider]             NVARCHAR(450)        NOT NULL,
  [ProviderKey]               NVARCHAR(450)        NOT NULL,
  [ProviderDisplayName]       NVARCHAR(MAX)            NULL,
  [UserId]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  CONSTRAINT PK_AspNetUserLogins PRIMARY KEY CLUSTERED ([LoginProvider], [ProviderKey])
);
GO

-- ── AspNetUserTokens  (사용자 토큰)
CREATE TABLE dbo.AspNetUserTokens (
  [UserId]                    NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [LoginProvider]             NVARCHAR(450)        NOT NULL,
  [Name]                      NVARCHAR(450)        NOT NULL,
  [Value]                     NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetUserTokens PRIMARY KEY CLUSTERED ([LoginProvider], [Name])
);
GO

-- ── AspNetRoleClaims  (역할 클레임)
CREATE TABLE dbo.AspNetRoleClaims (
  [Id]                        INT IDENTITY         NOT NULL,
  [RoleId]                    NVARCHAR(450)            NULL,  -- FK -> AspNetRoles.Id
  [ClaimType]                 NVARCHAR(MAX)            NULL,
  [ClaimValue]                NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY CLUSTERED ([Id])
);
GO


-- ════════════════════════════════════════════════════════════════════════
-- Sample seed data (minimal — covers SAV/GEO plants, key items, vendors)
-- ════════════════════════════════════════════════════════════════════════

-- UOMs
INSERT INTO dbo.MD_Uom (UOMCode, UOMName, UOMCategory, BaseFlag, ConvFactor, DecimalPrec, Symbol, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('EA',  'Each',       'QTY',    1, 1,      0, 'ea',  1, 'admin', SYSDATETIME()),
  ('BOX', 'Box',        'QTY',    0, 1,      0, 'box', 1, 'admin', SYSDATETIME()),
  ('PLT', 'Pallet',     'QTY',    0, 1,      0, 'plt', 1, 'admin', SYSDATETIME()),
  ('LB',  'Pound',      'WEIGHT', 1, 1,      2, 'lb',  1, 'admin', SYSDATETIME()),
  ('KG',  'Kilogram',   'WEIGHT', 0, 2.205,  3, 'kg',  1, 'admin', SYSDATETIME()),
  ('M',   'Meter',      'LENGTH', 1, 1,      3, 'm',   1, 'admin', SYSDATETIME()),
  ('FT',  'Foot',       'LENGTH', 0, 0.3048, 2, 'ft',  1, 'admin', SYSDATETIME()),
  ('GAL', 'Gallon',     'VOLUME', 1, 1,      2, 'gal', 1, 'admin', SYSDATETIME()),
  ('HR',  'Hour',       'TIME',   1, 1,      2, 'hr',  1, 'admin', SYSDATETIME());
GO

-- Customers
INSERT INTO dbo.MD_Customer (CustomerID, CustomerCode, CustomerName, CustomerNameEn, CustomerType, Country, EDIFlag, CurrencyCode, Status, CreatedBy, CreatedTS) VALUES
  ('SEMS',    'SEMS',  N'SEYON E-HWA Savannah',  'SEMS',     'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('SEMG',    'SEMG',  N'SEYON E-HWA Georgia',   'SEMG',  'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME());
GO

-- Vendors
INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, Phone, Email, EdiFlag, OtdTargetRate, PaymentTerms, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('SUP-CHEM',  N'ChemTech Industries',   'SUPPLIER', N'Polymer/Resin',  '(313) 555-0142', 'sales@chemtech.us',    1, 98.50, 'Net 30', 1, 'admin', SYSDATETIME()),
  ('SUP-EAST',  N'Eastern Coatings Co.',  'SUPPLIER', N'Paint/Powder',   '(419) 555-0188', 'orders@eastcoat.us',   1, 97.00, 'Net 45', 1, 'admin', SYSDATETIME()),
  ('SUP-PREC',  N'Precision Mold Inc.',   'SUPPLIER', N'Mold/Tooling',   '(216) 555-0211', 'support@precmold.us',  0, 95.00, 'Net 60', 1, 'admin', SYSDATETIME()),
  ('SUP-ABS',   N'ABS Resin Inc.',        'SUPPLIER', N'Resin',          '(513) 555-0367', 'sales@absresin.us',    1, 98.00, 'Net 30', 1, 'admin', SYSDATETIME()),
  ('SUP-HAARTZ',N'Haartz Corporation',    'SUPPLIER', N'Fabric',         '(978) 555-0421', 'fabric@haartz.us',     1, 99.00, 'Net 30', 1, 'admin', SYSDATETIME());
GO

-- Production Lines
INSERT INTO dbo.MD_Line (LineID, LineName, LineType, PlantCode, DailyCap, ShiftPattern, RfidEnabledFlag, Status, CreatedBy, CreatedTS) VALUES
  ('LINE-INJ-01', N'Injection Line 1 (650T)',  'INJECTION', 'SAV', 4800, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-INJ-02', N'Injection Line 2 (850T)',  'INJECTION', 'SAV', 3600, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-IMG-01', N'Wrapping Line 1',           'WRAPPING',  'SAV', 1200, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-01', N'Paint Line 1 (Powder)',     'PAINTING',  'GEO',  800, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-02', N'Paint Line 2 (Liquid)',     'PAINTING',  'GEO',  600, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME());
GO

-- Items (LQ2 rear door trim part master — docs/PartMaster_LQ2.xls)
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, SafetyStock, PGN, ALC, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('83335-P8000BM1',  N'GARNISH-RR DR UPR, LH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8132',   1, 'admin', SYSDATETIME()),
  ('83335-P8000DNN',  N'GARNISH-RR DR UPR, LH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8133',   1, 'admin', SYSDATETIME()),
  ('83335-P8000JY2',  N'GARNISH-RR DR UPR, LH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8175',   1, 'admin', SYSDATETIME()),
  ('83335-P8000RBQ',  N'GARNISH-RR DR UPR, LH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8046',   1, 'admin', SYSDATETIME()),
  ('83345-P8000BM1',  N'GARNISH-RR DR UPR, RH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8132',   1, 'admin', SYSDATETIME()),
  ('83345-P8000DNN',  N'GARNISH-RR DR UPR, RH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8133',   1, 'admin', SYSDATETIME()),
  ('83345-P8000JY2',  N'GARNISH-RR DR UPR, RH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8175',   1, 'admin', SYSDATETIME()),
  ('83345-P8000RBQ',  N'GARNISH-RR DR UPR, RH',                    'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8046',   1, 'admin', SYSDATETIME()),
  ('M83371-P8000RBQ', N'MODULE RR DR TRIM UPR, LH (FAKE STITCH)',  'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8106',   1, 'admin', SYSDATETIME()),
  ('M83371-P8010RBQ', N'MODULE RR DR TRIM UPR,LH (FAKE+CUR)',      'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q034',  '8046',   1, 'admin', SYSDATETIME()),
  ('M83381-P8000RBQ', N'MODULE RR DR TRIM UPR, RH (FAKE STITCH)',  'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8106',   1, 'admin', SYSDATETIME()),
  ('M83381-P8010RBQ', N'MODULE RR DR TRIM UPR,RH (FAKE+CUR)',      'ASSY',  N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'Q035',  '8046',   1, 'admin', SYSDATETIME()),
  ('83314-P8000',     N'RAIL-RR DR TRIM UPR, LH (FAKE STITCH)',    'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL001',  1, 'admin', SYSDATETIME()),
  ('83314-P8010',     N'RAIL-RR DR TRIM UPR, LH (FAKE STITCH, +C', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL003',  1, 'admin', SYSDATETIME()),
  ('83324-P8000',     N'RAIL-RR DR TRIM UPR, RH (FAKE STITCH)',    'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL002',  1, 'admin', SYSDATETIME()),
  ('83324-P8010',     N'RAIL-RR DR TRIM UPR, RH (FAKE STITCH, +C', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL004',  1, 'admin', SYSDATETIME()),
  ('83371-P8010RBQ',  N'PNL-RR DR TRIM UPR, LH (FAKE STITCH, +CU', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'QSUB',  '8047',   1, 'admin', SYSDATETIME()),
  ('83381-P8000RBQ',  N'PNL-RR DR TRIM UPR, RH (FAKE STITCH)',     'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'QSUB',  '8048',   1, 'admin', SYSDATETIME()),
  ('83381-P8010RBQ',  N'PNL-RR DR TRIM UPR, RH (FAKE STITCH, +CU', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'QSUB',  '8049',   1, 'admin', SYSDATETIME()),
  ('D0133-P8000',     N'CORE (IMG)-PNL-RR DR TRIM UPR, LH',        'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL007',  1, 'admin', SYSDATETIME()),
  ('D0133-P8010',     N'CORE (IMG)-PNL-RR DR TRIM UPR, LH (+CURT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL009',  1, 'admin', SYSDATETIME()),
  ('D0143-P8000',     N'CORE (IMG)-PNL-RR DR TRIM UPR, RH',        'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL008',  1, 'admin', SYSDATETIME()),
  ('D0143-P8010',     N'CORE (IMG)-PNL-RR DR TRIM UPR, RH (+CURT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL010',  1, 'admin', SYSDATETIME()),
  ('D3133-P8000',     N'CORE (IMG)-GARNISH-RR DR UPR, LH',         'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL005',  1, 'admin', SYSDATETIME()),
  ('D3143-P8000',     N'CORE (IMG)-GARNISH-RR DR UPR, RH',         'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'AQFG',  'DL006',  1, 'admin', SYSDATETIME()),
  ('M0230-P8000RBQ',  N'MODULE-PNL-RR DR TRIM UPR, LH (FAKE STIT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'XXXX',  'L001',   1, 'admin', SYSDATETIME()),
  ('M0230-P8010RBQ',  N'MODULE-PNL-RR DR TRIM UPR, LH (FAKE STIT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'XXXX',  'L002',   1, 'admin', SYSDATETIME()),
  ('M0240-P8000RBQ',  N'MODULE-PNL-RR DR TRIM UPR, RH (FAKE STIT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'XXXX',  'L003',   1, 'admin', SYSDATETIME()),
  ('M0240-P8010RBQ',  N'MODULE-PNL-RR DR TRIM UPR, RH (FAKE STIT', 'SUB',   N'LQ2 D/Trim', 'LQ2',  'EA', 0, 'XXXX',  'L004',   1, 'admin', SYSDATETIME());
GO

-- Items (ME1A part master — docs/PartMaster_ME1A.xls)
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, SafetyStock, PGN, ALC, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('81710-TD000NNB',     N'TRIM ASSY-TAIL GATE LWR',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q017',  'S00B',   1, 'admin', SYSDATETIME()),
  ('81710-TD000PNY',     N'TRIM ASSY-TAIL GATE LWR',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q017',  'S00P',   1, 'admin', SYSDATETIME()),
  ('81711-TD010NNB',     N'TRIM - TAIL GATE LWR',                         'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M011',   1, 'admin', SYSDATETIME()),
  ('81711-TD010PNY',     N'TRIM - TAIL GATE LWR',                         'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M012',   1, 'admin', SYSDATETIME()),
  ('82301-TD030NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S30B',   1, 'admin', SYSDATETIME()),
  ('82301-TD030YGN',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S30G',   1, 'admin', SYSDATETIME()),
  ('82301-TD0804NB',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S804',   1, 'admin', SYSDATETIME()),
  ('82301-TD090NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S90B',   1, 'admin', SYSDATETIME()),
  ('82301-TD090PNY',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S90P',   1, 'admin', SYSDATETIME()),
  ('82301-TD090VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S90K',   1, 'admin', SYSDATETIME()),
  ('82301-TD090YGN',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'S90G',   1, 'admin', SYSDATETIME()),
  ('82301-TD100NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SA0B',   1, 'admin', SYSDATETIME()),
  ('82301-TD100VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SA0K',   1, 'admin', SYSDATETIME()),
  ('82301-TD100YGN',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SA0G',   1, 'admin', SYSDATETIME()),
  ('82301-TD130NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SD0B',   1, 'admin', SYSDATETIME()),
  ('82301-TD130VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SD0K',   1, 'admin', SYSDATETIME()),
  ('82301-TD130YGN',     N'PNL ASSY-FR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q023',  'SD0G',   1, 'admin', SYSDATETIME()),
  ('82302-12345678',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('82302-TD000NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S00B',   1, 'admin', SYSDATETIME()),
  ('82302-TD000YGN',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S00G',   1, 'admin', SYSDATETIME()),
  ('82302-TD010NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S10B',   1, 'admin', SYSDATETIME()),
  ('82302-TD010VKE',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S10K',   1, 'admin', SYSDATETIME()),
  ('82302-TD010YGN',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S10G',   1, 'admin', SYSDATETIME()),
  ('82302-TD020NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S20B',   1, 'admin', SYSDATETIME()),
  ('82302-TD020VKE',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S20K',   1, 'admin', SYSDATETIME()),
  ('82302-TD020YGN',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S20G',   1, 'admin', SYSDATETIME()),
  ('82302-TD030NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('82302-TD040NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S40B',   1, 'admin', SYSDATETIME()),
  ('82302-TD040PNY',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S40P',   1, 'admin', SYSDATETIME()),
  ('82302-TD040VKE',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S40K',   1, 'admin', SYSDATETIME()),
  ('82302-TD040YGN',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S40G',   1, 'admin', SYSDATETIME()),
  ('82302-TD0504NB',     N'PNL ASSY-FR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q024',  'S504',   1, 'admin', SYSDATETIME()),
  ('82311-TD000NNB',     N'PNL ASSY-FR DR TRIM UPR, LH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M094',   1, 'admin', SYSDATETIME()),
  ('82311-TD000PNY',     N'PNL ASSY-FR DR TRIM UPR, LH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M096',   1, 'admin', SYSDATETIME()),
  ('82311-TD000VKE',     N'PNL ASSY-FR DR TRIM UPR, LH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M097',   1, 'admin', SYSDATETIME()),
  ('82311-TD000YGN',     N'PNL ASSY-FR DR TRIM UPR, LH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M095',   1, 'admin', SYSDATETIME()),
  ('82311-TD000YGU',     N'PNL ASSY-FR DR TRIM UPR, LH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('82321-TD000NNB',     N'PNL ASSY-FR DR TRIM UPR, RH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M098',   1, 'admin', SYSDATETIME()),
  ('82321-TD000PNY',     N'PNL ASSY-FR DR TRIM UPR, RH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M101',   1, 'admin', SYSDATETIME()),
  ('82321-TD000VKE',     N'PNL ASSY-FR DR TRIM UPR, RH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M100',   1, 'admin', SYSDATETIME()),
  ('82321-TD000YGN',     N'PNL ASSY-FR DR TRIM UPR, RH (NON DSM)',        'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M099',   1, 'admin', SYSDATETIME()),
  ('82330-TD100NNB',     N'PNL ASSY-FR DR CTR, LH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M121',   1, 'admin', SYSDATETIME()),
  ('82330-TD100YGU',     N'PNL ASSY-FR DR CTR, LH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M122',   1, 'admin', SYSDATETIME()),
  ('82330-TD200NNB',     N'PNL ASSY-FR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M118',   1, 'admin', SYSDATETIME()),
  ('82330-TD200ROG',     N'PNL ASSY-FR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M119',   1, 'admin', SYSDATETIME()),
  ('82330-TD200YGU',     N'PNL ASSY-FR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M120',   1, 'admin', SYSDATETIME()),
  ('82340-TD100NNB',     N'PNL ASSY-FR DR CTR, RH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M123',   1, 'admin', SYSDATETIME()),
  ('82340-TD100YGU',     N'PNL ASSY-FR DR CTR, RH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M124',   1, 'admin', SYSDATETIME()),
  ('82340-TD200NNB',     N'PNL ASSY-FR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M125',   1, 'admin', SYSDATETIME()),
  ('82340-TD200ROG',     N'PNL ASSY-FR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M126',   1, 'admin', SYSDATETIME()),
  ('82340-TD200YGU',     N'PNL ASSY-FR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M127',   1, 'admin', SYSDATETIME()),
  ('82350-TD000NNB',     N'PNL ASSY-FR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M005',   1, 'admin', SYSDATETIME()),
  ('82350-TD000ROG',     N'PNL ASSY-FR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M007',   1, 'admin', SYSDATETIME()),
  ('82350-TD000YGU',     N'PNL ASSY-FR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M006',   1, 'admin', SYSDATETIME()),
  ('82351-TD000NNB',     N'GRILLE ASSY-FR DR SPEAKER, LH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M138',   1, 'admin', SYSDATETIME()),
  ('82351-TD000YGU',     N'GRILLE ASSY-FR DR SPEAKER, LH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M139',   1, 'admin', SYSDATETIME()),
  ('82360-TD000NNB',     N'PNL ASSY-FR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M005',   1, 'admin', SYSDATETIME()),
  ('82360-TD000ROG',     N'PNL ASSY-FR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M007',   1, 'admin', SYSDATETIME()),
  ('82360-TD000YGU',     N'PNL ASSY-FR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M006',   1, 'admin', SYSDATETIME()),
  ('82361-TD000NNB',     N'GRILLE ASSY-FR DR SPEAKER, RH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M138',   1, 'admin', SYSDATETIME()),
  ('82361-TD000YGU',     N'GRILLE ASSY-FR DR SPEAKER, RH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M139',   1, 'admin', SYSDATETIME()),
  ('83301-12345678',     N'PNL ASSY-RR DR TRIM COMPL,LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'N10B',   1, 'admin', SYSDATETIME()),
  ('83301-TD010NNB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S10B',   1, 'admin', SYSDATETIME()),
  ('83301-TD010YGN',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S10G',   1, 'admin', SYSDATETIME()),
  ('83301-TD060NNB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S60B',   1, 'admin', SYSDATETIME()),
  ('83301-TD060PNY',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S60P',   1, 'admin', SYSDATETIME()),
  ('83301-TD060VKE',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S60K',   1, 'admin', SYSDATETIME()),
  ('83301-TD060YGN',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S60G',   1, 'admin', SYSDATETIME()),
  ('83301-TD0804NB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'S804',   1, 'admin', SYSDATETIME()),
  ('83301-TD100NNB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SA0B',   1, 'admin', SYSDATETIME()),
  ('83301-TD100VKE',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SA0K',   1, 'admin', SYSDATETIME()),
  ('83301-TD100YGN',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SA0G',   1, 'admin', SYSDATETIME()),
  ('83301-TD120NNB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SC0B',   1, 'admin', SYSDATETIME()),
  ('83301-TD120VKE',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SC0K',   1, 'admin', SYSDATETIME()),
  ('83301-TD120YGN',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SC0G',   1, 'admin', SYSDATETIME()),
  ('83301-TD130NNB',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SD0B',   1, 'admin', SYSDATETIME()),
  ('83301-TD130VKE',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SD0K',   1, 'admin', SYSDATETIME()),
  ('83301-TD130YGN',     N'PNL ASSY-RR DR TRIM COMPL, LH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q033',  'SD0G',   1, 'admin', SYSDATETIME()),
  ('83302-12345678',     N'PNL ASSY-RR DR TRIM COMPL,RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'N40B',   1, 'admin', SYSDATETIME()),
  ('83302-TD010NNB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S10B',   1, 'admin', SYSDATETIME()),
  ('83302-TD010YGN',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S10G',   1, 'admin', SYSDATETIME()),
  ('83302-TD060NNB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S60B',   1, 'admin', SYSDATETIME()),
  ('83302-TD060PNY',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S60P',   1, 'admin', SYSDATETIME()),
  ('83302-TD060VKE',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S60K',   1, 'admin', SYSDATETIME()),
  ('83302-TD060YGN',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S60G',   1, 'admin', SYSDATETIME()),
  ('83302-TD0804NB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'S804',   1, 'admin', SYSDATETIME()),
  ('83302-TD100NNB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SA0B',   1, 'admin', SYSDATETIME()),
  ('83302-TD100VKE',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SA0K',   1, 'admin', SYSDATETIME()),
  ('83302-TD100YGN',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SA0G',   1, 'admin', SYSDATETIME()),
  ('83302-TD120NNB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SC0B',   1, 'admin', SYSDATETIME()),
  ('83302-TD120VKE',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SC0K',   1, 'admin', SYSDATETIME()),
  ('83302-TD120YGN',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SC0G',   1, 'admin', SYSDATETIME()),
  ('83302-TD130NNB',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SD0B',   1, 'admin', SYSDATETIME()),
  ('83302-TD130VKE',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SD0K',   1, 'admin', SYSDATETIME()),
  ('83302-TD130YGN',     N'PNL ASSY-RR DR TRIM COMPL, RH',                'ASSY', NULL, 'ME1A', 'EA', 0, 'Q034',  'SD0G',   1, 'admin', SYSDATETIME()),
  ('83311-TD000NNB',     N'PNL ASSY-RR DR TRIM UPR, LH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M102',   1, 'admin', SYSDATETIME()),
  ('83311-TD000PNY',     N'PNL ASSY-RR DR TRIM UPR, LH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M105',   1, 'admin', SYSDATETIME()),
  ('83311-TD000VKE',     N'PNL ASSY-RR DR TRIM UPR, LH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M107',   1, 'admin', SYSDATETIME()),
  ('83311-TD000YGN',     N'PNL ASSY-RR DR TRIM UPR, LH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M103',   1, 'admin', SYSDATETIME()),
  ('83311-TD100NNB',     N'PNL ASSY-RR DR TRIM UPR, LH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M104',   1, 'admin', SYSDATETIME()),
  ('83311-TD100PNY',     N'PNL ASSY-RR DR TRIM UPR, LH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M106',   1, 'admin', SYSDATETIME()),
  ('83311-TD100VKE',     N'PNL ASSY-RR DR TRIM UPR, LH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M108',   1, 'admin', SYSDATETIME()),
  ('83311-TD100YGN',     N'PNL ASSY-RR DR TRIM UPR, LH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M109',   1, 'admin', SYSDATETIME()),
  ('83321-TD000NNB',     N'PNL ASSY-RR DR TRIM UPR, RH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M110',   1, 'admin', SYSDATETIME()),
  ('83321-TD000PNY',     N'PNL ASSY-RR DR TRIM UPR, RH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M113',   1, 'admin', SYSDATETIME()),
  ('83321-TD000VKE',     N'PNL ASSY-RR DR TRIM UPR, RH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M115',   1, 'admin', SYSDATETIME()),
  ('83321-TD000YGN',     N'PNL ASSY-RR DR TRIM UPR, RH (NON CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M111',   1, 'admin', SYSDATETIME()),
  ('83321-TD100NNB',     N'PNL ASSY-RR DR TRIM UPR, RH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M112',   1, 'admin', SYSDATETIME()),
  ('83321-TD100PNY',     N'PNL ASSY-RR DR TRIM UPR, RH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M114',   1, 'admin', SYSDATETIME()),
  ('83321-TD100VKE',     N'PNL ASSY-RR DR TRIM UPR, RH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M116',   1, 'admin', SYSDATETIME()),
  ('83321-TD100YGN',     N'PNL ASSY-RR DR TRIM UPR, RH (+CURTAIN)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M117',   1, 'admin', SYSDATETIME()),
  ('83330-TD100NNB',     N'PNL ASSY-RR DR CTR, LH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M131',   1, 'admin', SYSDATETIME()),
  ('83330-TD100YGU',     N'PNL ASSY-RR DR CTR, LH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M132',   1, 'admin', SYSDATETIME()),
  ('83330-TD200NNB',     N'PNL ASSY-RR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M128',   1, 'admin', SYSDATETIME()),
  ('83330-TD200ROG',     N'PNL ASSY-RR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M129',   1, 'admin', SYSDATETIME()),
  ('83330-TD200YGU',     N'PNL ASSY-RR DR CTR, LH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M130',   1, 'admin', SYSDATETIME()),
  ('83340-TD100NNB',     N'PNL ASSY-RR DR CTR, RH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M136',   1, 'admin', SYSDATETIME()),
  ('83340-TD100YGU',     N'PNL ASSY-RR DR CTR, RH (LEATHER)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M137',   1, 'admin', SYSDATETIME()),
  ('83340-TD200NNB',     N'PNL ASSY-RR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M133',   1, 'admin', SYSDATETIME()),
  ('83340-TD200ROG',     N'PNL ASSY-RR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M134',   1, 'admin', SYSDATETIME()),
  ('83340-TD200YGU',     N'PNL ASSY-RR DR CTR, RH (LEATHER + PERPOR',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M135',   1, 'admin', SYSDATETIME()),
  ('83350-TD000NNB',     N'PNL ASSY-RR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M008',   1, 'admin', SYSDATETIME()),
  ('83350-TD000ROG',     N'PNL ASSY-RR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M010',   1, 'admin', SYSDATETIME()),
  ('83350-TD000YGU',     N'PNL ASSY-RR DR TRIM LWR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M009',   1, 'admin', SYSDATETIME()),
  ('83351-TD000NNB',     N'GRILLE ASSY-RR DR SPEAKER, LH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M140',   1, 'admin', SYSDATETIME()),
  ('83351-TD000YGU',     N'GRILLE ASSY-RR DR SPEAKER, LH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M141',   1, 'admin', SYSDATETIME()),
  ('83360-TD000NNB',     N'PNL ASSY-RR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M008',   1, 'admin', SYSDATETIME()),
  ('83360-TD000ROG',     N'PNL ASSY-RR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M010',   1, 'admin', SYSDATETIME()),
  ('83360-TD000YGU',     N'PNL ASSY-RR DR TRIM LWR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M009',   1, 'admin', SYSDATETIME()),
  ('83361-TD000NNB',     N'GRILLE ASSY-RR DR SPEAKER, RH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M140',   1, 'admin', SYSDATETIME()),
  ('83361-TD000YGU',     N'GRILLE ASSY-RR DR SPEAKER, RH',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M141',   1, 'admin', SYSDATETIME()),
  ('85300-TD090YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'M90Y',   1, 'admin', SYSDATETIME()),
  ('85300-TD110YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'MB0Y',   1, 'admin', SYSDATETIME()),
  ('85300-TD350YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'M1AY',   1, 'admin', SYSDATETIME()),
  ('85300-TD500ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD500YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD510ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD510YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD520ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD520YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD530ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD530YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD540ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD540YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD550ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD550YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD560ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD560YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD570ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD570YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'SPAY',   1, 'admin', SYSDATETIME()),
  ('85300-TD580NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD580ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD580YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD590NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD590ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD590YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD600NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD600ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD600YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD610NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD610ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD610YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD620NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD620ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD620YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD630NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD630ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD630YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD640NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD640ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD640YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD650NNB',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD650ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD650YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD660ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD660YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'SYAY',   1, 'admin', SYSDATETIME()),
  ('85300-TD670ROG',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-TD670YGU',     N'COMPLETE ASSY-HEAD LINING(STD)',               'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85310-TD000ROG',     N'HEADLINING ASSY (TRICOT, STD)',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X045',   1, 'admin', SYSDATETIME()),
  ('85310-TD000YGU',     N'HEADLINING ASSY (TRICOT, STD)',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M166',   1, 'admin', SYSDATETIME()),
  ('85310-TD100NNB',     N'HEADLINING ASSY (SUEDE, STD)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X034',   1, 'admin', SYSDATETIME()),
  ('85310-TD100ROG',     N'HEADLINING ASSY (SUEDE, STD)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X035',   1, 'admin', SYSDATETIME()),
  ('85310-TD100YGU',     N'HEADLINING ASSY (SUEDE, STD)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X036',   1, 'admin', SYSDATETIME()),
  ('85310-TD200ROG',     N'HEADLINING ASSY (TRICOT+MIC, STD)',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X049',   1, 'admin', SYSDATETIME()),
  ('85310-TD200YGU',     N'HEADLINING ASSY (TRICOT+MIC, STD)',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X050',   1, 'admin', SYSDATETIME()),
  ('85310-TD300NNB',     N'HEADLINING ASSY (SUEDE+MIC, STD)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X040',   1, 'admin', SYSDATETIME()),
  ('85310-TD300ROG',     N'HEADLINING ASSY (SUEDE+MIC, STD)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X041',   1, 'admin', SYSDATETIME()),
  ('85310-TD300YGU',     N'HEADLINING ASSY (SUEDE+MIC, STD)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X042',   1, 'admin', SYSDATETIME()),
  ('85321-TD000',        N'BOARD-HEADLINING',                             'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X001',   1, 'admin', SYSDATETIME()),
  ('85321-TD000YGU',     N'BOARD-HEADLINING (CLOTH)',                     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M083',   1, 'admin', SYSDATETIME()),
  ('85321-TD10E',        N'PU BLOCK-BOARD-ME1A',                          'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD080YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'L80Y',   1, 'admin', SYSDATETIME()),
  ('85400-TD240YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'LQ0Y',   1, 'admin', SYSDATETIME()),
  ('85400-TD280NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'LU0B',   1, 'admin', SYSDATETIME()),
  ('85400-TD280ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'LU0D',   1, 'admin', SYSDATETIME()),
  ('85400-TD280YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'LU0Y',   1, 'admin', SYSDATETIME()),
  ('85400-TD500ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD500YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD510ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD510YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD520ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD520YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'SJAY',   1, 'admin', SYSDATETIME()),
  ('85400-TD530ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD530YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD540ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD540YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD550ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD550YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD560ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD560YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD570ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD570YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD580NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD580ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD580YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD590NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD590ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD590YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD600NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD600ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD600YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD610NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD610ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD610YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD620NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD620ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD620YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD630NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD630ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD630YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD640NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD640ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD640YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD650NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD650ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD650YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD660ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD660YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD670ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD670YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD680ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD680YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD690ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD690YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD700ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD700YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD710ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD710YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD720ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD720YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD730ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD730YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD740NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'S6LB',   1, 'admin', SYSDATETIME()),
  ('85400-TD740ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD740YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'S6LY',   1, 'admin', SYSDATETIME()),
  ('85400-TD750NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD750ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD750YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD760NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'S8LB',   1, 'admin', SYSDATETIME()),
  ('85400-TD760ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD760YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, 'Q053',  'S8LY',   1, 'admin', SYSDATETIME()),
  ('85400-TD770NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD770ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD770YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD780NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD780ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD780YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD790NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD790ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD790YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD800NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD800ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD800YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD810NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD810ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD810YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD820ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD820YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD830ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD830YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD840ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD840YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD850ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD850YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD860ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD860YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD870ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD870YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD880ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD880YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD890ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD890YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD900NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD900ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD900YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD910NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD910ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD910YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD920NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD920ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD920YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD930NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD930ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD930YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD940NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD940ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD940YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD950NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD950ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD950YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD960NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD960ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD960YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD970NNB',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD970ROG',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-TD970YGU',     N'COMPLETE ASSY-HEAD LINING(PANO)',              'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85410-TD000ROG',     N'HEADLINING ASSY (TRICOT, SRF)',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X043',   1, 'admin', SYSDATETIME()),
  ('85410-TD000YGU',     N'HEADLINING ASSY (TRICOT, SRF)',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X044',   1, 'admin', SYSDATETIME()),
  ('85410-TD100NNB',     N'HEADLINING ASSY (SUEDE, SRF)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X031',   1, 'admin', SYSDATETIME()),
  ('85410-TD100ROG',     N'HEADLINING ASSY (SUEDE, SRF)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X032',   1, 'admin', SYSDATETIME()),
  ('85410-TD100YGU',     N'HEADLINING ASSY (SUEDE, SRF)',                 'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X033',   1, 'admin', SYSDATETIME()),
  ('85410-TD200ROG',     N'HEADLINING ASSY (TRICOT+MIC, SRF)',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X047',   1, 'admin', SYSDATETIME()),
  ('85410-TD200YGU',     N'HEADLINING ASSY (TRICOT+MIC, SRF)',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M084',   1, 'admin', SYSDATETIME()),
  ('85410-TD300NNB',     N'HEADLINING ASSY (SUEDE+MIC, SRF)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX01',   1, 'admin', SYSDATETIME()),
  ('85410-TD300ROG',     N'HEADLINING ASSY (SUEDE+MIC, SRF)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M085',   1, 'admin', SYSDATETIME()),
  ('85410-TD300YGU',     N'HEADLINING ASSY (SUEDE+MIC, SRF)',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M086',   1, 'admin', SYSDATETIME()),
  ('85412-TD000',        N'ROOF DUCT ASSY-HEADLINING STD',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M180',   1, 'admin', SYSDATETIME()),
  ('85412-TD100',        N'ROOF DUCT ASSY-HEADLINING PRF',                'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M181',   1, 'admin', SYSDATETIME()),
  ('85412-TD10E',        N'PU BLOCK-DUCT-ME1A',                           'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85421-TD000',        N'BOARD-HEADLINING',                             'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X002',   1, 'admin', SYSDATETIME()),
  ('85730-12345678',     N'TRIM ASSY-LUGGAGE SIDE,LH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'N50B',   1, 'admin', SYSDATETIME()),
  ('85730-TD010NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'S10B',   1, 'admin', SYSDATETIME()),
  ('85730-TD010PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'S10P',   1, 'admin', SYSDATETIME()),
  ('85730-TD010YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'S10Y',   1, 'admin', SYSDATETIME()),
  ('85730-TD040NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'S40B',   1, 'admin', SYSDATETIME()),
  ('85730-TD040YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'S40Y',   1, 'admin', SYSDATETIME()),
  ('85730-TD510NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD510PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD510YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD520NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SJAB',   1, 'admin', SYSDATETIME()),
  ('85730-TD520PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SJAP',   1, 'admin', SYSDATETIME()),
  ('85730-TD520YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SJAY',   1, 'admin', SYSDATETIME()),
  ('85730-TD530NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD530PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD530YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD540NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SLAB',   1, 'admin', SYSDATETIME()),
  ('85730-TD540PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SLAP',   1, 'admin', SYSDATETIME()),
  ('85730-TD540YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SLAY',   1, 'admin', SYSDATETIME()),
  ('85730-TD550NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SMAB',   1, 'admin', SYSDATETIME()),
  ('85730-TD550PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SMAP',   1, 'admin', SYSDATETIME()),
  ('85730-TD550YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q114',  'SMAY',   1, 'admin', SYSDATETIME()),
  ('85730-TD560NNB',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD560PNY',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85730-TD560YGU',     N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85731-TD000NNB',     N'TRIM-LUGG SIDE, LH',                           'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M001',   1, 'admin', SYSDATETIME()),
  ('85731-TD000PNY',     N'TRIM-LUGG SIDE, LH',                           'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M002',   1, 'admin', SYSDATETIME()),
  ('85740-12345678',     N'TRIM ASSY-LUGGAGE SIDE,RH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85740-TD020NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'S20B',   1, 'admin', SYSDATETIME()),
  ('85740-TD020PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'S20P',   1, 'admin', SYSDATETIME()),
  ('85740-TD020YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'S20Y',   1, 'admin', SYSDATETIME()),
  ('85740-TD260NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SS0B',   1, 'admin', SYSDATETIME()),
  ('85740-TD260YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SS0Y',   1, 'admin', SYSDATETIME()),
  ('85740-TD360NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'S2AB',   1, 'admin', SYSDATETIME()),
  ('85740-TD360YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'S2AY',   1, 'admin', SYSDATETIME()),
  ('85740-TD500NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD500PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD500YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD520NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJAB',   1, 'admin', SYSDATETIME()),
  ('85740-TD520PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJAP',   1, 'admin', SYSDATETIME()),
  ('85740-TD520YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJAY',   1, 'admin', SYSDATETIME()),
  ('85740-TD540NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD540PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD540YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD550NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SMAB',   1, 'admin', SYSDATETIME()),
  ('85740-TD550PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SMAP',   1, 'admin', SYSDATETIME()),
  ('85740-TD550YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SMAY',   1, 'admin', SYSDATETIME()),
  ('85740-TD560NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD560PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD560YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD580NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD580PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD580YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD600NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD600PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD600YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD620NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD620PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD620YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD640NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD640PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD640YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD650NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD650PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD650YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD660NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD660PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD660YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD680NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD680PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD680YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD700NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD700PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD700YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD720NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD720PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD720YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD740NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD740PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD740YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD750NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD750PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD750YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD760NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD760PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD760YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD780NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD780PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD780YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD800NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD800PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD800YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD820NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD820PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD820YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD840NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD840PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD840YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD850NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD850PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD850YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD860NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJLB',   1, 'admin', SYSDATETIME()),
  ('85740-TD860PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJLP',   1, 'admin', SYSDATETIME()),
  ('85740-TD860YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, 'Q112',  'SJLY',   1, 'admin', SYSDATETIME()),
  ('85740-TD880NNB',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD880PNY',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85740-TD880YGU',     N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85741-TD000NNB',     N'TRIM-LUGG SIDE, RH',                           'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M003',   1, 'admin', SYSDATETIME()),
  ('85741-TD000PNY',     N'TRIM-LUGG SIDE, RH',                           'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M004',   1, 'admin', SYSDATETIME()),
  ('85770-TD000NNB',   N'TRIM ASSY-RR TRANSVERSE',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q113',  'S00B',   1, 'admin', SYSDATETIME()),
  ('85770-TD100NNB',   N'TRIM ASSY-RR TRANSVERSE',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q113',  'SA0B',   1, 'admin', SYSDATETIME()),
  ('85770-TD100PNY',   N'TRIM ASSY-RR TRANSVERSE',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q113',  'SA0P',   1, 'admin', SYSDATETIME()),
  ('85771-TD100NNB',     N'TRIM-RR TRANSVERSE (OPT)',                     'SUB',  NULL, 'ME1A', 'EA', 0, 'Q113',  'ZZZ5',   1, 'admin', SYSDATETIME()),
  ('85823-TD000NNB',   N'TRIM ASSY-COWL SIDE, LH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q060',  'S00B',   1, 'admin', SYSDATETIME()),
  ('85823-TD000ROG',   N'TRIM ASSY-COWL SIDE, LH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q060',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85823-TD000YGU',   N'TRIM ASSY-COWL SIDE, LH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q060',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85824-TD000NNB',   N'TRIM ASSY-COWL SIDE, RH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q061',  'S00B',   1, 'admin', SYSDATETIME()),
  ('85824-TD000ROG',   N'TRIM ASSY-COWL SIDE, RH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q061',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85824-TD000YGU',   N'TRIM ASSY-COWL SIDE, RH',                      'ASSY', NULL, 'ME1A', 'EA', 0, 'Q061',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85835-TD000NNB',   N'TRIM ASSY-CTR PILLAR LWR, LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q064',  'S00B',   1, 'admin', SYSDATETIME()),
  ('85835-TD000ROG',   N'TRIM ASSY-CTR PILLAR LWR, LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q064',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85835-TD000YGU',   N'TRIM ASSY-CTR PILLAR LWR, LH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q064',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85845-TD000NNB',   N'TRIM ASSY-CTR PILLAR LWR, RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q065',  'S00B',   1, 'admin', SYSDATETIME()),
  ('85845-TD000ROG',   N'TRIM ASSY-CTR PILLAR LWR, RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q065',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85845-TD000YGU',   N'TRIM ASSY-CTR PILLAR LWR, RH',                 'ASSY', NULL, 'ME1A', 'EA', 0, 'Q065',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85855-TD000ROG',   N'TRIM ASSY-GATE PILLAR, LH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q068',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85855-TD000YGU',   N'TRIM ASSY-GATE PILLAR, LH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q068',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85865-TD000ROG',   N'TRIM ASSY-GATE PILLAR, RH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q070',  'S00D',   1, 'admin', SYSDATETIME()),
  ('85865-TD000YGU',   N'TRIM ASSY-GATE PILLAR, RH',                    'ASSY', NULL, 'ME1A', 'EA', 0, 'Q070',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('C2311-TD000',        N'CORE-PNL ASSY-FR DR TRIM UPR, LH (NON DS',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M087',   1, 'admin', SYSDATETIME()),
  ('C2321-TD000',        N'CORE-PNL ASSY-FR DR TRIM UPR, RH (NON DS',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M087',   1, 'admin', SYSDATETIME()),
  ('C2330-TD100',        N'CORE-PNL ASSY-FR DR CTR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M090',   1, 'admin', SYSDATETIME()),
  ('C2340-TD100',        N'CORE-PNL ASSY-FR DR CTR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M090',   1, 'admin', SYSDATETIME()),
  ('C3311-TD000',        N'CORE-PNL ASSY-RR DR TRIM UPR, LH (NON CU',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M088',   1, 'admin', SYSDATETIME()),
  ('C3311-TD100',        N'CORE-PNL ASSY-RR DR TRIM UPR, LH (+CURTA',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M089',   1, 'admin', SYSDATETIME()),
  ('C3321-TD000',        N'CORE-PNL ASSY-RR DR TRIM UPR, RH (NON CU',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M088',   1, 'admin', SYSDATETIME()),
  ('C3321-TD100',        N'CORE-PNL ASSY-RR DR TRIM UPR, RH (+CURTA',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M089',   1, 'admin', SYSDATETIME()),
  ('C3330-TD100',        N'CORE-PNL ASSY-RR DR CTR, LH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M091',   1, 'admin', SYSDATETIME()),
  ('C3340-TD100',        N'CORE-PNL ASSY-RR DR CTR, RH',                  'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M091',   1, 'admin', SYSDATETIME()),
  ('M1101-TD100NNB',     N'MODULE ASSY-FR UPR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M013',   1, 'admin', SYSDATETIME()),
  ('M1101-TD100YGN',     N'MODULE ASSY-FR UPR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M014',   1, 'admin', SYSDATETIME()),
  ('M1101-TD200NNB',     N'MODULE ASSY- FR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M019',   1, 'admin', SYSDATETIME()),
  ('M1101-TD200VKE',     N'MODULE ASSY- FR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M020',   1, 'admin', SYSDATETIME()),
  ('M1101-TD200YGN',     N'MODULE ASSY- FR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M021',   1, 'admin', SYSDATETIME()),
  ('M1101-TD300NNB',     N'MODULE ASSY- FR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M022',   1, 'admin', SYSDATETIME()),
  ('M1101-TD300VKE',     N'MODULE ASSY- FR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M023',   1, 'admin', SYSDATETIME()),
  ('M1101-TD300YGN',     N'MODULE ASSY- FR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M024',   1, 'admin', SYSDATETIME()),
  ('M1101-TD400NNB',     N'MODULE ASSY- FR UPR TRIM NO.4, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M015',   1, 'admin', SYSDATETIME()),
  ('M1101-TD400PNY',     N'MODULE ASSY- FR UPR TRIM NO.4, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M016',   1, 'admin', SYSDATETIME()),
  ('M1101-TD400VKE',     N'MODULE ASSY- FR UPR TRIM NO.4, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M017',   1, 'admin', SYSDATETIME()),
  ('M1101-TD400YGN',     N'MODULE ASSY- FR UPR TRIM NO.4, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M018',   1, 'admin', SYSDATETIME()),
  ('M1101-TD5004NB',     N'MODULE ASSY- FR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX06',   1, 'admin', SYSDATETIME()),
  ('M1101-TD500NNB',     N'MODULE ASSY- FR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M175',   1, 'admin', SYSDATETIME()),
  ('M1101-TD500PNY',     N'MODULE ASSY- FR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M176',   1, 'admin', SYSDATETIME()),
  ('M1101-TD500VKE',     N'MODULE ASSY- FR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M177',   1, 'admin', SYSDATETIME()),
  ('M1101-TD500YGN',     N'MODULE ASSY- FR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M178',   1, 'admin', SYSDATETIME()),
  ('M1102-TD100NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M052',   1, 'admin', SYSDATETIME()),
  ('M1102-TD100VKE',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M058',   1, 'admin', SYSDATETIME()),
  ('M1102-TD100YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M053',   1, 'admin', SYSDATETIME()),
  ('M1102-TD100YGU',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M200',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200NNB',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M054',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200PNY',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M055',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200ROG',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M201',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200VKE',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M056',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200YGN',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M057',   1, 'admin', SYSDATETIME()),
  ('M1102-TD200YGU',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M202',   1, 'admin', SYSDATETIME()),
  ('M1102-TD3004NB',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX02',   1, 'admin', SYSDATETIME()),
  ('M1102-TD300NNB',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M203',   1, 'admin', SYSDATETIME()),
  ('M1102-TD300ROG',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M204',   1, 'admin', SYSDATETIME()),
  ('M1102-TD300YGU',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M205',   1, 'admin', SYSDATETIME()),
  ('M1201-TD100NNB',     N'MODULE ASSY- FR UPR TRIM NO.1, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M025',   1, 'admin', SYSDATETIME()),
  ('M1201-TD100YGN',     N'MODULE ASSY- FR UPR TRIM NO.1, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M026',   1, 'admin', SYSDATETIME()),
  ('M1201-TD200NNB',     N'MODULE ASSY- FR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M027',   1, 'admin', SYSDATETIME()),
  ('M1201-TD200VKE',     N'MODULE ASSY- FR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M028',   1, 'admin', SYSDATETIME()),
  ('M1201-TD200YGN',     N'MODULE ASSY- FR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M029',   1, 'admin', SYSDATETIME()),
  ('M1201-TD3004NB',     N'MODULE ASSY- FR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX07',   1, 'admin', SYSDATETIME()),
  ('M1201-TD300NNB',     N'MODULE ASSY- FR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M030',   1, 'admin', SYSDATETIME()),
  ('M1201-TD300PNY',     N'MODULE ASSY- FR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M031',   1, 'admin', SYSDATETIME()),
  ('M1201-TD300VKE',     N'MODULE ASSY- FR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M032',   1, 'admin', SYSDATETIME()),
  ('M1201-TD300YGN',     N'MODULE ASSY- FR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M033',   1, 'admin', SYSDATETIME()),
  ('M1202-TD100NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M059',   1, 'admin', SYSDATETIME()),
  ('M1202-TD100VKE',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M061',   1, 'admin', SYSDATETIME()),
  ('M1202-TD100YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M060',   1, 'admin', SYSDATETIME()),
  ('M1202-TD100YGU',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M206',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200NNB',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M062',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200PNY',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M179',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200ROG',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M207',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200VKE',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M063',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200YGN',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M064',   1, 'admin', SYSDATETIME()),
  ('M1202-TD200YGU',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M208',   1, 'admin', SYSDATETIME()),
  ('M1202-TD3004NB',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX03',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300NNB',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M065',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300PNY',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M066',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300ROG',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M209',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300VKE',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M067',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300YGN',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M068',   1, 'admin', SYSDATETIME()),
  ('M1202-TD300YGU',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M210',   1, 'admin', SYSDATETIME()),
  ('M1301-TD100NNB',     N'MODULE ASSY- RR UPR TRIM NO.1, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M034',   1, 'admin', SYSDATETIME()),
  ('M1301-TD100YGN',     N'MODULE ASSY- RR UPR TRIM NO.1, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M035',   1, 'admin', SYSDATETIME()),
  ('M1301-TD200NNB',     N'MODULE ASSY- RR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M040',   1, 'admin', SYSDATETIME()),
  ('M1301-TD200VKE',     N'MODULE ASSY- RR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M041',   1, 'admin', SYSDATETIME()),
  ('M1301-TD200YGN',     N'MODULE ASSY- RR UPR TRIM NO.2, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M042',   1, 'admin', SYSDATETIME()),
  ('M1301-TD300NNB',     N'MODULE ASSY- RR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M036',   1, 'admin', SYSDATETIME()),
  ('M1301-TD300PNY',     N'MODULE ASSY- RR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M037',   1, 'admin', SYSDATETIME()),
  ('M1301-TD300VKE',     N'MODULE ASSY- RR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M038',   1, 'admin', SYSDATETIME()),
  ('M1301-TD300YGN',     N'MODULE ASSY- RR UPR TRIM NO.3, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M039',   1, 'admin', SYSDATETIME()),
  ('M1301-TD5004NB',     N'MODULE ASSY- RR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX08',   1, 'admin', SYSDATETIME()),
  ('M1301-TD500NNB',     N'MODULE ASSY- RR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M167',   1, 'admin', SYSDATETIME()),
  ('M1301-TD500PNY',     N'MODULE ASSY- RR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M168',   1, 'admin', SYSDATETIME()),
  ('M1301-TD500VKE',     N'MODULE ASSY- RR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M169',   1, 'admin', SYSDATETIME()),
  ('M1301-TD500YGN',     N'MODULE ASSY- RR UPR TRIM NO.5, LH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M170',   1, 'admin', SYSDATETIME()),
  ('M1302-TD100NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M069',   1, 'admin', SYSDATETIME()),
  ('M1302-TD100VKE',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M075',   1, 'admin', SYSDATETIME()),
  ('M1302-TD100YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M070',   1, 'admin', SYSDATETIME()),
  ('M1302-TD100YGU',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M211',   1, 'admin', SYSDATETIME()),
  ('M1302-TD2004NB',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX04',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200NNB',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M071',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200PNY',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M072',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200ROG',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M212',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200VKE',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M073',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200YGN',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M074',   1, 'admin', SYSDATETIME()),
  ('M1302-TD200YGU',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M213',   1, 'admin', SYSDATETIME()),
  ('M1302-TD3004NB',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'MXX1',   1, 'admin', SYSDATETIME()),
  ('M1302-TD300NNB',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M1302-TD300PNY',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M1302-TD300YGU',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M1401-TD100NNB',     N'MODULE ASSY- RR UPR TRIM NO.1, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M043',   1, 'admin', SYSDATETIME()),
  ('M1401-TD100YGN',     N'MODULE ASSY- RR UPR TRIM NO.1, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M044',   1, 'admin', SYSDATETIME()),
  ('M1401-TD200NNB',     N'MODULE ASSY- RR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M049',   1, 'admin', SYSDATETIME()),
  ('M1401-TD200VKE',     N'MODULE ASSY- RR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M050',   1, 'admin', SYSDATETIME()),
  ('M1401-TD200YGN',     N'MODULE ASSY- RR UPR TRIM NO.2, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M051',   1, 'admin', SYSDATETIME()),
  ('M1401-TD300NNB',     N'MODULE ASSY- RR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M045',   1, 'admin', SYSDATETIME()),
  ('M1401-TD300PNY',     N'MODULE ASSY- RR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M046',   1, 'admin', SYSDATETIME()),
  ('M1401-TD300VKE',     N'MODULE ASSY- RR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M047',   1, 'admin', SYSDATETIME()),
  ('M1401-TD300YGN',     N'MODULE ASSY- RR UPR TRIM NO.3, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M048',   1, 'admin', SYSDATETIME()),
  ('M1401-TD5004NB',     N'MODULE ASSY- RR UPR TRIM NO.5, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX09',   1, 'admin', SYSDATETIME()),
  ('M1401-TD500NNB',     N'MODULE ASSY- RR UPR TRIM NO.5, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M171',   1, 'admin', SYSDATETIME()),
  ('M1401-TD500PNY',     N'MODULE ASSY- RR UPR TRIM NO.5, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M172',   1, 'admin', SYSDATETIME()),
  ('M1401-TD500VKE',     N'MODULE ASSY- RR UPR TRIM NO.5, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M173',   1, 'admin', SYSDATETIME()),
  ('M1401-TD500YGN',     N'MODULE ASSY- RR UPR TRIM NO.5, RH',            'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M174',   1, 'admin', SYSDATETIME()),
  ('M1402-TD100NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M076',   1, 'admin', SYSDATETIME()),
  ('M1402-TD100VKE',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M082',   1, 'admin', SYSDATETIME()),
  ('M1402-TD100YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M077',   1, 'admin', SYSDATETIME()),
  ('M1402-TD100YGU',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M214',   1, 'admin', SYSDATETIME()),
  ('M1402-TD2004NB',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'HMGM',  'XX05',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200NNB',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M078',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200PNY',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M079',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200ROG',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M215',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200VKE',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M080',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200YGN',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M081',   1, 'admin', SYSDATETIME()),
  ('M1402-TD200YGU',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M216',   1, 'admin', SYSDATETIME()),
  ('M1402-TD3004NB',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'MXX5',   1, 'admin', SYSDATETIME()),
  ('M1402-TD300NNB',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M1402-TD300PNY',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M1402-TD300YGU',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('M2311-TD000NNB',     N'MODULE ASSY-FR DR TRIM UPR, LH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M142',   1, 'admin', SYSDATETIME()),
  ('M2311-TD000PNY',     N'MODULE ASSY-FR DR TRIM UPR, LH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M144',   1, 'admin', SYSDATETIME()),
  ('M2311-TD000VKE',     N'MODULE ASSY-FR DR TRIM UPR, LH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M145',   1, 'admin', SYSDATETIME()),
  ('M2311-TD000YGN',     N'MODULE ASSY-FR DR TRIM UPR, LH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M143',   1, 'admin', SYSDATETIME()),
  ('M2321-TD000NNB',     N'MODULE ASSY-FR DR TRIM UPR, RH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M146',   1, 'admin', SYSDATETIME()),
  ('M2321-TD000PNY',     N'MODULE ASSY-FR DR TRIM UPR, RH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M149',   1, 'admin', SYSDATETIME()),
  ('M2321-TD000VKE',     N'MODULE ASSY-FR DR TRIM UPR, RH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M148',   1, 'admin', SYSDATETIME()),
  ('M2321-TD000YGN',     N'MODULE ASSY-FR DR TRIM UPR, RH (NON DSM)',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M147',   1, 'admin', SYSDATETIME()),
  ('M3311-TD000NNB',     N'MODULE ASSY-RR DR TRIM UPR, LH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M150',   1, 'admin', SYSDATETIME()),
  ('M3311-TD000PNY',     N'MODULE ASSY-RR DR TRIM UPR, LH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M153',   1, 'admin', SYSDATETIME()),
  ('M3311-TD000VKE',     N'MODULE ASSY-RR DR TRIM UPR, LH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M155',   1, 'admin', SYSDATETIME()),
  ('M3311-TD000YGN',     N'MODULE ASSY-RR DR TRIM UPR, LH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M151',   1, 'admin', SYSDATETIME()),
  ('M3311-TD100NNB',     N'MODULE ASSY-RR DR TRIM UPR, LH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M152',   1, 'admin', SYSDATETIME()),
  ('M3311-TD100PNY',     N'MODULE ASSY-RR DR TRIM UPR, LH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M154',   1, 'admin', SYSDATETIME()),
  ('M3311-TD100VKE',     N'MODULE ASSY-RR DR TRIM UPR, LH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M156',   1, 'admin', SYSDATETIME()),
  ('M3311-TD100YGN',     N'MODULE ASSY-RR DR TRIM UPR, LH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M157',   1, 'admin', SYSDATETIME()),
  ('M3321-TD000NNB',     N'MODULE ASSY-RR DR TRIM UPR, RH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M158',   1, 'admin', SYSDATETIME()),
  ('M3321-TD000PNY',     N'MODULE ASSY-RR DR TRIM UPR, RH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M161',   1, 'admin', SYSDATETIME()),
  ('M3321-TD000VKE',     N'MODULE ASSY-RR DR TRIM UPR, RH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M163',   1, 'admin', SYSDATETIME()),
  ('M3321-TD000YGN',     N'MODULE ASSY-RR DR TRIM UPR, RH (NON CURT',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M159',   1, 'admin', SYSDATETIME()),
  ('M3321-TD100NNB',     N'MODULE ASSY-RR DR TRIM UPR, RH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M160',   1, 'admin', SYSDATETIME()),
  ('M3321-TD100PNY',     N'MODULE ASSY-RR DR TRIM UPR, RH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M162',   1, 'admin', SYSDATETIME()),
  ('M3321-TD100VKE',     N'MODULE ASSY-RR DR TRIM UPR, RH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M164',   1, 'admin', SYSDATETIME()),
  ('M3321-TD100YGN',     N'MODULE ASSY-RR DR TRIM UPR, RH (+CURTAIN',     'SUB',  NULL, 'ME1A', 'EA', 0, 'QSUB',  'M165',   1, 'admin', SYSDATETIME()),
  ('M85321-TD000ROG',    N'BOARD ASSY-HEADLINING (TRICOT, STD)',          'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X021',   1, 'admin', SYSDATETIME()),
  ('M85321-TD000YGU',    N'BOARD ASSY-HEADLINING (TRICOT, STD)',          'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X022',   1, 'admin', SYSDATETIME()),
  ('M85321-TD100NNB',    N'BOARD ASSY-HEADLINING (SUEDE, STD)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X010',   1, 'admin', SYSDATETIME()),
  ('M85321-TD100ROG',    N'BOARD ASSY-HEADLINING (SUEDE, STD)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X011',   1, 'admin', SYSDATETIME()),
  ('M85321-TD100YGU',    N'BOARD ASSY-HEADLINING (SUEDE, STD)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X012',   1, 'admin', SYSDATETIME()),
  ('M85321-TD200ROG',    N'BOARD ASSY-HEADLINING (TRICOT+MIC, STD)',      'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X025',   1, 'admin', SYSDATETIME()),
  ('M85321-TD200YGU',    N'BOARD ASSY-HEADLINING (TRICOT+MIC, STD)',      'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X026',   1, 'admin', SYSDATETIME()),
  ('M85321-TD300NNB',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, STD)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X016',   1, 'admin', SYSDATETIME()),
  ('M85321-TD300ROG',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, STD)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X017',   1, 'admin', SYSDATETIME()),
  ('M85321-TD300YGU',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, STD)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X018',   1, 'admin', SYSDATETIME()),
  ('M85421-TD000ROG',    N'BOARD ASSY-HEADLINING (TRICOT, SRF)',          'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X019',   1, 'admin', SYSDATETIME()),
  ('M85421-TD000YGU',    N'BOARD ASSY-HEADLINING (TRICOT, SRF)',          'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X020',   1, 'admin', SYSDATETIME()),
  ('M85421-TD100NNB',    N'BOARD ASSY-HEADLINING (SUEDE, SRF)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X007',   1, 'admin', SYSDATETIME()),
  ('M85421-TD100ROG',    N'BOARD ASSY-HEADLINING (SUEDE, SRF)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X008',   1, 'admin', SYSDATETIME()),
  ('M85421-TD100YGU',    N'BOARD ASSY-HEADLINING (SUEDE, SRF)',           'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X009',   1, 'admin', SYSDATETIME()),
  ('M85421-TD200ROG',    N'BOARD ASSY-HEADLINING (TRICOT+MIC, SRF)',      'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X023',   1, 'admin', SYSDATETIME()),
  ('M85421-TD200YGU',    N'BOARD ASSY-HEADLINING (TRICOT+MIC, SRF)',      'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X024',   1, 'admin', SYSDATETIME()),
  ('M85421-TD300NNB',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, SRF)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X013',   1, 'admin', SYSDATETIME()),
  ('M85421-TD300ROG',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, SRF)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X014',   1, 'admin', SYSDATETIME()),
  ('M85421-TD300YGU',    N'BOARD ASSY-HEADLINING (SUEDE+MIC, SRF)',       'SUB',  NULL, 'ME1A', 'EA', 0, 'QXXX',  'X015',   1, 'admin', SYSDATETIME()),
  ('Z2025-TDDT10',       N'COMPL ASSY-FR DR TRIM, LH',                    'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDDT20',       N'COMPL ASSY-FR DR TRIM, RH',                    'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDDT30',       N'COMPL ASSY-RR DR TRIM, LH',                    'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDDT40',       N'COMPL ASSY-RR DR TRIM, RH',                    'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDHL10',       N'COMPLETE ASSY- H/LIN''G',                      'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDHL101',      N'SUB PART- H/LIN''G',                           'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDLS10',       N'TRIM ASSY-LUGGAGE SIDE, LH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDLS101',      N'SUB PART-LUGGAGE SIDE, LH',                    'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDLS20',       N'TRIM ASSY-LUGGAGE SIDE, RH',                   'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDLS201',      N'SUB PART-LUGGAGE SIDE, RH',                    'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDTG10',       N'TRIM ASSY-TRIM ASSY-TAIL GATE LWR',            'ASSY', NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('Z2025-TDTG101',      N'SUB PART-TRIM ASSY-TAIL GATE LWR',             'SUB',  NULL, 'ME1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME());
GO

-- Items (NE1A part master — docs/PartMaster_NE1A.xls)
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, SafetyStock, PGN, ALC, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('81710-PI000NNB',     N'TRIM ASSY-TAIL GATE, LWR',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q017',  'N00B',   1, 'admin', SYSDATETIME()),
  ('81710-PI000YGN',     N'TRIM ASSY-TAIL GATE, LWR',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q017',  'N00G',   1, 'admin', SYSDATETIME()),
  ('81710-PI010NNB',     N'TRIM ASSY-TAIL GATE, LWR',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q017',  'N10B',   1, 'admin', SYSDATETIME()),
  ('81710-PI010YGN',     N'TRIM ASSY-TAIL GATE, LWR',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q017',  'N10G',   1, 'admin', SYSDATETIME()),
  ('81711-PI000NNB',     N'TRIM - TAIL GATE LWR',                      'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N001',   1, 'admin', SYSDATETIME()),
  ('81711-PI000YGN',     N'TRIM - TAIL GATE LWR',                      'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N002',   1, 'admin', SYSDATETIME()),
  ('82301-PI000NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N00B',   1, 'admin', SYSDATETIME()),
  ('82301-PI000YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI010NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N10B',   1, 'admin', SYSDATETIME()),
  ('82301-PI010YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N10Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI020NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N20B',   1, 'admin', SYSDATETIME()),
  ('82301-PI020YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI030NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N30B',   1, 'admin', SYSDATETIME()),
  ('82301-PI030VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N30K',   1, 'admin', SYSDATETIME()),
  ('82301-PI030YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N30Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI040NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N40B',   1, 'admin', SYSDATETIME()),
  ('82301-PI040VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N40K',   1, 'admin', SYSDATETIME()),
  ('82301-PI040YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N40Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI050NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N50B',   1, 'admin', SYSDATETIME()),
  ('82301-PI050VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N50K',   1, 'admin', SYSDATETIME()),
  ('82301-PI050XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('82301-PI050YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N50Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI060XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N60X',   1, 'admin', SYSDATETIME()),
  ('82301-PI070XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N70X',   1, 'admin', SYSDATETIME()),
  ('82301-PI080XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N80X',   1, 'admin', SYSDATETIME()),
  ('82301-PI090XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N90X',   1, 'admin', SYSDATETIME()),
  ('82301-PI300NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'NW0B',   1, 'admin', SYSDATETIME()),
  ('82301-PI300YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'NW0Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI310NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'NX0B',   1, 'admin', SYSDATETIME()),
  ('82301-PI310YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'NX0Y',   1, 'admin', SYSDATETIME()),
  ('82301-PI320NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('82301-PI350NNB',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N1AB',   1, 'admin', SYSDATETIME()),
  ('82301-PI350VKE',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N1AK',   1, 'admin', SYSDATETIME()),
  ('82301-PI350YGU',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N1AY',   1, 'admin', SYSDATETIME()),
  ('82301-PI360XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N2AX',   1, 'admin', SYSDATETIME()),
  ('82301-PI370XE8',     N'PNL ASSY-FR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'N3AX',   1, 'admin', SYSDATETIME()),
  ('82302-PI000NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N00B',   1, 'admin', SYSDATETIME()),
  ('82302-PI000YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI010NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N10B',   1, 'admin', SYSDATETIME()),
  ('82302-PI010YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N10Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI020NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N20B',   1, 'admin', SYSDATETIME()),
  ('82302-PI020VKE',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N20K',   1, 'admin', SYSDATETIME()),
  ('82302-PI020YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI030XE8',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'N30X',   1, 'admin', SYSDATETIME()),
  ('82302-PI300NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NW0B',   1, 'admin', SYSDATETIME()),
  ('82302-PI300YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NW0Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI310NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NX0B',   1, 'admin', SYSDATETIME()),
  ('82302-PI310YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NX0Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI320NNB',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NY0B',   1, 'admin', SYSDATETIME()),
  ('82302-PI320VKE',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NY0K',   1, 'admin', SYSDATETIME()),
  ('82302-PI320YGU',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NY0Y',   1, 'admin', SYSDATETIME()),
  ('82302-PI330XE8',     N'PNL ASSY-FR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'NZ0X',   1, 'admin', SYSDATETIME()),
  ('82305-PI000NNB',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00B',  1, 'admin', SYSDATETIME()),
  ('82305-PI000YGU',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00Y',  1, 'admin', SYSDATETIME()),
  ('82305-PI020NNB',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN50B',  1, 'admin', SYSDATETIME()),
  ('82305-PI020VKE',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN50K',  1, 'admin', SYSDATETIME()),
  ('82305-PI020YGU',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN50Y',  1, 'admin', SYSDATETIME()),
  ('82305-PI030XE8',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN70X',  1, 'admin', SYSDATETIME()),
  ('82305-PI300NNB',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PNW0B',  1, 'admin', SYSDATETIME()),
  ('82305-PI300YGU',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PNW0Y',  1, 'admin', SYSDATETIME()),
  ('82305-PI320NNB',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PN1AB',  1, 'admin', SYSDATETIME()),
  ('82305-PI320VKE',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PN1AK',  1, 'admin', SYSDATETIME()),
  ('82305-PI320YGU',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PN1AY',  1, 'admin', SYSDATETIME()),
  ('82305-PI330XE8',     N'PNL ASSY-FR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q023',  'PN2AX',  1, 'admin', SYSDATETIME()),
  ('82306-PI000NNB',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00B',  1, 'admin', SYSDATETIME()),
  ('82306-PI000YGU',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00Y',  1, 'admin', SYSDATETIME()),
  ('82306-PI010NNB',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN20B',  1, 'admin', SYSDATETIME()),
  ('82306-PI010VKE',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN20Y',  1, 'admin', SYSDATETIME()),
  ('82306-PI010YGU',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN30B',  1, 'admin', SYSDATETIME()),
  ('82306-PI020XE8',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN30K',  1, 'admin', SYSDATETIME()),
  ('82306-PI300NNB',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNW0B',  1, 'admin', SYSDATETIME()),
  ('82306-PI300YGU',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNW0Y',  1, 'admin', SYSDATETIME()),
  ('82306-PI310NNB',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNY0B',  1, 'admin', SYSDATETIME()),
  ('82306-PI310VKE',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNY0K',  1, 'admin', SYSDATETIME()),
  ('82306-PI310YGU',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNY0Y',  1, 'admin', SYSDATETIME()),
  ('82306-PI320XE8',     N'PNL ASSY-FR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q024',  'PNZ0X',  1, 'admin', SYSDATETIME()),
  ('82310-PI010NNB',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N003',   1, 'admin', SYSDATETIME()),
  ('82310-PI010VKE',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N004',   1, 'admin', SYSDATETIME()),
  ('82310-PI010YGU',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N005',   1, 'admin', SYSDATETIME()),
  ('82310-PIP00MEE',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N147',   1, 'admin', SYSDATETIME()),
  ('82310-PIP00NNB',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N148',   1, 'admin', SYSDATETIME()),
  ('82310-PIP00YGU',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N149',   1, 'admin', SYSDATETIME()),
  ('82320-PI010NNB',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N006',   1, 'admin', SYSDATETIME()),
  ('82320-PI010VKE',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N007',   1, 'admin', SYSDATETIME()),
  ('82320-PI010YGU',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N008',   1, 'admin', SYSDATETIME()),
  ('82320-PIP00MEE',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N150',   1, 'admin', SYSDATETIME()),
  ('82320-PIP00NNB',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N151',   1, 'admin', SYSDATETIME()),
  ('82320-PIP00YGU',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N152',   1, 'admin', SYSDATETIME()),
  ('82350-PI000NNB',     N'PNL-FR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N009',   1, 'admin', SYSDATETIME()),
  ('82350-PI000YGN',     N'PNL-FR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N010',   1, 'admin', SYSDATETIME()),
  ('82350-PI000YGU',     N'PNL-FR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N011',   1, 'admin', SYSDATETIME()),
  ('82360-PI000NNB',     N'PNL-FR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N009',   1, 'admin', SYSDATETIME()),
  ('82360-PI000YGN',     N'PNL-FR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N010',   1, 'admin', SYSDATETIME()),
  ('82360-PI000YGU',     N'PNL-FR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N011',   1, 'admin', SYSDATETIME()),
  ('82731-PI010NNB',     N'HDL-FR DR PULL INR, LH(MOOD LAMP)',         'MATERIAL', NULL, 'NE1A', 'EA', 0, 'Q021',  'Q000',   1, 'admin', SYSDATETIME()),
  ('83301-PI000NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N00B',   1, 'admin', SYSDATETIME()),
  ('83301-PI000YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI010NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N10B',   1, 'admin', SYSDATETIME()),
  ('83301-PI010YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N10Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI020NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N20B',   1, 'admin', SYSDATETIME()),
  ('83301-PI020VKE',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N20K',   1, 'admin', SYSDATETIME()),
  ('83301-PI020YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI030NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N30B',   1, 'admin', SYSDATETIME()),
  ('83301-PI030VKE',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N30K',   1, 'admin', SYSDATETIME()),
  ('83301-PI030YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N30Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI040NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N40B',   1, 'admin', SYSDATETIME()),
  ('83301-PI040VKE',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N40K',   1, 'admin', SYSDATETIME()),
  ('83301-PI040YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N40Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI050XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N50X',   1, 'admin', SYSDATETIME()),
  ('83301-PI060XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N60X',   1, 'admin', SYSDATETIME()),
  ('83301-PI070XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N70X',   1, 'admin', SYSDATETIME()),
  ('83301-PI080XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N80X',   1, 'admin', SYSDATETIME()),
  ('83301-PI300NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'NW0B',   1, 'admin', SYSDATETIME()),
  ('83301-PI300YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'NW0Y',   1, 'admin', SYSDATETIME()),
  ('83301-PI340NNB',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N0AB',   1, 'admin', SYSDATETIME()),
  ('83301-PI340VKE',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N0AK',   1, 'admin', SYSDATETIME()),
  ('83301-PI340YGU',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N0AY',   1, 'admin', SYSDATETIME()),
  ('83301-PI350XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N1AX',   1, 'admin', SYSDATETIME()),
  ('83301-PI360XE8',     N'PNL ASSY-RR DR TRIM COMPL,LH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'N2AX',   1, 'admin', SYSDATETIME()),
  ('83302-PI000NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N00B',   1, 'admin', SYSDATETIME()),
  ('83302-PI000YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI010NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N10B',   1, 'admin', SYSDATETIME()),
  ('83302-PI010YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N10Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI020NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N20B',   1, 'admin', SYSDATETIME()),
  ('83302-PI020VKE',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N20K',   1, 'admin', SYSDATETIME()),
  ('83302-PI020YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI030NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N30B',   1, 'admin', SYSDATETIME()),
  ('83302-PI030VKE',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N30K',   1, 'admin', SYSDATETIME()),
  ('83302-PI030YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N30Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI040NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N40B',   1, 'admin', SYSDATETIME()),
  ('83302-PI040VKE',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N40K',   1, 'admin', SYSDATETIME()),
  ('83302-PI040YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N40Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI050XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N50X',   1, 'admin', SYSDATETIME()),
  ('83302-PI060XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N60X',   1, 'admin', SYSDATETIME()),
  ('83302-PI070XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N70X',   1, 'admin', SYSDATETIME()),
  ('83302-PI080XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N80X',   1, 'admin', SYSDATETIME()),
  ('83302-PI300NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'NW0B',   1, 'admin', SYSDATETIME()),
  ('83302-PI300YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'NW0Y',   1, 'admin', SYSDATETIME()),
  ('83302-PI340NNB',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N0AB',   1, 'admin', SYSDATETIME()),
  ('83302-PI340VKE',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N0AK',   1, 'admin', SYSDATETIME()),
  ('83302-PI340YGU',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N0AY',   1, 'admin', SYSDATETIME()),
  ('83302-PI350XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N1AX',   1, 'admin', SYSDATETIME()),
  ('83302-PI360XE8',     N'PNL ASSY-RR DR TRIM COMPL,RH',              'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'N2AX',   1, 'admin', SYSDATETIME()),
  ('83305-PI000NNB',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00B',  1, 'admin', SYSDATETIME()),
  ('83305-PI000YGU',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00Y',  1, 'admin', SYSDATETIME()),
  ('83305-PI010NNB',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN10B',  1, 'admin', SYSDATETIME()),
  ('83305-PI010YGU',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN10Y',  1, 'admin', SYSDATETIME()),
  ('83305-PI030NNB',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40B',  1, 'admin', SYSDATETIME()),
  ('83305-PI030VKE',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40K',  1, 'admin', SYSDATETIME()),
  ('83305-PI030YGU',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40Y',  1, 'admin', SYSDATETIME()),
  ('83305-PI040XE8',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN70X',  1, 'admin', SYSDATETIME()),
  ('83305-PI050XE8',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN60X',  1, 'admin', SYSDATETIME()),
  ('83305-PI300NNB',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'PNW0B',  1, 'admin', SYSDATETIME()),
  ('83305-PI300YGU',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'PNW0Y',  1, 'admin', SYSDATETIME()),
  ('83305-PI330NNB',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'PN0AB',  1, 'admin', SYSDATETIME()),
  ('83305-PI330VKE',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'PN0AK',  1, 'admin', SYSDATETIME()),
  ('83305-PI330YGU',     N'PNL ASSY-RR DR TRIM,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q033',  'PN0AY',  1, 'admin', SYSDATETIME()),
  ('83306-PI000NNB',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00B',  1, 'admin', SYSDATETIME()),
  ('83306-PI000YGU',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN00Y',  1, 'admin', SYSDATETIME()),
  ('83306-PI010NNB',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN10B',  1, 'admin', SYSDATETIME()),
  ('83306-PI010YGU',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN10Y',  1, 'admin', SYSDATETIME()),
  ('83306-PI030NNB',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40B',  1, 'admin', SYSDATETIME()),
  ('83306-PI030VKE',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40K',  1, 'admin', SYSDATETIME()),
  ('83306-PI030YGU',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN40Y',  1, 'admin', SYSDATETIME()),
  ('83306-PI040XE8',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN70X',  1, 'admin', SYSDATETIME()),
  ('83306-PI050XE8',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'QMTO',  'PN60X',  1, 'admin', SYSDATETIME()),
  ('83306-PI300NNB',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PNW0B',  1, 'admin', SYSDATETIME()),
  ('83306-PI300YGU',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PNW0Y',  1, 'admin', SYSDATETIME()),
  ('83306-PI330NNB',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PN0AB',  1, 'admin', SYSDATETIME()),
  ('83306-PI330VKE',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PN0AK',  1, 'admin', SYSDATETIME()),
  ('83306-PI330YGU',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PN0AY',  1, 'admin', SYSDATETIME()),
  ('83306-PI340XE8',     N'PNL ASSY-RR DR TRIM,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q034',  'PN1AX',  1, 'admin', SYSDATETIME()),
  ('83310-PI010NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N012',   1, 'admin', SYSDATETIME()),
  ('83310-PI010VKE',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N013',   1, 'admin', SYSDATETIME()),
  ('83310-PI010YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N014',   1, 'admin', SYSDATETIME()),
  ('83310-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N015',   1, 'admin', SYSDATETIME()),
  ('83310-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N016',   1, 'admin', SYSDATETIME()),
  ('83310-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N017',   1, 'admin', SYSDATETIME()),
  ('83310-PI021VKE',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS13',   1, 'admin', SYSDATETIME()),
  ('83310-PIP00MEE',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N153',   1, 'admin', SYSDATETIME()),
  ('83310-PIP00NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N154',   1, 'admin', SYSDATETIME()),
  ('83310-PIP00YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N155',   1, 'admin', SYSDATETIME()),
  ('83320-PI010NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N012',   1, 'admin', SYSDATETIME()),
  ('83320-PI010VKE',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N013',   1, 'admin', SYSDATETIME()),
  ('83320-PI010YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N014',   1, 'admin', SYSDATETIME()),
  ('83320-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N015',   1, 'admin', SYSDATETIME()),
  ('83320-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N016',   1, 'admin', SYSDATETIME()),
  ('83320-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N017',   1, 'admin', SYSDATETIME()),
  ('83320-PI021NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS14',   1, 'admin', SYSDATETIME()),
  ('83320-PI021YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS15',   1, 'admin', SYSDATETIME()),
  ('83320-PIP00MEE',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N156',   1, 'admin', SYSDATETIME()),
  ('83320-PIP00NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N157',   1, 'admin', SYSDATETIME()),
  ('83320-PIP00YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N158',   1, 'admin', SYSDATETIME()),
  ('83350-PI000NNB',     N'PNL-RR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N024',   1, 'admin', SYSDATETIME()),
  ('83350-PI000YGN',     N'PNL-RR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N025',   1, 'admin', SYSDATETIME()),
  ('83350-PI000YGU',     N'PNL-RR DR MAIN TRIM, LH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N026',   1, 'admin', SYSDATETIME()),
  ('83360-PI000NNB',     N'PNL-RR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N024',   1, 'admin', SYSDATETIME()),
  ('83360-PI000YGN',     N'PNL-RR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N025',   1, 'admin', SYSDATETIME()),
  ('83360-PI000YGU',     N'PNL-RR DR MAIN TRIM, RH',                   'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N026',   1, 'admin', SYSDATETIME()),
  ('85300-PI000MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI000YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI010MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI010YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N10Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI020MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI020YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N20Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI030MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N30M',   1, 'admin', SYSDATETIME()),
  ('85300-PI030YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N30Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI040MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI040YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N40Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI050MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI050YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N50Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI060MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N60M',   1, 'admin', SYSDATETIME()),
  ('85300-PI060YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N60Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI070MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N70Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI070YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N70Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI080MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N80M',   1, 'admin', SYSDATETIME()),
  ('85300-PI080YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N80Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI090MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N90M',   1, 'admin', SYSDATETIME()),
  ('85300-PI090YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'N90Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI100MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI100YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI110MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'NB0M',   1, 'admin', SYSDATETIME()),
  ('85300-PI110YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI120MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'NC0M',   1, 'admin', SYSDATETIME()),
  ('85300-PI120YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI130MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI130YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85300-PI140YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'NE0Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI150YGU',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'NF0Y',   1, 'admin', SYSDATETIME()),
  ('85300-PI160MMN',     N'HEADLINING COMPLETE (STD)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'NG0M',   1, 'admin', SYSDATETIME()),
  ('85310-PI000MMN',     N'HEAD LINING ASSY (STD)',                    'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X029',   1, 'admin', SYSDATETIME()),
  ('85310-PI000YGU',     N'HEAD LINING ASSY (STD)',                    'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X030',   1, 'admin', SYSDATETIME()),
  ('85311-PI000',        N'BOARD-HEADLINING (STD)',                    'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N027',   1, 'admin', SYSDATETIME()),
  ('85311-PI000MMN',     N'BOARD ASSY-HEADLINING(STD)',                'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N123',   1, 'admin', SYSDATETIME()),
  ('85311-PI000YGU',     N'BOARD ASSY-HEADLINING(STD)',                'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N124',   1, 'admin', SYSDATETIME()),
  ('85311-PI10E',        N'PU BLOCK-BOARD-NE1A',                       'SUB',      NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85311-PI20E',        N'PU BLOCK-BOARD-NE1A (1540*2180*920)',       'SUB',      NULL, 'NE1A', 'EA', 0, NULL,    NULL,     1, 'admin', SYSDATETIME()),
  ('85400-PI000YGU',     N'HEADLINING COMPLETE (SRF)',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q053',  'S00Y',   1, 'admin', SYSDATETIME()),
  ('85410-PI000YGU',     N'HEAD LINING ASSY (SRF)',                    'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X028',   1, 'admin', SYSDATETIME()),
  ('85411-PI000',        N'BOARD-HEADLINING (SRF)',                    'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N028',   1, 'admin', SYSDATETIME()),
  ('85411-PI000YGU',     N'BOARD ASSY-HEADLINING(SRF)',                'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N125',   1, 'admin', SYSDATETIME()),
  ('85730-PI000NNB',     N'TRIM ASSY-LUGGAGE SIDE,LH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q114',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85730-PI000YGN',     N'TRIM ASSY-LUGGAGE SIDE,LH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q114',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85730-PI050NNB',     N'TRIM ASSY-LUGGAGE SIDE,LH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q114',  'N50B',   1, 'admin', SYSDATETIME()),
  ('85730-PI050YGN',     N'TRIM ASSY-LUGGAGE SIDE,LH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q114',  'N50G',   1, 'admin', SYSDATETIME()),
  ('85731-PI000NNB',     N'TRIM-LUGGAGE SIDE, LH',                     'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N029',   1, 'admin', SYSDATETIME()),
  ('85731-PI000YGN',     N'TRIM-LUGGAGE SIDE, LH',                     'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N030',   1, 'admin', SYSDATETIME()),
  ('85740-PI000NNB',     N'TRIM ASSY-LUGGAGE SIDE,RH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q112',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85740-PI000YGN',     N'TRIM ASSY-LUGGAGE SIDE,RH',                 'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q112',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85741-PI000NNB',     N'TRIM-LUGGAGE SIDE, RH',                     'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N031',   1, 'admin', SYSDATETIME()),
  ('85741-PI000YGN',     N'TRIM-LUGGAGE SIDE, RH',                     'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N032',   1, 'admin', SYSDATETIME()),
  ('85770-PI000NNB',   N'TRIM ASSY-RR TRANSVERSE',                   'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q113',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85770-PI000YGN',   N'TRIM ASSY-RR TRANSVERSE',                   'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q113',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85770-PI100NNB',   N'TRIM ASSY-RR TRANSVERSE',                   'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q113',  'NA0B',   1, 'admin', SYSDATETIME()),
  ('85770-PI100YGN',   N'TRIM ASSY-RR TRANSVERSE',                   'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q113',  'NA0G',   1, 'admin', SYSDATETIME()),
  ('85810-PI000NNB',   N'TRIM ASSY-FR PLR,LH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q058',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85810-PI000YGU',   N'TRIM ASSY-FR PLR,LH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q058',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85820-PI000NNB',   N'TRIM ASSY-FR PLR,RH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q059',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85820-PI000YGU',   N'TRIM ASSY-FR PLR,RH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q059',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85823-PI000NNB',   N'TRIM ASSY-COWL SIDE,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q060',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85823-PI000YGN',   N'TRIM ASSY-COWL SIDE,LH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q060',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85824-PI000NNB',   N'TRIM ASSY-COWL SIDE,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q061',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85824-PI000YGN',   N'TRIM ASSY-COWL SIDE,RH',                    'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q061',  'N00G',   1, 'admin', SYSDATETIME()),
  ('85830-PI000NNB',   N'TRIM ASSY-CTR PLR UPR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q062',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85830-PI000YGU',   N'TRIM ASSY-CTR PLR UPR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q062',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85835-PI000NNB',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85835-PI000YGU',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85835-PI100MEE',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'NA0E',   1, 'admin', SYSDATETIME()),
  ('85835-PI100NNB',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'NA0B',   1, 'admin', SYSDATETIME()),
  ('85835-PI100VKE',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'NA0K',   1, 'admin', SYSDATETIME()),
  ('85835-PI100YGU',   N'TRIM ASSY-CTR PLR LWR,LH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q064',  'NA0Y',   1, 'admin', SYSDATETIME()),
  ('85836-PI000NNB',     N'GARNISH-CTR PLR LWR-UPR PIECE, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN200',  1, 'admin', SYSDATETIME()),
  ('85836-PI000VKE',     N'GARNISH-CTR PLR LWR-UPR PIECE, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN202',  1, 'admin', SYSDATETIME()),
  ('85836-PI000YGU',     N'GARNISH-CTR PLR LWR-UPR PIECE, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN204',  1, 'admin', SYSDATETIME()),
  ('85837-PI000NNB',     N'GARNISH-CTR PLR LWR-LWR PIECE, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN201',  1, 'admin', SYSDATETIME()),
  ('85837-PI000YGN',     N'GARNISH-CTR PLR LWR-LWR PIECE, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN203',  1, 'admin', SYSDATETIME()),
  ('85840-PI000NNB',   N'TRIM ASSY-CTR PLR UPR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q063',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85840-PI000YGU',   N'TRIM ASSY-CTR PLR UPR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q063',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85845-PI000NNB',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85845-PI000YGU',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85845-PI100MEE',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'NA0E',   1, 'admin', SYSDATETIME()),
  ('85845-PI100NNB',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'NA0B',   1, 'admin', SYSDATETIME()),
  ('85845-PI100VKE',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'NA0K',   1, 'admin', SYSDATETIME()),
  ('85845-PI100YGU',   N'TRIM ASSY-CTR PLR LWR,RH',                  'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q065',  'NA0Y',   1, 'admin', SYSDATETIME()),
  ('85846-PI000NNB',     N'GARNISH-CTR PLR LWR-UPR PIECE, RH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN214',  1, 'admin', SYSDATETIME()),
  ('85846-PI000VKE',     N'GARNISH-CTR PLR LWR-UPR PIECE, RH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN216',  1, 'admin', SYSDATETIME()),
  ('85846-PI000YGU',     N'GARNISH-CTR PLR LWR-UPR PIECE, RH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN218',  1, 'admin', SYSDATETIME()),
  ('85847-PI000NNB',     N'GARNISH-CTR PLR LWR-LWR PIECE, RH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN215',  1, 'admin', SYSDATETIME()),
  ('85847-PI000YGN',     N'GARNISH-CTR PLR LWR-LWR PIECE, RH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN217',  1, 'admin', SYSDATETIME()),
  ('85850-PI000NNB',     N'TRIM ASSY-RR PLR,LH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q066',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85850-PI000YGU',     N'TRIM ASSY-RR PLR,LH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q066',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85851-PI000NNB',     N'TRIM-RR PLR, LH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N033',   1, 'admin', SYSDATETIME()),
  ('85851-PI000YGU',     N'TRIM-RR PLR, LH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N034',   1, 'admin', SYSDATETIME()),
  ('85860-PI000NNB',     N'TRIM ASSY-RR PLR,RH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q067',  'N00B',   1, 'admin', SYSDATETIME()),
  ('85860-PI000YGU',     N'TRIM ASSY-RR PLR,RH',                       'ASSY',     NULL, 'NE1A', 'EA', 0, 'Q067',  'N00Y',   1, 'admin', SYSDATETIME()),
  ('85861-PI000NNB',     N'TRIM-RR PLR, RH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N033',   1, 'admin', SYSDATETIME()),
  ('85861-PI000YGU',     N'TRIM-RR PLR, RH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N034',   1, 'admin', SYSDATETIME()),
  ('918A0-PI230AA',      N'WIRING HARNESS-ROOF',                       'MATERIAL', NULL, 'NE1A', 'EA', 0, 'Q018',  'Q230',   1, 'admin', SYSDATETIME()),
  ('D0111-PI010',        N'FRT CORE,LH',                               'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N035',   1, 'admin', SYSDATETIME()),
  ('D0121-PI010',        N'FRT CORE,RH',                               'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N035',   1, 'admin', SYSDATETIME()),
  ('D0131-PI010',        N'RR(STD) CORE,LH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N036',   1, 'admin', SYSDATETIME()),
  ('D0131-PI020',        N'RR(CURTAIN) CORE,LH',                       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N037',   1, 'admin', SYSDATETIME()),
  ('D0141-PI010',        N'RR(STD) CORE,RH',                           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N036',   1, 'admin', SYSDATETIME()),
  ('D0141-PI020',        N'RR(CURTAIN) CORE,RH',                       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N037',   1, 'admin', SYSDATETIME()),
  ('M0210-PI000MEE',     N'MODULE ASSY-FR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N126',   1, 'admin', SYSDATETIME()),
  ('M0210-PI000NNB',     N'MODULE ASSY-FR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N038',   1, 'admin', SYSDATETIME()),
  ('M0210-PI000XE8',     N'MODULE ASSY-FR UPR TRIM NO.1,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N039',   1, 'admin', SYSDATETIME()),
  ('M0210-PI000YGU',     N'MODULE ASSY-FR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N040',   1, 'admin', SYSDATETIME()),
  ('M0210-PI010NNB',     N'MODULE ASSY-FR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N041',   1, 'admin', SYSDATETIME()),
  ('M0210-PI010VKE',     N'MODULE ASSY-FR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N042',   1, 'admin', SYSDATETIME()),
  ('M0210-PI010YGU',     N'MODULE ASSY-FR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N043',   1, 'admin', SYSDATETIME()),
  ('M0210-PI020MEE',     N'MODULE ASSY-FR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N159',   1, 'admin', SYSDATETIME()),
  ('M0210-PI020NNB',     N'MODULE ASSY-FR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N160',   1, 'admin', SYSDATETIME()),
  ('M0210-PI020YGU',     N'MODULE ASSY-FR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N161',   1, 'admin', SYSDATETIME()),
  ('M0210-PI030NNB',     N'MODULE ASSY-FR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N162',   1, 'admin', SYSDATETIME()),
  ('M0210-PI030VKE',     N'MODULE ASSY-FR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N163',   1, 'admin', SYSDATETIME()),
  ('M0210-PI030YGU',     N'MODULE ASSY-FR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N164',   1, 'admin', SYSDATETIME()),
  ('M0220-PI000MEE',     N'MODULE ASSY-FR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N127',   1, 'admin', SYSDATETIME()),
  ('M0220-PI000NNB',     N'MODULE ASSY-FR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N044',   1, 'admin', SYSDATETIME()),
  ('M0220-PI000XE8',     N'MODULE ASSY-FR UPR TRIM NO.1,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N045',   1, 'admin', SYSDATETIME()),
  ('M0220-PI000YGU',     N'MODULE ASSY-FR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N046',   1, 'admin', SYSDATETIME()),
  ('M0220-PI010NNB',     N'MODULE ASSY-FR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N047',   1, 'admin', SYSDATETIME()),
  ('M0220-PI010VKE',     N'MODULE ASSY-FR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N048',   1, 'admin', SYSDATETIME()),
  ('M0220-PI010YGU',     N'MODULE ASSY-FR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N049',   1, 'admin', SYSDATETIME()),
  ('M0220-PI020MEE',     N'MODULE ASSY-FR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N165',   1, 'admin', SYSDATETIME()),
  ('M0220-PI020NNB',     N'MODULE ASSY-FR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N166',   1, 'admin', SYSDATETIME()),
  ('M0220-PI020YGU',     N'MODULE ASSY-FR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N167',   1, 'admin', SYSDATETIME()),
  ('M0220-PI030NNB',     N'MODULE ASSY-FR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N168',   1, 'admin', SYSDATETIME()),
  ('M0220-PI030VKE',     N'MODULE ASSY-FR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N169',   1, 'admin', SYSDATETIME()),
  ('M0220-PI030YGU',     N'MODULE ASSY-FR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N170',   1, 'admin', SYSDATETIME()),
  ('M0230-PI000MEE',     N'MODULE ASSY-RR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N128',   1, 'admin', SYSDATETIME()),
  ('M0230-PI000NNB',     N'MODULE ASSY-RR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N050',   1, 'admin', SYSDATETIME()),
  ('M0230-PI000XE8',     N'MODULE ASSY-RR UPR TRIM NO.1,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N051',   1, 'admin', SYSDATETIME()),
  ('M0230-PI000YGU',     N'MODULE ASSY-RR UPR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N052',   1, 'admin', SYSDATETIME()),
  ('M0230-PI010NNB',     N'MODULE ASSY-RR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N053',   1, 'admin', SYSDATETIME()),
  ('M0230-PI010VKE',     N'MODULE ASSY-RR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N054',   1, 'admin', SYSDATETIME()),
  ('M0230-PI010YGU',     N'MODULE ASSY-RR UPR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N055',   1, 'admin', SYSDATETIME()),
  ('M0230-PI020NNB',     N'MODULE ASSY-RR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N056',   1, 'admin', SYSDATETIME()),
  ('M0230-PI020VKE',     N'MODULE ASSY-RR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N057',   1, 'admin', SYSDATETIME()),
  ('M0230-PI020YGU',     N'MODULE ASSY-RR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N058',   1, 'admin', SYSDATETIME()),
  ('M0230-PI030MEE',     N'MODULE ASSY-RR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N171',   1, 'admin', SYSDATETIME()),
  ('M0230-PI030NNB',     N'MODULE ASSY-RR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N172',   1, 'admin', SYSDATETIME()),
  ('M0230-PI030YGU',     N'MODULE ASSY-RR UPR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N173',   1, 'admin', SYSDATETIME()),
  ('M0230-PI040NNB',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N174',   1, 'admin', SYSDATETIME()),
  ('M0230-PI040VKE',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N175',   1, 'admin', SYSDATETIME()),
  ('M0230-PI040YGU',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N176',   1, 'admin', SYSDATETIME()),
  ('M0231-PI020VKE',     N'MODULE ASSY-RR UPR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS01',   1, 'admin', SYSDATETIME()),
  ('M0231-PI040NNB',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS21',  1, 'admin', SYSDATETIME()),
  ('M0231-PI040VKE',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS22',  1, 'admin', SYSDATETIME()),
  ('M0231-PI040YGU',     N'MODULE ASSY-RR UPR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS23',  1, 'admin', SYSDATETIME()),
  ('M0240-PI000MEE',     N'MODULE ASSY-RR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N129',   1, 'admin', SYSDATETIME()),
  ('M0240-PI000NNB',     N'MODULE ASSY-RR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N059',   1, 'admin', SYSDATETIME()),
  ('M0240-PI000XE8',     N'MODULE ASSY-RR UPR TRIM NO.1,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N060',   1, 'admin', SYSDATETIME()),
  ('M0240-PI000YGU',     N'MODULE ASSY-RR UPR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N061',   1, 'admin', SYSDATETIME()),
  ('M0240-PI010NNB',     N'MODULE ASSY-RR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N062',   1, 'admin', SYSDATETIME()),
  ('M0240-PI010VKE',     N'MODULE ASSY-RR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N063',   1, 'admin', SYSDATETIME()),
  ('M0240-PI010YGU',     N'MODULE ASSY-RR UPR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N064',   1, 'admin', SYSDATETIME()),
  ('M0240-PI020NNB',     N'MODULE ASSY-RR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N065',   1, 'admin', SYSDATETIME()),
  ('M0240-PI020VKE',     N'MODULE ASSY-RR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N066',   1, 'admin', SYSDATETIME()),
  ('M0240-PI020YGU',     N'MODULE ASSY-RR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N067',   1, 'admin', SYSDATETIME()),
  ('M0240-PI030MEE',     N'MODULE ASSY-RR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N177',   1, 'admin', SYSDATETIME()),
  ('M0240-PI030NNB',     N'MODULE ASSY-RR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N178',   1, 'admin', SYSDATETIME()),
  ('M0240-PI030YGU',     N'MODULE ASSY-RR UPR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N179',   1, 'admin', SYSDATETIME()),
  ('M0240-PI040NNB',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N180',   1, 'admin', SYSDATETIME()),
  ('M0240-PI040VKE',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N181',   1, 'admin', SYSDATETIME()),
  ('M0240-PI040YGU',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N182',   1, 'admin', SYSDATETIME()),
  ('M0241-PI020NNB',     N'MODULE ASSY-RR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS16',   1, 'admin', SYSDATETIME()),
  ('M0241-PI020YGU',     N'MODULE ASSY-RR UPR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS02',   1, 'admin', SYSDATETIME()),
  ('M0241-PI040NNB',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS24',  1, 'admin', SYSDATETIME()),
  ('M0241-PI040VKE',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS25',  1, 'admin', SYSDATETIME()),
  ('M0241-PI040YGU',     N'MODULE ASSY-RR UPR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS26',  1, 'admin', SYSDATETIME()),
  ('M0310-PI000NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N068',   1, 'admin', SYSDATETIME()),
  ('M0310-PI000YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N130',   1, 'admin', SYSDATETIME()),
  ('M0310-PI000YGU',     N'MODULE ASSY-FR LWR TRIM NO.1,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N069',   1, 'admin', SYSDATETIME()),
  ('M0310-PI010NNB',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N070',   1, 'admin', SYSDATETIME()),
  ('M0310-PI010YGN',     N'MODULE ASSY-FR LWR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N131',   1, 'admin', SYSDATETIME()),
  ('M0310-PI010YGU',     N'MODULE ASSY-FR LWR TRIM NO.2,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N071',   1, 'admin', SYSDATETIME()),
  ('M0310-PI020NNB',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N072',   1, 'admin', SYSDATETIME()),
  ('M0310-PI020YGN',     N'MODULE ASSY-FR LWR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N132',   1, 'admin', SYSDATETIME()),
  ('M0310-PI020YGU',     N'MODULE ASSY-FR LWR TRIM NO.3,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N073',   1, 'admin', SYSDATETIME()),
  ('M0310-PI030NNB',     N'MODULE ASSY-FR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N074',   1, 'admin', SYSDATETIME()),
  ('M0310-PI030VKE',     N'MODULE ASSY-FR LWR TRIM NO.4,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N075',   1, 'admin', SYSDATETIME()),
  ('M0310-PI030YGN',     N'MODULE ASSY-FR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N133',   1, 'admin', SYSDATETIME()),
  ('M0310-PI030YGU',     N'MODULE ASSY-FR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N076',   1, 'admin', SYSDATETIME()),
  ('M0310-PI040NNB',     N'MODULE ASSY-FR LWR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N077',   1, 'admin', SYSDATETIME()),
  ('M0310-PI040VKE',     N'MODULE ASSY-FR LWR TRIM NO.5,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N078',   1, 'admin', SYSDATETIME()),
  ('M0310-PI040YGN',     N'MODULE ASSY-FR LWR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N134',   1, 'admin', SYSDATETIME()),
  ('M0310-PI040YGU',     N'MODULE ASSY-FR LWR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N079',   1, 'admin', SYSDATETIME()),
  ('M0310-PI050NNB',     N'MODULE ASSY-FR LWR TRIM NO.6, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N080',   1, 'admin', SYSDATETIME()),
  ('M0310-PI050VKE',     N'MODULE ASSY-FR LWR TRIM NO.6,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N081',   1, 'admin', SYSDATETIME()),
  ('M0310-PI050YGN',     N'MODULE ASSY-FR LWR TRIM NO.6, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N135',   1, 'admin', SYSDATETIME()),
  ('M0310-PI050YGU',     N'MODULE ASSY-FR LWR TRIM NO.6, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N082',   1, 'admin', SYSDATETIME()),
  ('M0310-PI060XE8',     N'MODULE ASSY-FR LWR TRIM NO.7, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N083',   1, 'admin', SYSDATETIME()),
  ('M0310-PI070XE8',     N'MODULE ASSY-FR LWR TRIM NO.8, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N084',   1, 'admin', SYSDATETIME()),
  ('M0310-PI080XE8',     N'MODULE ASSY-FR LWR TRIM NO.9, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N085',   1, 'admin', SYSDATETIME()),
  ('M0310-PI090XE8',     N'MODULE ASSY-FR LWR TRIM NO.10, LH',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N086',   1, 'admin', SYSDATETIME()),
  ('M0311-PI000NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS07',   1, 'admin', SYSDATETIME()),
  ('M0311-PI000YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS08',   1, 'admin', SYSDATETIME()),
  ('M0311-PI020NNB',     N'MODULE ASSY-FR LWR TRIM NO.3, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS09',   1, 'admin', SYSDATETIME()),
  ('M0311-PI020YGN',     N'MODULE ASSY-FR LWR TRIM NO.3, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS27',  1, 'admin', SYSDATETIME()),
  ('M0311-PI020YGU',     N'MODULE ASSY-FR LWR TRIM NO.3, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS10',   1, 'admin', SYSDATETIME()),
  ('M0311-PI030XE8',     N'MODULE ASSY-FR LWR TRIM NO.4, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS28',  1, 'admin', SYSDATETIME()),
  ('M0320-PI000NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N087',   1, 'admin', SYSDATETIME()),
  ('M0320-PI000YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N136',   1, 'admin', SYSDATETIME()),
  ('M0320-PI000YGU',     N'MODULE ASSY-FR LWR TRIM NO.1,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N088',   1, 'admin', SYSDATETIME()),
  ('M0320-PI010NNB',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N089',   1, 'admin', SYSDATETIME()),
  ('M0320-PI010YGN',     N'MODULE ASSY-FR LWR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N137',   1, 'admin', SYSDATETIME()),
  ('M0320-PI010YGU',     N'MODULE ASSY-FR LWR TRIM NO.2,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N090',   1, 'admin', SYSDATETIME()),
  ('M0320-PI020NNB',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N091',   1, 'admin', SYSDATETIME()),
  ('M0320-PI020VKE',     N'MODULE ASSY-FR LWR TRIM NO.3,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N092',   1, 'admin', SYSDATETIME()),
  ('M0320-PI020YGN',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N138',   1, 'admin', SYSDATETIME()),
  ('M0320-PI020YGU',     N'MODULE ASSY-FR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N093',   1, 'admin', SYSDATETIME()),
  ('M0320-PI030XE8',     N'MODULE ASSY-FR LWR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N094',   1, 'admin', SYSDATETIME()),
  ('M0321-PI000NNB',     N'MODULE ASSY-FR LWR TRIM NO.1, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS29',  1, 'admin', SYSDATETIME()),
  ('M0321-PI000YGN',     N'MODULE ASSY-FR LWR TRIM NO.1, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS11',   1, 'admin', SYSDATETIME()),
  ('M0321-PI010NNB',     N'MODULE ASSY-FR LWR TRIM NO.2, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS12',   1, 'admin', SYSDATETIME()),
  ('M0321-PI010YGN',     N'MODULE ASSY-FR LWR TRIM NO.2, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS30',  1, 'admin', SYSDATETIME()),
  ('M0321-PI010YGU',     N'MODULE ASSY-FR LWR TRIM NO.2, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS31',  1, 'admin', SYSDATETIME()),
  ('M0321-PI020XE8',     N'MODULE ASSY-FR LWR TRIM NO.3, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS32',  1, 'admin', SYSDATETIME()),
  ('M0330-PI000NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N095',   1, 'admin', SYSDATETIME()),
  ('M0330-PI000YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N139',   1, 'admin', SYSDATETIME()),
  ('M0330-PI000YGU',     N'MODULE ASSY-RR LWR TRIM NO.1,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N096',   1, 'admin', SYSDATETIME()),
  ('M0330-PI010NNB',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N097',   1, 'admin', SYSDATETIME()),
  ('M0330-PI010YGN',     N'MODULE ASSY-RR LWR TRIM NO.2, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N140',   1, 'admin', SYSDATETIME()),
  ('M0330-PI010YGU',     N'MODULE ASSY-RR LWR TRIM NO.2,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N098',   1, 'admin', SYSDATETIME()),
  ('M0330-PI020NNB',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N099',   1, 'admin', SYSDATETIME()),
  ('M0330-PI020VKE',     N'MODULE ASSY-RR LWR TRIM NO.3,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N100',   1, 'admin', SYSDATETIME()),
  ('M0330-PI020YGN',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N141',   1, 'admin', SYSDATETIME()),
  ('M0330-PI020YGU',     N'MODULE ASSY-RR LWR TRIM NO.3, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N101',   1, 'admin', SYSDATETIME()),
  ('M0330-PI030NNB',     N'MODULE ASSY-RR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N102',   1, 'admin', SYSDATETIME()),
  ('M0330-PI030VKE',     N'MODULE ASSY-RR LWR TRIM NO.4,LH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N103',   1, 'admin', SYSDATETIME()),
  ('M0330-PI030YGN',     N'MODULE ASSY-RR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N142',   1, 'admin', SYSDATETIME()),
  ('M0330-PI030YGU',     N'MODULE ASSY-RR LWR TRIM NO.4, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N104',   1, 'admin', SYSDATETIME()),
  ('M0330-PI040XE8',     N'MODULE ASSY-RR LWR TRIM NO.5, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N105',   1, 'admin', SYSDATETIME()),
  ('M0330-PI050XE8',     N'MODULE ASSY-RR LWR TRIM NO.6, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N106',   1, 'admin', SYSDATETIME()),
  ('M0330-PI060XE8',     N'MODULE ASSY-RR LWR TRIM NO.7, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N107',   1, 'admin', SYSDATETIME()),
  ('M0330-PI070XE8',     N'MODULE ASSY-RR LWR TRIM NO.8, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N108',   1, 'admin', SYSDATETIME()),
  ('M0330-PI080NNB',     N'MODULE ASSY-RR LWR TRIM NO.9, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N183',   1, 'admin', SYSDATETIME()),
  ('M0330-PI080YGN',     N'MODULE ASSY-RR LWR TRIM NO.9, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N184',   1, 'admin', SYSDATETIME()),
  ('M0330-PI080YGU',     N'MODULE ASSY-RR LWR TRIM NO.9, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N185',   1, 'admin', SYSDATETIME()),
  ('M0331-PI000NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS33',  1, 'admin', SYSDATETIME()),
  ('M0331-PI000YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS34',  1, 'admin', SYSDATETIME()),
  ('M0331-PI010NNB',     N'MODULE ASSY-RR LWR TRIM NO.2, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS17',   1, 'admin', SYSDATETIME()),
  ('M0331-PI020YGU',     N'MODULE ASSY-RR LWR TRIM NO.3, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS03',   1, 'admin', SYSDATETIME()),
  ('M0331-PI050NNB',     N'MODULE ASSY-RR LWR TRIM NO.6, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS35',  1, 'admin', SYSDATETIME()),
  ('M0331-PI050YGN',     N'MODULE ASSY-RR LWR TRIM NO.6, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS36',  1, 'admin', SYSDATETIME()),
  ('M0331-PI050YGU',     N'MODULE ASSY-RR LWR TRIM NO.6, LH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS37',  1, 'admin', SYSDATETIME()),
  ('M0340-PI000NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N109',   1, 'admin', SYSDATETIME()),
  ('M0340-PI000YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N143',   1, 'admin', SYSDATETIME()),
  ('M0340-PI000YGU',     N'MODULE ASSY-RR LWR TRIM NO.1,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N110',   1, 'admin', SYSDATETIME()),
  ('M0340-PI010NNB',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N111',   1, 'admin', SYSDATETIME()),
  ('M0340-PI010YGN',     N'MODULE ASSY-RR LWR TRIM NO.2, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N144',   1, 'admin', SYSDATETIME()),
  ('M0340-PI010YGU',     N'MODULE ASSY-RR LWR TRIM NO.2,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N112',   1, 'admin', SYSDATETIME()),
  ('M0340-PI020NNB',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N113',   1, 'admin', SYSDATETIME()),
  ('M0340-PI020VKE',     N'MODULE ASSY-RR LWR TRIM NO.3,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N114',   1, 'admin', SYSDATETIME()),
  ('M0340-PI020YGN',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N145',   1, 'admin', SYSDATETIME()),
  ('M0340-PI020YGU',     N'MODULE ASSY-RR LWR TRIM NO.3, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N115',   1, 'admin', SYSDATETIME()),
  ('M0340-PI030NNB',     N'MODULE ASSY-RR LWR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N116',   1, 'admin', SYSDATETIME()),
  ('M0340-PI030VKE',     N'MODULE ASSY-RR LWR TRIM NO.4,RH',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N117',   1, 'admin', SYSDATETIME()),
  ('M0340-PI030YGN',     N'MODULE ASSY-RR LWR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N146',   1, 'admin', SYSDATETIME()),
  ('M0340-PI030YGU',     N'MODULE ASSY-RR LWR TRIM NO.4, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N118',   1, 'admin', SYSDATETIME()),
  ('M0340-PI040XE8',     N'MODULE ASSY-RR LWR TRIM NO.5, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N119',   1, 'admin', SYSDATETIME()),
  ('M0340-PI050XE8',     N'MODULE ASSY-RR LWR TRIM NO.6, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N120',   1, 'admin', SYSDATETIME()),
  ('M0340-PI060XE8',     N'MODULE ASSY-RR LWR TRIM NO.7, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N121',   1, 'admin', SYSDATETIME()),
  ('M0340-PI070XE8',     N'MODULE ASSY-RR LWR TRIM NO.8, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N122',   1, 'admin', SYSDATETIME()),
  ('M0340-PI080NNB',     N'MODULE ASSY-RR LWR TRIM NO.9, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N186',   1, 'admin', SYSDATETIME()),
  ('M0340-PI080YGN',     N'MODULE ASSY-RR LWR TRIM NO.9, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N187',   1, 'admin', SYSDATETIME()),
  ('M0340-PI080YGU',     N'MODULE ASSY-RR LWR TRIM NO.9, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N188',   1, 'admin', SYSDATETIME()),
  ('M0341-PI000NNB',     N'MODULE ASSY-RR LWR TRIM NO.1, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS18',   1, 'admin', SYSDATETIME()),
  ('M0341-PI000YGN',     N'MODULE ASSY-RR LWR TRIM NO.1, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS38',  1, 'admin', SYSDATETIME()),
  ('M0341-PI010YGN',     N'MODULE ASSY-RR LWR TRIM NO.2, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS04',   1, 'admin', SYSDATETIME()),
  ('M0341-PI020NNB',     N'MODULE ASSY-RR LWR TRIM NO.3, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS19',   1, 'admin', SYSDATETIME()),
  ('M0341-PI020YGN',     N'MODULE ASSY-RR LWR TRIM NO.3, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS05',   1, 'admin', SYSDATETIME()),
  ('M0341-PI030XE8',     N'MODULE ASSY-RR LWR TRIM NO.4, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS39',  1, 'admin', SYSDATETIME()),
  ('M0341-PI040XE8',     N'MODULE ASSY-RR LWR TRIM NO.5, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'QASP',  'AS20',   1, 'admin', SYSDATETIME()),
  ('M0341-PI050NNB',     N'MODULE ASSY-RR LWR TRIM NO.6, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS40',  1, 'admin', SYSDATETIME()),
  ('M0341-PI050YGN',     N'MODULE ASSY-RR LWR TRIM NO.6, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS41',  1, 'admin', SYSDATETIME()),
  ('M0341-PI050YGU',     N'MODULE ASSY-RR LWR TRIM NO.6, RH (AS)',     'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS42',  1, 'admin', SYSDATETIME()),
  ('M2310-PI000MEE',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N189',   1, 'admin', SYSDATETIME()),
  ('M2310-PI000NNB',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N190',   1, 'admin', SYSDATETIME()),
  ('M2310-PI000YGU',     N'PNL ASSY-FR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N191',   1, 'admin', SYSDATETIME()),
  ('M2310-PI010NNB',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N192',   1, 'admin', SYSDATETIME()),
  ('M2310-PI010VKE',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N193',   1, 'admin', SYSDATETIME()),
  ('M2310-PI010YGU',     N'PNL ASSY-FR DR UPR TRIM,LH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N194',   1, 'admin', SYSDATETIME()),
  ('M2320-PI000MEE',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N195',   1, 'admin', SYSDATETIME()),
  ('M2320-PI000NNB',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N196',   1, 'admin', SYSDATETIME()),
  ('M2320-PI000YGU',     N'PNL ASSY-FR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N197',   1, 'admin', SYSDATETIME()),
  ('M2320-PI010NNB',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N198',   1, 'admin', SYSDATETIME()),
  ('M2320-PI010VKE',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N199',   1, 'admin', SYSDATETIME()),
  ('M2320-PI010YGU',     N'PNL ASSY-FR DR UPR TRIM,RH(IMG)',           'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N200',   1, 'admin', SYSDATETIME()),
  ('M3310-PI000MEE',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N201',   1, 'admin', SYSDATETIME()),
  ('M3310-PI000NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N202',   1, 'admin', SYSDATETIME()),
  ('M3310-PI000YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N203',   1, 'admin', SYSDATETIME()),
  ('M3310-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N204',   1, 'admin', SYSDATETIME()),
  ('M3310-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N205',   1, 'admin', SYSDATETIME()),
  ('M3310-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N206',   1, 'admin', SYSDATETIME()),
  ('M3311-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS43',  1, 'admin', SYSDATETIME()),
  ('M3311-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS44',  1, 'admin', SYSDATETIME()),
  ('M3311-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,LH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS45',  1, 'admin', SYSDATETIME()),
  ('M3320-PI000MEE',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N207',   1, 'admin', SYSDATETIME()),
  ('M3320-PI000NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N208',   1, 'admin', SYSDATETIME()),
  ('M3320-PI000YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(PAINT)',         'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N209',   1, 'admin', SYSDATETIME()),
  ('M3320-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N210',   1, 'admin', SYSDATETIME()),
  ('M3320-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N211',   1, 'admin', SYSDATETIME()),
  ('M3320-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'QSUB',  'N212',   1, 'admin', SYSDATETIME()),
  ('M3321-PI020NNB',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS46',  1, 'admin', SYSDATETIME()),
  ('M3321-PI020VKE',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS47',  1, 'admin', SYSDATETIME()),
  ('M3321-PI020YGU',     N'PNL ASSY-RR DR UPR TRIM,RH(IMG,CUR)',       'SUB',      NULL, 'NE1A', 'EA', 0, 'AQMT',  'OAS48',  1, 'admin', SYSDATETIME()),
  ('M5836-PI100MEE',     N'MODULE-CTR PLR LWR-UPR PIECE, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN234',  1, 'admin', SYSDATETIME()),
  ('M5836-PI100NNB',     N'MODULE-CTR PLR LWR-UPR PIECE, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN228',  1, 'admin', SYSDATETIME()),
  ('M5836-PI100VKE',     N'MODULE-CTR PLR LWR-UPR PIECE, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN229',  1, 'admin', SYSDATETIME()),
  ('M5836-PI100YGU',     N'MODULE-CTR PLR LWR-UPR PIECE, LH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN230',  1, 'admin', SYSDATETIME()),
  ('M5846-PI100MEE',     N'MODULE-CTR PLR LWR-UPR PIECE, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN235',  1, 'admin', SYSDATETIME()),
  ('M5846-PI100NNB',     N'MODULE-CTR PLR LWR-UPR PIECE, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN231',  1, 'admin', SYSDATETIME()),
  ('M5846-PI100VKE',     N'MODULE-CTR PLR LWR-UPR PIECE, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN232',  1, 'admin', SYSDATETIME()),
  ('M5846-PI100YGU',     N'MODULE-CTR PLR LWR-UPR PIECE, RH',          'SUB',      NULL, 'NE1A', 'EA', 0, 'AQSU',  'BN233',  1, 'admin', SYSDATETIME()),
  ('M85311-PI000MMN',    N'BOARD ASSY-HEADLINING (STD)',               'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X005',   1, 'admin', SYSDATETIME()),
  ('M85311-PI000YGU',    N'BOARD ASSY-HEADLINING (STD)',               'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X006',   1, 'admin', SYSDATETIME()),
  ('M85411-PI000YGU',    N'BOARD ASSY-HEADLINING(SRF)',                'SUB',      NULL, 'NE1A', 'EA', 0, 'QXXX',  'X027',   1, 'admin', SYSDATETIME());
GO

-- Equipment
INSERT INTO dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, MakerModel, InstallDate, TheoreticalCycle, TargetOEE, PlcAddress, Status, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('INJ-650-01',  N'Husky 650T Injection',     'INJ_MACHINE',  'LINE-INJ-01', N'Husky H650 RS135/132', '2023-06-15', 45.0, 85.00, '192.168.10.21', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('INJ-850-02',  N'Husky 850T Injection',     'INJ_MACHINE',  'LINE-INJ-02', N'Husky H850 RS180/180', '2023-08-22', 52.0, 85.00, '192.168.10.22', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('IMG-PRESS-01',N'Vinyl Wrapping Press',     'WRAP_PRESS',   'LINE-IMG-01', N'Dieffenbacher VP-400', '2024-02-10', 60.0, 82.00, '192.168.10.31', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('PNT-ROBOT-01',N'Paint Robot ABB IRB-6700', 'PNT_ROBOT',    'LINE-PNT-01', N'ABB IRB-6700-235',     '2024-04-05', 30.0, 80.00, '192.168.10.41', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('OVEN-A1',     N'Cure Oven Zone A1',        'OVEN_UNIT',    'LINE-PNT-01', N'Eisenmann CT-180',     '2024-04-05', 0.0,  90.00, '192.168.10.42', 'IDLE', 1, 'admin', SYSDATETIME());
GO

PRINT '✓ Seed data inserted: 9 UOMs, 7 Customers, 5 Vendors, 5 Lines, 1188 Items, 5 Equipment';
GO

-- ════════════════════════════════════════════════════════════════════════
-- 사출 자동수집 시드: 시뮬레이터 검증 금형코드 4종 (MEADTRCTNNB / NEAFUCNNB / LQ2DTMDCBK / LQ2DTRUCBK)
--   색상코드 = 뒤 3자리, 금형코드 = 나머지 (원본 Main.cs 규칙)
--   품번은 위 시드의 실존 MD_Item 사용
-- ════════════════════════════════════════════════════════════════════════
DELETE FROM dbo.MD_Mold WHERE MoldID IN ('MOLD-LQ2-DTMD','MOLD-LQ2-DTRU','MOLD-MEA-DTRCT','MOLD-NEA-FUC');
GO
INSERT INTO dbo.MD_Mold (MoldID, MoldName, RatedShots, CurrentShots, CavityCount, Tonnage, Status, CreatedBy, CreatedTS) VALUES
  ('MOLD-LQ2-DTMD',  N'LQ2 Door Trim MD (2-cav)',  500000, 0, 2, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-LQ2-DTRU',  N'LQ2 Door Trim RU (2-cav)',  500000, 0, 2, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-MEA-DTRCT', N'MEA Door Trim CT (1-cav)',  300000, 0, 1, 650, 'ACTIVE', 'admin', SYSDATETIME()),
  ('MOLD-NEA-FUC',   N'NEA FUC (1-cav)',           300000, 0, 1, 650, 'ACTIVE', 'admin', SYSDATETIME());
GO
INSERT INTO dbo.MD_MoldItemMap (MoldCode, ColorCode, CavityNo, CavityPos, ItemNo, MoldID, CreatedBy) VALUES
  ('LQ2DTMD',  'CBK', 1, 'LH', '83335-P8000RBQ',  'MOLD-LQ2-DTMD',  'admin'),
  ('LQ2DTMD',  'CBK', 2, 'RH', '83345-P8000RBQ',  'MOLD-LQ2-DTMD',  'admin'),
  ('LQ2DTRU',  'CBK', 1, 'LH', 'M83371-P8000RBQ', 'MOLD-LQ2-DTRU',  'admin'),
  ('LQ2DTRU',  'CBK', 2, 'RH', 'M83381-P8000RBQ', 'MOLD-LQ2-DTRU',  'admin'),
  ('MEADTRCT', 'NNB', 1, 'LH', '83314-P8000',     'MOLD-MEA-DTRCT', 'admin'),
  ('NEAFUC',   'NNB', 1, 'LH', '83314-P8010',     'MOLD-NEA-FUC',   'admin');
GO
INSERT INTO dbo.MD_InjCondItem (LineID, ItemCode, ItemName, SetAddress, ActualAddress, DataType, CreatedBy) VALUES
  ('LINE-INJ-01', 'TEMP',  N'배럴온도', 5400, 5404, 'FLOAT', 'admin'),
  ('LINE-INJ-01', 'PRESS', N'사출압력', 5410, 5414, 'LONG',  'admin');
GO
PRINT '✓ Seed data inserted: 4 Molds, 6 MoldItemMap, 2 InjCondItem (사출 자동수집)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- SYS_Screen  (화면 기준정보 — 가시성 및 내비게이션 관리)
-- ════════════════════════════════════════════════════════════════════════
IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL DROP TABLE dbo.SYS_Screen;
GO

CREATE TABLE dbo.SYS_Screen (
  [ScreenID]       INT IDENTITY     NOT NULL,
  [ScreenCode]     VARCHAR(20)      NOT NULL,   -- e.g. 'PP-001', 'SYS-003'
  [ModuleCode]     VARCHAR(10)      NOT NULL,   -- 포탈 식별자 (WEB / POP / PDA)
  [ProcessCode]    VARCHAR(10)          NULL,   -- 기능 영역 (PP / MNT / RPT / MD / SYS)
  [SubProcessCode] VARCHAR(10)          NULL,   -- MD 서브그룹 (Fd/Rp/Re/Rm/Ql)
  [ScreenName]     NVARCHAR(100)    NOT NULL,
  [ScreenNameEn]   NVARCHAR(100)        NULL,
  [HRef]           VARCHAR(200)         NULL,   -- route path (e.g. 'md/rp/line')
  [LidLabel]     VARCHAR(20)          NULL,   -- chip label shown in NavMenu
  [SortOrder]    INT                  NULL,
  [IsVisible]    BIT                  NULL  DEFAULT 1,
  [CreatedBy]    VARCHAR(50)      NOT NULL,
  [CreatedTS]    DATETIME2            NULL  DEFAULT SYSDATETIME(),
  [ModifiedBy]   VARCHAR(50)          NULL,
  [ModifiedTS]   DATETIME2            NULL,
  CONSTRAINT PK_SYS_Screen PRIMARY KEY CLUSTERED ([ScreenID]),
  CONSTRAINT UQ_SYS_Screen UNIQUE ([ScreenCode])
);
GO

-- ── Seed: PP · 생산계획 ──────────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('PP-001', 'PP', N'수요 예측',        N'Forecast',          'pp/forecast',         'PP-001',  1, 1, 'admin'),
  ('PP-002', 'PP', N'공급계획 가져오기',   N'Supply Plan Import', 'pp/supply-plan-import', 'PP-002',  2, 1, 'admin'),
  ('PP-003', 'PP', N'계획 확정',        N'Plan Confirm',      'pp/plan-confirm',     'PP-003',  3, 1, 'admin'),
  ('PP-004', 'PP', N'작업 지시',        N'Work Order',        'pp/work-order',       'PP-004',  4, 1, 'admin'),
  ('PP-005', 'PP', N'MRP',              N'MRP',               'pp/mrp',              'PP-005',  5, 1, 'admin'),
  ('PP-006', 'PP', N'구매 요청',        N'Purchase Req',      'pp/purchase-req',     'PP-006',  6, 1, 'admin'),
  ('PP-007', 'PP', N'작업 지시 릴리스', N'WO Release',        'pp/wo-release',       'PP-007',  7, 1, 'admin'),
  ('PP-CAL', 'PP', N'캘린더',           N'Calendar',          'pp/calendar',         'CAL',     8, 1, 'admin'),
  ('PP-LSB', 'PP', N'라인 일정',        N'Line Schedule',     'pp/line-schedule',    'LSB',     9, 1, 'admin'),
  ('PP-OEE', 'PP', N'라인 OEE',         N'Line OEE',          'pp/oee',              'OEE',    10, 1, 'admin'),
  ('PP-DTL', 'PP', N'비가동 이력',      N'Downtime Log',      'pp/downtime',         'DTL',    11, 1, 'admin'),
  ('PP-ODM', 'PP', N'비가동 모니터',    N'Downtime Monitor',  'pp/downtime-monitor', 'ODM',    12, 1, 'admin'),
  ('PP-OTD', 'PP', N'납기 준수율',      N'On-Time Delivery',  'pp/delivery',         'OTD',    13, 1, 'admin');
GO

-- ── Seed: MNT · 설비보전 ────────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('MNT-001', 'MNT', N'설비 카드',   N'Equipment Card',   'mnt/equipment-card', 'MNT-001', 1, 1, 'admin'),
  ('MNT-002', 'MNT', N'고장 등록',   N'Failure Register', 'mnt/failure',        'MNT-002', 2, 1, 'admin'),
  ('MNT-003', 'MNT', N'OEE 분석',    N'OEE Analysis',     'mnt/oee-analysis',   'MNT-003', 3, 1, 'admin'),
  ('MNT-004', 'MNT', N'금형 관리',   N'Mold Management',  'mnt/mold',           'MNT-004', 4, 1, 'admin'),
  ('MNT-005', 'MNT', N'PM 일정',     N'PM Schedule',      'mnt/pm-schedule',    'MNT-005', 5, 1, 'admin'),
  ('MNT-006', 'MNT', N'비가동 이력', N'Downtime Log',     'mnt/downtime',       'MNT-006', 6, 1, 'admin'),
  ('MNT-007', 'MNT', N'작업 지시',   N'Work Order',       'mnt/work-order',     'MNT-007', 7, 1, 'admin'),
  ('MNT-008', 'MNT', N'예비 부품',   N'Spare Parts',      'mnt/spare-parts',    'MNT-008', 8, 1, 'admin'),
  ('MNT-009', 'MNT', N'대시보드',    N'Dashboard',        'mnt/dashboard',      'MNT-009', 9, 1, 'admin');
GO

-- ── Seed: RPT · 보고서 ──────────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('RPT-001', 'RPT', N'일별 생산 실적', N'Daily Production',   'rpt/daily-production',   'RPT-001',  1, 1, 'admin'),
  ('RPT-002', 'RPT', N'불량 파레토',   N'Defect Pareto',      'rpt/defect-pareto',      'RPT-002',  2, 1, 'admin'),
  ('RPT-003', 'RPT', N'일별 출하 현황',N'Daily Shipment',     'rpt/daily-shipment',     'RPT-003',  3, 1, 'admin'),
  ('RPT-004', 'RPT', N'납기 준수율',   N'On-Time Delivery',   'rpt/on-time',            'RPT-004',  4, 1, 'admin'),
  ('RPT-005', 'RPT', N'재고 현황',     N'Inventory Status',   'rpt/inventory',          'RPT-005',  5, 1, 'admin'),
  ('RPT-006', 'RPT', N'설비 OEE',      N'Equipment OEE',      'rpt/equipment-oee',      'RPT-006',  6, 1, 'admin'),
  ('RPT-007', 'RPT', N'월간 KPI',      N'Monthly KPI',        'rpt/monthly-kpi',        'RPT-007',  7, 1, 'admin'),
  ('RPT-008', 'RPT', N'계획 준수율',   N'Schedule Adherence', 'rpt/schedule-adherence', 'RPT-008',  8, 1, 'admin'),
  ('RPT-009', 'RPT', N'리포트 센터',   N'Report Center',      'rpt/report-center',      'RPT-009',  9, 1, 'admin'),
  ('RPT-010', 'RPT', N'리포트 빌더',   N'Report Builder',     'rpt/report-builder',     'RPT-010', 10, 1, 'admin');
GO

-- ── Seed: MD · 마스터데이터 ─────────────────────────────────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('MD-001', 'MD', N'공장/라인 기준정보 관리',     N'Factory / Line Master',          'md/line',                'MD-001',  1, 1, 'admin'),
  ('MD-002', 'MD', N'공정 기준정보 관리',          N'Process / Station Master',       'md/station',             'MD-002',  2, 1, 'admin'),
  ('MD-003', 'MD', N'제품 기준정보 관리',          N'Product Item Master',            'md/items',               'MD-003',  3, 1, 'admin'),
  ('MD-004', 'MD', N'BOM 관리',                    N'BOM Management',                 'md/bom',                 'MD-004',  4, 1, 'admin'),
  ('MD-005', 'MD', N'BOP 관리',                    N'BOP Management',                 'md/bop',                 'MD-005',  5, 1, 'admin'),
  ('MD-006', 'MD', N'Work Center 관리',            N'Work Center Management',         'md/work-center',         'MD-006',  6, 1, 'admin'),
  ('MD-007', 'MD', N'금형 기준정보 관리',          N'Mold Master',                    'md/mold',                'MD-007',  7, 1, 'admin'),
  ('MD-008', 'MD', N'원부자재 기준정보 관리',      N'Paint & Fabric Master',          'md/rm/paint-fabric',     'MD-008',  8, 1, 'admin'),
  ('MD-009', 'MD', N'공급업체 기준정보 관리',      N'Vendor Master',                  'md/vendor',              'MD-009',  9, 1, 'admin'),
  ('MD-010', 'MD', N'고객사 기준정보 관리',        N'Customer Master',                'md/customer',            'MD-010', 10, 1, 'admin'),
  ('MD-011', 'MD', N'출하처 기준정보 관리',        N'Shipment Destination Master',    'md/shipment-dest',       'MD-011', 11, 1, 'admin'),
  ('MD-012', 'MD', N'불량유형 기준정보 관리',      N'Defect Code Master',             'md/defect-code',         'MD-012', 12, 1, 'admin'),
  ('MD-013', 'MD', N'불량원인 기준정보 관리',      N'Defect Cause Master',            'md/defect-cause',        'MD-013', 13, 1, 'admin'),
  ('MD-014', 'MD', N'설비 기준정보 관리',          N'Equipment Master',               'md/equipment',           'MD-014', 14, 1, 'admin'),
  ('MD-015', 'MD', N'건조로 기준정보 관리',        N'Oven Master',                    'md/oven',                'MD-015', 15, 1, 'admin'),
  ('MD-016', 'MD', N'지그 기준정보 관리',          N'Jig Master',                     'md/jig',                 'MD-016', 16, 1, 'admin'),
  ('MD-017', 'MD', N'검사기준 기준정보 관리',      N'Inspection Standard Master',     'md/inspection-standard', 'MD-017', 17, 1, 'admin'),
  ('MD-018', 'MD', N'창고/로케이션 기준정보 관리', N'Warehouse Location Master',      'md/location',            'MD-018', 18, 1, 'admin'),
  ('MD-019', 'MD', N'단위 관리',                   N'UOM Master',                     'md/uom',                 'MD-019', 19, 1, 'admin'),
  ('MD-020', 'MD', N'RFID 태그 관리',              N'RFID Tag Master',                'md/rm/rfid-tag',         'MD-020', 20, 1, 'admin'),
  ('MD-021', 'MD', N'RAL 색상 관리',               N'RAL Color Master',               'md/rm/ral-color',        'MD-021', 21, 1, 'admin'),
  ('MD-022', 'MD', N'RFID 리더 관리',              N'RFID Reader Master',             'md/rm/rfid-reader',      'MD-022', 22, 1, 'admin'),
  ('MD-023', 'MD', N'포장 사양 관리',              N'Packaging Spec Master',          'md/packaging-spec',      'MD-023', 23, 1, 'admin'),
  ('MD-024', 'MD', N'라벨 템플릿 관리',            N'Label Template Master',          'md/label-template',      'MD-024', 24, 1, 'admin'),
  ('MD-025', 'MD', N'사유 코드 관리',              N'Reason Code Master',             'md/reason-code',         'MD-025', 25, 1, 'admin'),
  ('MD-026', 'MD', N'예비품 마스터',               N'Spare Part Master',              'md/spare-part',          'MD-026', 26, 1, 'admin'),
  ('MD-027', 'MD', N'PM 템플릿 관리',              N'PM Template Master',             'md/pm-template',         'MD-027', 27, 1, 'admin'),
  ('MD-028', 'MD', N'라인 시간 패턴 관리',         N'Line Time Pattern Master',       'md/line-time-pattern',   'MD-028', 28, 1, 'admin'),
  ('MD-029', 'MD', N'레시피 관리',                 N'Recipe Master',                  'md/recipe',              'MD-029', 29, 1, 'admin'),
  ('MD-030', 'MD', N'코드 기준정보 관리',          N'Common Code Master',             'md/common-code',         'MD-030', 30, 1, 'admin');
GO

-- ── Seed: SYS · 시스템 (SYS-003=화면관리, SYS-004=RBAC) ─────────────
INSERT INTO dbo.SYS_Screen (ScreenCode, ModuleCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy) VALUES
  ('SYS-001', 'SYS', N'사용자 관리',           N'User Management',          'sys/users',         'SYS-001',  1, 1, 'admin'),
  ('SYS-002', 'SYS', N'역할 관리',             N'Role Management',          'sys/roles',         'SYS-002',  2, 1, 'admin'),
  ('SYS-003', 'SYS', N'화면 관리',             N'Screen Management',        'sys/screens',       'SYS-003',  3, 1, 'admin'),
  ('SYS-004', 'SYS', N'역할/권한 관리 (RBAC)', N'Role & Permission (RBAC)', 'sys/rbac',          'SYS-004',  4, 1, 'admin'),
  ('SYS-005', 'SYS', N'공장 캘린더',           N'Factory Calendar',         'sys/calendar',      'SYS-005',  5, 1, 'admin'),
  ('SYS-006', 'SYS', N'인터페이스 모니터',     N'Interface Monitor',        'sys/interfaces',    'SYS-006',  6, 1, 'admin'),
  ('SYS-007', 'SYS', N'감사 로그',             N'Audit Log',                'sys/audit',         'SYS-007',  7, 1, 'admin'),
  ('SYS-008', 'SYS', N'알림 관리',             N'Notification Management',  'sys/notifications', 'SYS-008',  8, 1, 'admin'),
  ('SYS-009', 'SYS', N'시스템 설정',           N'System Configuration',     'sys/config',        'SYS-009',  9, 1, 'admin'),
  ('SYS-010', 'SYS', N'시스템 상태',           N'System Health',            'sys/health',        'SYS-010', 10, 1, 'admin');
GO

PRINT '✓ SYS_Screen: 72 rows (PP:13 + MNT:9 + RPT:10 + MD:30 + SYS:10)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- SYS_InterfaceMonitor  (인터페이스 모니터링 샘플 데이터)
-- ════════════════════════════════════════════════════════════════════════
INSERT INTO dbo.SYS_InterfaceMonitor
    (InterfaceCode, InterfaceName, Direction, Endpoint, Protocol,
     ConnStatus, LastSyncTS, MaxGapMinutes, LastRecordCount, RetryCount, LastErrorMsg, IsEnabled, CreatedBy)
VALUES
    ('SAP-PROD',  N'SAP 생산실적 전송',   'OUT', 'https://sap.seyon.local:8443/sap/pi/prod',  'HTTPS', 'OK',   DATEADD(MINUTE,  -5, SYSDATETIME()),  30,  120, 0, NULL, 1, 'admin'),
    ('SAP-WO',    N'SAP 작업지시 수신',   'IN',  'https://sap.seyon.local:8443/sap/pi/wo',    'HTTPS', 'OK',   DATEADD(MINUTE, -12, SYSDATETIME()),  60,   45, 0, NULL, 1, 'admin'),
    ('SAP-MM',    N'SAP 자재이동 연동',   'BI',  'https://sap.seyon.local:8443/sap/pi/mm',    'HTTPS', 'WARN', DATEADD(MINUTE, -95, SYSDATETIME()),  60,    0, 2, N'Connection timeout after 30s', 1, 'admin'),
    ('EDI-HMC',   N'현대차 EDI 수주',    'IN',  'ftp://edi.hyundai.com/seyon/',               'FTP',   'OK',   DATEADD(MINUTE,  -3, SYSDATETIME()), 120,  210, 0, NULL, 1, 'admin'),
    ('EDI-KIA',   N'기아차 EDI 수주',    'IN',  'ftp://edi.kia.com/seyon/',                   'FTP',   'OK',   DATEADD(MINUTE,  -8, SYSDATETIME()), 120,   87, 0, NULL, 1, 'admin'),
    ('PLC-INJ01', N'사출 PLC #1',        'IN',  '192.168.10.21:102',                          'S7',    'OK',   DATEADD(SECOND, -30, SYSDATETIME()),   5,    1, 0, NULL, 1, 'admin'),
    ('PLC-INJ02', N'사출 PLC #2',        'IN',  '192.168.10.22:102',                          'S7',    'DOWN', DATEADD(MINUTE, -35, SYSDATETIME()),   5,    0, 5, N'No route to host (192.168.10.22)', 1, 'admin'),
    ('PLC-PNT01', N'도장 PLC',           'IN',  '192.168.10.41:102',                          'S7',    'OK',   DATEADD(SECOND, -45, SYSDATETIME()),   5,    1, 0, NULL, 1, 'admin'),
    ('WMS-SYNC',  N'WMS 재고 동기화',    'BI',  'http://wms.seyon.local/api/sync',            'HTTP',  'OK',   DATEADD(MINUTE,  -8, SYSDATETIME()),  15,   88, 0, NULL, 1, 'admin'),
    ('MES-LABEL', N'라벨 프린터 서버',   'OUT', '192.168.20.10:9100',                         'TCP',   'ERROR',DATEADD(MINUTE, -62, SYSDATETIME()),  10,    0, 3, N'EPSON TM-T88VI: paper jam detected', 1, 'admin');
GO

PRINT '✓ SYS_InterfaceMonitor: 10 rows';
GO

-- ════════════════════════════════════════════════════════════════════════
-- Verification
-- ════════════════════════════════════════════════════════════════════════
SELECT
  COUNT(*) AS TotalTables
FROM sys.tables;
GO

SELECT
  LEFT(name, 4) AS Module,
  COUNT(*)      AS Tables
FROM sys.tables
WHERE name LIKE 'MD[_]%' OR name LIKE 'WH[_]%' OR name LIKE 'PP[_]%'
   OR name LIKE 'PR[_]%' OR name LIKE 'PNT[_]%' OR name LIKE 'QC[_]%'
   OR name LIKE 'FG[_]%' OR name LIKE 'MNT[_]%' OR name LIKE 'SYS[_]%'
   OR name LIKE 'AspNet%' OR name = 'tbl_Lot'
GROUP BY LEFT(name, 4)
ORDER BY 1;
GO
