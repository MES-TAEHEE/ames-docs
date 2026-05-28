-- ════════════════════════════════════════════════════════════════════════
-- A-MES Database Schema (Auto-generated)
-- Generated: 2026-05-28 15:14:15
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
  [ItemNo                   ] VARCHAR(20)          NOT NULL,
  [ItemName                 ] NVARCHAR(80)         NOT NULL,
  [ItemNameEN               ] NVARCHAR(80)             NULL,
  [ItemType                 ] VARCHAR(10)              NULL,
  [ItemCategory             ] VARCHAR(30)              NULL,
  [DefaultUOM               ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [RoutingType              ] CHAR(1)                  NULL,
  [MinStock                 ] DECIMAL(14,4)            NULL,
  [MaxStock                 ] DECIMAL(14,4)            NULL,
  [SafetyStock              ] DECIMAL(14,4)            NULL,
  [UnitCost                 ] DECIMAL(14,2)            NULL,
  [CustItemNoSAV            ] VARCHAR(30)              NULL,
  [CustItemNoGEO            ] VARCHAR(30)              NULL,
  [DrawingNo                ] VARCHAR(30)              NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(20)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Item PRIMARY KEY CLUSTERED ([ItemNo])
);
GO

-- ── MD_Bom  (BOM (MD-02))
CREATE TABLE dbo.MD_Bom (
  [BOMID                    ] VARCHAR(24)          NOT NULL,
  [ParentItemNo             ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CompItemNo               ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [BOMLevel                 ] INT                      NULL,
  [QtyPer                   ] DECIMAL(12,4)            NULL,
  [UOM                      ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [ScrapPct                 ] DECIMAL(5,2)             NULL,
  [VersionID                ] VARCHAR(24)              NULL,  -- FK -> MD_BomVersion.VersionID
  [Position                 ] INT                      NULL,
  [Note                     ] NVARCHAR(120)            NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Bom PRIMARY KEY CLUSTERED ([BOMID])
);
GO

-- ── MD_BomVersion  (BOM 버전 (MD-03))
CREATE TABLE dbo.MD_BomVersion (
  [VersionID                ] VARCHAR(24)          NOT NULL,
  [RootItemNo               ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VersionNo                ] VARCHAR(10)              NULL,
  [EffFrom                  ] DATE                     NULL,
  [EffTo                    ] DATE                     NULL,
  [ChangeType               ] VARCHAR(16)              NULL,
  [ChangeReason             ] NVARCHAR(200)            NULL,
  [RequestedBy              ] VARCHAR(20)              NULL,
  [ApprovedBy               ] VARCHAR(20)              NULL,
  [ApprovedTS               ] DATETIME2                NULL,
  [Status                   ] VARCHAR(12)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_BomVersion PRIMARY KEY CLUSTERED ([VersionID])
);
GO

-- ── MD_Bop  (BOP 라우팅 (MD-04))
CREATE TABLE dbo.MD_Bop (
  [BOPID                    ] VARCHAR(24)          NOT NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RoutingType              ] CHAR(1)                  NULL,
  [StepSeq                  ] INT                      NULL,
  [ProcessCode              ] VARCHAR(10)              NULL,
  [WorkCenterID             ] VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [StdCycleTime             ] DECIMAL(8,2)             NULL,
  [StdSetupTime             ] DECIMAL(8,2)             NULL,
  [QcRequiredFlag           ] BIT                      NULL,
  [StepDescription          ] NVARCHAR(120)            NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Bop PRIMARY KEY CLUSTERED ([BOPID])
);
GO

-- ── MD_WorkCenter  (작업장 (MD-05))
CREATE TABLE dbo.MD_WorkCenter (
  [WCID                     ] VARCHAR(20)          NOT NULL,
  [WCName                   ] NVARCHAR(50)             NULL,
  [ProcessType              ] VARCHAR(16)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [DailyCapacity            ] INT                      NULL,
  [StdManpower              ] INT                      NULL,
  [CostCenterCode           ] VARCHAR(20)              NULL,
  [LocationDesc             ] NVARCHAR(60)             NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_WorkCenter PRIMARY KEY CLUSTERED ([WCID])
);
GO

-- ── MD_InspectionStandard  (검사 기준 (MD-06))
CREATE TABLE dbo.MD_InspectionStandard (
  [InspStdID                ] VARCHAR(20)          NOT NULL,
  [ItemID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [ProcessCode              ] VARCHAR(10)              NULL,
  [InspType                 ] VARCHAR(10)              NULL,
  [CharName                 ] NVARCHAR(60)             NULL,
  [SpecNominal              ] DECIMAL(12,4)            NULL,
  [SpecLSL                  ] DECIMAL(12,4)            NULL,
  [SpecUSL                  ] DECIMAL(12,4)            NULL,
  [UOM                      ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [SamplingPlan             ] VARCHAR(20)              NULL,
  [InspMethod               ] NVARCHAR(40)             NULL,
  [IsCTQ                    ] BIT                      NULL,
  [EffectiveDate            ] DATE                     NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_InspectionStandard PRIMARY KEY CLUSTERED ([InspStdID])
);
GO

-- ── MD_Vendor  (거래선 (MD-07))
CREATE TABLE dbo.MD_Vendor (
  [VendorID                 ] VARCHAR(20)          NOT NULL,
  [VendorName               ] NVARCHAR(80)             NULL,
  [VendorType               ] VARCHAR(10)              NULL,
  [VendorCategory           ] NVARCHAR(30)             NULL,
  [BizRegNo                 ] VARCHAR(20)              NULL,
  [ContactPerson            ] NVARCHAR(40)             NULL,
  [Phone                    ] VARCHAR(20)              NULL,
  [Email                    ] VARCHAR(60)              NULL,
  [ScmURL                   ] VARCHAR(255)             NULL,
  [EdiFlag                  ] BIT                      NULL,
  [OtdTargetRate            ] DECIMAL(5,2)             NULL,
  [PaymentTerms             ] VARCHAR(30)              NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Vendor PRIMARY KEY CLUSTERED ([VendorID])
);
GO

-- ── MD_Equipment  (설비 (MD-08))
CREATE TABLE dbo.MD_Equipment (
  [EquipID                  ] VARCHAR(20)          NOT NULL,
  [EquipName                ] NVARCHAR(50)             NULL,
  [EquipType                ] VARCHAR(16)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [WCID                     ] VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [MakerModel               ] NVARCHAR(60)             NULL,
  [InstallDate              ] DATE                     NULL,
  [TheoreticalCycle         ] DECIMAL(8,2)             NULL,
  [TargetOEE                ] DECIMAL(5,2)             NULL,
  [MoldCompatJSON           ] NVARCHAR(MAX)            NULL,
  [PlcAddress               ] VARCHAR(40)              NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Equipment PRIMARY KEY CLUSTERED ([EquipID])
);
GO

-- ── MD_Mold  (금형 (MD-09))
CREATE TABLE dbo.MD_Mold (
  [MoldID                   ] VARCHAR(20)          NOT NULL,
  [MoldName                 ] NVARCHAR(50)             NULL,
  [CompatItemsJSON          ] NVARCHAR(MAX)            NULL,
  [RatedShots               ] INT                      NULL,
  [CurrentShots             ] INT                      NULL,
  [CavityCount              ] INT                      NULL,
  [Tonnage                  ] INT                      NULL,
  [StorageLoc               ] VARCHAR(20)              NULL,
  [LastMaintDate            ] DATE                     NULL,
  [Status                   ] VARCHAR(10)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Mold PRIMARY KEY CLUSTERED ([MoldID])
);
GO

-- ── MD_PaintFabric  (도료·원단 LOT (MD-10))
CREATE TABLE dbo.MD_PaintFabric (
  [MatLotID                 ] VARCHAR(24)          NOT NULL,
  [MatCode                  ] VARCHAR(20)              NULL,
  [MatName                  ] NVARCHAR(60)             NULL,
  [MatType                  ] VARCHAR(14)              NULL,
  [LotNo                    ] VARCHAR(24)              NULL,
  [SupplierID               ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [UOM                      ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [QtyOnHand                ] DECIMAL(12,3)            NULL,
  [ReceiptDate              ] DATE                     NULL,
  [ExpDate                  ] DATE                     NULL,
  [StorageReq               ] NVARCHAR(40)             NULL,
  [Status                   ] VARCHAR(10)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_PaintFabric PRIMARY KEY CLUSTERED ([MatLotID])
);
GO

-- ── MD_ShipmentDest  (출하처 (MD-11))
CREATE TABLE dbo.MD_ShipmentDest (
  [ShipDestID               ] VARCHAR(20)          NOT NULL,
  [CustomerID               ] VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [DestName                 ] NVARCHAR(80)             NULL,
  [DestType                 ] VARCHAR(10)              NULL,
  [Address                  ] NVARCHAR(200)            NULL,
  [Country                  ] CHAR(3)                  NULL,
  [DeliveryDock             ] VARCHAR(20)              NULL,
  [LeadTimeDays             ] INT                      NULL,
  [DefaultCarrier           ] NVARCHAR(40)             NULL,
  [DeliveryWindow           ] VARCHAR(40)              NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_ShipmentDest PRIMARY KEY CLUSTERED ([ShipDestID])
);
GO

-- ── MD_Customer  (고객사 (MD-12))
CREATE TABLE dbo.MD_Customer (
  [CustomerID               ] VARCHAR(20)          NOT NULL,
  [CustomerCode             ] VARCHAR(20)              NULL,
  [CustomerName             ] NVARCHAR(80)             NULL,
  [CustomerNameEn           ] NVARCHAR(80)             NULL,
  [CustomerType             ] VARCHAR(12)              NULL,
  [BizRegNo                 ] VARCHAR(20)              NULL,
  [Country                  ] CHAR(3)                  NULL,
  [ContactPerson            ] NVARCHAR(40)             NULL,
  [ContactPhone             ] VARCHAR(20)              NULL,
  [ContactEmail             ] VARCHAR(60)              NULL,
  [EDIFlag                  ] BIT                      NULL,
  [CurrencyCode             ] CHAR(3)                  NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Customer PRIMARY KEY CLUSTERED ([CustomerID])
);
GO

-- ── MD_Uom  (단위 (MD-13))
CREATE TABLE dbo.MD_Uom (
  [UOMCode                  ] VARCHAR(10)          NOT NULL,
  [UOMName                  ] NVARCHAR(30)             NULL,
  [UOMCategory              ] VARCHAR(10)              NULL,
  [BaseFlag                 ] BIT                      NULL,
  [BaseUOM                  ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [ConvFactor               ] DECIMAL(18,8)            NULL,
  [DecimalPrec              ] INT                      NULL,
  [Symbol                   ] NVARCHAR(8)              NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Uom PRIMARY KEY CLUSTERED ([UOMCode])
);
GO

-- ── MD_Calendar  (공장 캘린더 (MD-14))
CREATE TABLE dbo.MD_Calendar (
  [PlantID                  ] VARCHAR(10)          NOT NULL,
  [CalendarDate             ] DATE                 NOT NULL,
  [DayType                  ] VARCHAR(10)              NULL,
  [HolidayName              ] NVARCHAR(40)             NULL,
  [ShiftCount               ] INT                      NULL,
  [ShiftPattern             ] VARCHAR(20)              NULL,
  [WorkHours                ] DECIMAL(5,2)             NULL,
  [CalendarYear             ] INT                      NULL,
  [Note                     ] NVARCHAR(120)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Calendar PRIMARY KEY CLUSTERED ([PlantID], [CalendarDate])
);
GO

-- ── MD_Jig  (지그 (MD-15))
CREATE TABLE dbo.MD_Jig (
  [JigID                    ] VARCHAR(20)          NOT NULL,
  [JigName                  ] NVARCHAR(50)             NULL,
  [HangerCount              ] INT                      NULL,
  [CompatItemsJSON          ] NVARCHAR(MAX)            NULL,
  [RatedCycle               ] INT                      NULL,
  [CycleCount               ] INT                      NULL,
  [ReadFailRate             ] DECIMAL(5,2)             NULL,
  [HealthStatus             ] VARCHAR(8)               NULL,
  [LastServiceDate          ] DATE                     NULL,
  [LastUsedTS               ] DATETIME2                NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Jig PRIMARY KEY CLUSTERED ([JigID])
);
GO

-- ── MD_RfidTag  (RFID 태그 (MD-16))
CREATE TABLE dbo.MD_RfidTag (
  [TagID                    ] VARCHAR(24)          NOT NULL,
  [EPC                      ] VARCHAR(32)              NULL,
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [TagRole                  ] VARCHAR(6)               NULL,
  [HeatRating               ] INT                      NULL,
  [MountPos                 ] NVARCHAR(20)             NULL,
  [InstallDate              ] DATE                     NULL,
  [CycleCount               ] INT                      NULL,
  [ReplaceSchedule          ] DATE                     NULL,
  [Status                   ] VARCHAR(10)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_RfidTag PRIMARY KEY CLUSTERED ([TagID])
);
GO

-- ── MD_RalColor  (RAL 컬러 (MD-17))
CREATE TABLE dbo.MD_RalColor (
  [RALCode                  ] VARCHAR(12)          NOT NULL,
  [ColorName                ] NVARCHAR(40)             NULL,
  [HexValue                 ] VARCHAR(7)               NULL,
  [CurrentPowderLot         ] VARCHAR(24)              NULL,  -- FK -> MD_PaintFabric.MatLotID
  [CureTemp                 ] INT                      NULL,
  [CureDuration             ] INT                      NULL,
  [ElectroV                 ] INT                      NULL,
  [ParticleUm               ] DECIMAL(5,1)             NULL,
  [CustomerMapJSON          ] NVARCHAR(MAX)            NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_RalColor PRIMARY KEY CLUSTERED ([RALCode])
);
GO

-- ── MD_Oven  (오븐 (MD-18))
CREATE TABLE dbo.MD_Oven (
  [OvenID                   ] VARCHAR(20)          NOT NULL,
  [OvenName                 ] NVARCHAR(50)             NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ZoneCount                ] INT                      NULL,
  [TargetTemp               ] INT                      NULL,
  [Tolerance                ] INT                      NULL,
  [DwellSec                 ] INT                      NULL,
  [ConveyorSpeed            ] DECIMAL(6,2)             NULL,
  [MaxLoadKg                ] DECIMAL(8,1)             NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Oven PRIMARY KEY CLUSTERED ([OvenID])
);
GO

-- ── MD_RfidReader  (RFID 리더 (MD-19))
CREATE TABLE dbo.MD_RfidReader (
  [ReaderID                 ] VARCHAR(20)          NOT NULL,
  [ReaderName               ] NVARCHAR(50)             NULL,
  [GateLocation             ] VARCHAR(4)               NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [AntennaCount             ] INT                      NULL,
  [PowerDbm                 ] INT                      NULL,
  [PeTriggerFlag            ] BIT                      NULL,
  [WindowMs                 ] INT                      NULL,
  [IpAddress                ] VARCHAR(45)              NULL,
  [FirmwareVer              ] VARCHAR(16)              NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_RfidReader PRIMARY KEY CLUSTERED ([ReaderID])
);
GO

-- ── MD_Line  (생산 라인 (MD-20))
CREATE TABLE dbo.MD_Line (
  [LineID                   ] VARCHAR(20)          NOT NULL,
  [LineName                 ] NVARCHAR(50)             NULL,
  [LineType                 ] VARCHAR(16)              NULL,
  [PlantID                  ] VARCHAR(10)              NULL,
  [DefaultWCID              ] VARCHAR(20)              NULL,  -- FK -> MD_WorkCenter.WCID
  [DailyCap                 ] INT                      NULL,
  [ShiftPattern             ] VARCHAR(20)              NULL,
  [RfidEnabledFlag          ] BIT                      NULL,
  [Status                   ] VARCHAR(10)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Line PRIMARY KEY CLUSTERED ([LineID])
);
GO

-- ── MD_DefectCode  (불량 코드 (MD-21))
CREATE TABLE dbo.MD_DefectCode (
  [DefectCode               ] VARCHAR(16)          NOT NULL,
  [DefectName               ] NVARCHAR(60)             NULL,
  [DefectNameEn             ] NVARCHAR(60)             NULL,
  [ProcessCode              ] VARCHAR(10)              NULL,
  [DefectCategory           ] VARCHAR(14)              NULL,
  [SeverityLevel            ] VARCHAR(8)               NULL,
  [DispositionDefault       ] VARCHAR(10)              NULL,
  [DefaultCauseCode         ] VARCHAR(16)              NULL,  -- FK -> MD_DefectCause.CauseCode
  [ParetoFlag               ] BIT                      NULL,
  [ImageRef                 ] VARCHAR(120)             NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_DefectCode PRIMARY KEY CLUSTERED ([DefectCode])
);
GO

-- ── MD_DefectCause  (불량 원인 (MD-22))
CREATE TABLE dbo.MD_DefectCause (
  [CauseCode                ] VARCHAR(16)          NOT NULL,
  [CauseName                ] NVARCHAR(60)             NULL,
  [CauseCategory            ] VARCHAR(9)               NULL,
  [ParentCauseCode          ] VARCHAR(16)              NULL,  -- FK -> MD_DefectCause.CauseCode
  [ProcessCode              ] VARCHAR(10)              NULL,
  [RootCauseFlag            ] BIT                      NULL,
  [CorrectiveGuide          ] NVARCHAR(200)            NULL,
  [ResponsibleDept          ] NVARCHAR(30)             NULL,
  [SortOrder                ] INT                      NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_DefectCause PRIMARY KEY CLUSTERED ([CauseCode])
);
GO

-- ── MD_PackagingSpec  (포장 사양 (MD-23))
CREATE TABLE dbo.MD_PackagingSpec (
  [PackSpecID               ] VARCHAR(20)          NOT NULL,
  [ItemID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [PackType                 ] VARCHAR(12)              NULL,
  [QtyPerInner              ] INT                      NULL,
  [InnerPerOuter            ] INT                      NULL,
  [OuterPerPallet           ] INT                      NULL,
  [NetWeightKg              ] DECIMAL(8,3)             NULL,
  [GrossWeightKg            ] DECIMAL(8,3)             NULL,
  [DimLxWxH                 ] VARCHAR(30)              NULL,
  [ReturnableFlag           ] BIT                      NULL,
  [LabelTemplateID          ] VARCHAR(20)              NULL,  -- FK -> MD_LabelTemplate.LabelTemplateID
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_PackagingSpec PRIMARY KEY CLUSTERED ([PackSpecID])
);
GO

-- ── MD_LabelTemplate  (라벨 템플릿 (MD-24))
CREATE TABLE dbo.MD_LabelTemplate (
  [LabelTemplateID          ] VARCHAR(20)          NOT NULL,
  [TemplateName             ] NVARCHAR(60)             NULL,
  [LabelType                ] VARCHAR(12)              NULL,
  [PaperSize                ] VARCHAR(12)              NULL,
  [BarcodeType              ] VARCHAR(12)              NULL,
  [LayoutZPL                ] NVARCHAR(MAX)            NULL,
  [FieldMapJSON             ] NVARCHAR(MAX)            NULL,
  [CustomerID               ] VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [Version                  ] INT                      NULL,
  [PrinterModel             ] VARCHAR(30)              NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_LabelTemplate PRIMARY KEY CLUSTERED ([LabelTemplateID])
);
GO

-- ── MD_ReasonCode  (사유 코드 (MD-25))
CREATE TABLE dbo.MD_ReasonCode (
  [ReasonCode               ] VARCHAR(16)          NOT NULL,
  [ReasonName               ] NVARCHAR(60)             NULL,
  [ReasonType               ] VARCHAR(12)              NULL,
  [AppliesToModule          ] VARCHAR(10)              NULL,
  [RequiresComment          ] BIT                      NULL,
  [PlannedFlag              ] BIT                      NULL,
  [DisplayOrder             ] INT                      NULL,
  [Description              ] NVARCHAR(120)            NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_ReasonCode PRIMARY KEY CLUSTERED ([ReasonCode])
);
GO

-- ── MD_CodeGroup  (공통코드 그룹 (MD-26a))
CREATE TABLE dbo.MD_CodeGroup (
  [GroupCode                ] VARCHAR(20)          NOT NULL,
  [GroupName                ] NVARCHAR(60)             NULL,
  [GroupNameEn              ] NVARCHAR(60)             NULL,
  [Description              ] NVARCHAR(200)            NULL,
  [UseFlag                  ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_CodeGroup PRIMARY KEY CLUSTERED ([GroupCode])
);
GO

-- ── MD_CodeItem  (공통코드 항목 (MD-26b))
CREATE TABLE dbo.MD_CodeItem (
  [CodeID                   ] VARCHAR(24)          NOT NULL,
  [GroupCode                ] VARCHAR(20)              NULL,  -- FK -> MD_CodeGroup.GroupCode
  [CodeValue                ] VARCHAR(20)              NULL,
  [CodeName                 ] NVARCHAR(60)             NULL,
  [CodeNameEn               ] NVARCHAR(60)             NULL,
  [ParentCodeID             ] VARCHAR(24)              NULL,  -- FK -> MD_CodeItem.CodeID
  [SortOrder                ] INT                      NULL,
  [Attribute1               ] NVARCHAR(40)             NULL,
  [UseFlag                  ] BIT                      NULL DEFAULT 1,
  [Description              ] NVARCHAR(120)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_CodeItem PRIMARY KEY CLUSTERED ([CodeID])
);
GO

-- ── MD_SparePart  (정비 자재 (MD-27))
CREATE TABLE dbo.MD_SparePart (
  [PartNo                   ] VARCHAR(20)          NOT NULL,
  [PartName                 ] NVARCHAR(60)             NULL,
  [Category                 ] VARCHAR(16)              NULL,
  [CompatEquipJSON          ] NVARCHAR(MAX)            NULL,
  [UnitCost                 ] DECIMAL(12,2)            NULL,
  [UOM                      ] VARCHAR(10)              NULL,  -- FK -> MD_Uom.UOMCode
  [SafetyStock              ] INT                      NULL,
  [ReorderPoint             ] INT                      NULL,
  [ReorderQty               ] INT                      NULL,
  [LeadTimeDays             ] INT                      NULL,
  [SupplierID               ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [StorageLoc               ] VARCHAR(20)              NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_SparePart PRIMARY KEY CLUSTERED ([PartNo])
);
GO

-- ── MD_PmTemplate  (PM 템플릿 (MD-28a))
CREATE TABLE dbo.MD_PmTemplate (
  [PMTemplateID             ] VARCHAR(20)          NOT NULL,
  [TemplateName             ] NVARCHAR(60)             NULL,
  [EquipType                ] VARCHAR(16)              NULL,
  [CycleBasis               ] VARCHAR(10)              NULL,
  [IntervalValue            ] INT                      NULL,
  [IntervalUnit             ] VARCHAR(8)               NULL,
  [StdDurationMin           ] INT                      NULL,
  [SafetyLOTOFlag           ] BIT                      NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_PmTemplate PRIMARY KEY CLUSTERED ([PMTemplateID])
);
GO

-- ── MD_PmTemplateStep  (PM 점검 항목 (MD-28b))
CREATE TABLE dbo.MD_PmTemplateStep (
  [PMStepID                 ] VARCHAR(24)          NOT NULL,
  [PMTemplateID             ] VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [StepSeq                  ] INT                      NULL,
  [StepDescription          ] NVARCHAR(200)            NULL,
  [AcceptanceCriteria       ] NVARCHAR(200)            NULL,
  [RequiredPartNo           ] VARCHAR(20)              NULL,  -- FK -> MD_SparePart.PartNo
  [RequiredQty              ] DECIMAL(10,3)            NULL,
  [StepDurationMin          ] INT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_PmTemplateStep PRIMARY KEY CLUSTERED ([PMStepID])
);
GO

-- ── MD_LineTimePattern  (시간패턴 헤더 (MD-29a))
CREATE TABLE dbo.MD_LineTimePattern (
  [PatternID                ] VARCHAR(20)          NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PatternName              ] NVARCHAR(50)             NULL,
  [DayType                  ] VARCHAR(10)              NULL,
  [ShiftModel               ] VARCHAR(12)              NULL,
  [EffectiveFrom            ] DATE                     NULL,
  [EffectiveTo              ] DATE                     NULL,
  [TotalOperatingMin        ] INT                      NULL,
  [TotalPlannedDownMin      ] INT                      NULL,
  [TimeZone                 ] VARCHAR(20)              NULL,
  [Status                   ] VARCHAR(8)               NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_LineTimePattern PRIMARY KEY CLUSTERED ([PatternID])
);
GO

-- ── MD_LineTimeSegment  (시간 세그먼트 (MD-29b))
CREATE TABLE dbo.MD_LineTimeSegment (
  [SegmentID                ] VARCHAR(24)          NOT NULL,
  [PatternID                ] VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [SeqNo                    ] INT                      NULL,
  [StartMin                 ] SMALLINT                 NULL,
  [EndMin                   ] SMALLINT                 NULL,
  [SegmentState             ] VARCHAR(14)              NULL,
  [ReasonCode               ] VARCHAR(16)              NULL,  -- FK -> MD_ReasonCode.ReasonCode
  [ShiftCode                ] VARCHAR(10)              NULL,
  [Description              ] NVARCHAR(60)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_LineTimeSegment PRIMARY KEY CLUSTERED ([SegmentID])
);
GO

-- ── MD_Location  (로케이션 (보조))
CREATE TABLE dbo.MD_Location (
  [LocationID               ] VARCHAR(20)          NOT NULL,
  [LocationName             ] NVARCHAR(60)             NULL,
  [ZoneCode                 ] VARCHAR(10)              NULL,
  [Aisle                    ] VARCHAR(5)               NULL,
  [Bay                      ] VARCHAR(5)               NULL,
  [Slot                     ] VARCHAR(5)               NULL,
  [Capacity                 ] DECIMAL(10,3)            NULL,
  [LocationType             ] VARCHAR(20)              NULL,
  [PlantID                  ] VARCHAR(10)              NULL,
  [ActiveFlag               ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Location PRIMARY KEY CLUSTERED ([LocationID])
);
GO

-- ── MD_Recipe  (레시피 (보조))
CREATE TABLE dbo.MD_Recipe (
  [RecipeID                 ] VARCHAR(20)          NOT NULL,
  [RecipeName               ] NVARCHAR(60)             NULL,
  [RecipeType               ] VARCHAR(15)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CycleTime                ] INT                      NULL,
  [ParamsJSON               ] NVARCHAR(MAX)            NULL,
  [Version                  ] VARCHAR(10)              NULL,
  [EffectiveDate            ] DATE                     NULL,
  [Status                   ] VARCHAR(10)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MD_Recipe PRIMARY KEY CLUSTERED ([RecipeID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: WH                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── WH_PurchaseOrder  (구매발주 (SCM))
CREATE TABLE dbo.WH_PurchaseOrder (
  [PoID                     ] INT IDENTITY         NOT NULL,
  [PoNumber                 ] VARCHAR(20)              NULL,
  [PoLineNo                 ] INT                      NULL,
  [VendorID                 ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty                 ] DECIMAL(12,3)            NULL,
  [ReceivedQty              ] DECIMAL(12,3)            NULL,
  [UnitCode                 ] VARCHAR(10)              NULL,
  [UnitPrice                ] DECIMAL(14,4)            NULL,
  [Currency                 ] CHAR(3)                  NULL,
  [OrderDate                ] DATE                     NULL,
  [DueDate                  ] DATE                     NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [SapSyncedAt              ] DATETIME2                NULL,
  [CreatedAt                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_PurchaseOrder PRIMARY KEY CLUSTERED ([PoID])
);
GO

-- ── WH_Receiving  (입고 실적)
CREATE TABLE dbo.WH_Receiving (
  [ReceivingID              ] INT IDENTITY         NOT NULL,
  [ReceivingNo              ] VARCHAR(24)              NULL,
  [PoID                     ] INT                      NULL,  -- FK -> WH_PurchaseOrder.PoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VendorID                 ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [ReceivedQty              ] DECIMAL(12,3)            NULL,
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotCode                  ] VARCHAR(40)              NULL,
  [ReceivedAt               ] DATETIME2                NULL,
  [ReceivedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID               ] VARCHAR(20)              NULL,
  [QcStatus                 ] VARCHAR(20)              NULL,
  [LabelPrinted             ] BIT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_Receiving PRIMARY KEY CLUSTERED ([ReceivingID])
);
GO

-- ── WH_Inventory  (현재고)
CREATE TABLE dbo.WH_Inventory (
  [InventoryID              ] INT IDENTITY         NOT NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [OnHandQty                ] DECIMAL(14,3)            NULL,
  [ReservedQty              ] DECIMAL(14,3)            NULL,
  [UnitCost                 ] DECIMAL(14,4)            NULL,
  [LastReceivedAt           ] DATETIME2                NULL,
  [ExpiryDate               ] DATE                     NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_Inventory PRIMARY KEY CLUSTERED ([InventoryID])
);
GO

-- ── WH_InventorySnapshot  (재고 일일 스냅샷)
CREATE TABLE dbo.WH_InventorySnapshot (
  [SnapshotID               ] BIGINT IDENTITY      NOT NULL,
  [SnapshotDate             ] DATE                     NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [OnHandQty                ] DECIMAL(14,3)            NULL,
  [UnitCost                 ] DECIMAL(14,4)            NULL,
  [TotalValue               ] DECIMAL(16,2)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_InventorySnapshot PRIMARY KEY CLUSTERED ([SnapshotID])
);
GO

-- ── WH_InventoryAdjust  (재고 조정)
CREATE TABLE dbo.WH_InventoryAdjust (
  [AdjustID                 ] INT IDENTITY         NOT NULL,
  [AdjustNo                 ] VARCHAR(24)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [QtyBefore                ] DECIMAL(14,3)            NULL,
  [Delta                    ] DECIMAL(14,3)            NULL,
  [QtyAfter                 ] DECIMAL(14,3)            NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [ReasonNote               ] NVARCHAR(500)            NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [RequestedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_InventoryAdjust PRIMARY KEY CLUSTERED ([AdjustID])
);
GO

-- ── WH_ReleaseSchedule  (출고 예정 (WO 수요))
CREATE TABLE dbo.WH_ReleaseSchedule (
  [ReleaseScheduleID        ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [DemandQty                ] DECIMAL(14,3)            NULL,
  [PickedQty                ] DECIMAL(14,3)            NULL,
  [RequiredAt               ] DATETIME2                NULL,
  [Priority                 ] TINYINT                  NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_ReleaseSchedule PRIMARY KEY CLUSTERED ([ReleaseScheduleID])
);
GO

-- ── WH_ReleasePicking  (출고 피킹)
CREATE TABLE dbo.WH_ReleasePicking (
  [PickingID                ] INT IDENTITY         NOT NULL,
  [PickingNo                ] VARCHAR(24)              NULL,
  [ReleaseScheduleID        ] INT                      NULL,  -- FK -> WH_ReleaseSchedule.ReleaseScheduleID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PickedQty                ] DECIMAL(14,3)            NULL,
  [DestLineID               ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PickedAt                 ] DATETIME2                NULL,
  [PickedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID               ] VARCHAR(20)              NULL,
  [FifoOverride             ] BIT                      NULL,
  [OverrideReason           ] NVARCHAR(200)            NULL,
  [OverrideApprover         ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_ReleasePicking PRIMARY KEY CLUSTERED ([PickingID])
);
GO

-- ── WH_TransactionHistory  (입출고 트랜잭션 (append-only))
CREATE TABLE dbo.WH_TransactionHistory (
  [TxnID                    ] BIGINT IDENTITY      NOT NULL,
  [TxnTime                  ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [TxnType                  ] VARCHAR(10)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LocationID               ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [QtyBefore                ] DECIMAL(14,3)            NULL,
  [Delta                    ] DECIMAL(14,3)            NULL,
  [QtyAfter                 ] DECIMAL(14,3)            NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [RefDocType               ] VARCHAR(20)              NULL,
  [RefDocID                 ] INT                      NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApproverID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Note                     ] NVARCHAR(500)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_WH_TransactionHistory PRIMARY KEY CLUSTERED ([TxnID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PP                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── PP_Forecast  (수요예측)
CREATE TABLE dbo.PP_Forecast (
  [ForecastID               ] INT IDENTITY         NOT NULL,
  [ForecastBatch            ] VARCHAR(20)              NULL,
  [CustomerID               ] VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [ForecastMonth            ] DATE                     NULL,
  [ForecastQty              ] DECIMAL(14,3)            NULL,
  [Confidence               ] VARCHAR(10)              NULL,
  [Source                   ] VARCHAR(20)              NULL,
  [ImportedAt               ] DATETIME2                NULL,
  [ImportedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_Forecast PRIMARY KEY CLUSTERED ([ForecastID])
);
GO

-- ── PP_ForecastHistory  (예측 이력)
CREATE TABLE dbo.PP_ForecastHistory (
  [HistoryID                ] BIGINT IDENTITY      NOT NULL,
  [ForecastID               ] INT                      NULL,  -- FK -> PP_Forecast.ForecastID
  [PrevBatch                ] VARCHAR(20)              NULL,
  [PrevQty                  ] DECIMAL(14,3)            NULL,
  [NewQty                   ] DECIMAL(14,3)            NULL,
  [ChangedAt                ] DATETIME2                NULL,
  [ChangedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_ForecastHistory PRIMARY KEY CLUSTERED ([HistoryID])
);
GO

-- ── PP_CustomerOrder  (수주 (SO))
CREATE TABLE dbo.PP_CustomerOrder (
  [SoID                     ] INT IDENTITY         NOT NULL,
  [SoNumber                 ] VARCHAR(20)              NULL,
  [SoLineNo                 ] INT                      NULL,
  [CustomerID               ] VARCHAR(20)              NULL,  -- FK -> MD_Customer.CustomerID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty                 ] DECIMAL(14,3)            NULL,
  [ShippedQty               ] DECIMAL(14,3)            NULL,
  [OrderDate                ] DATE                     NULL,
  [RequestedDeliveryDate    ] DATE                     NULL,
  [PromisedDate             ] DATE                     NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [SapSyncedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_CustomerOrder PRIMARY KEY CLUSTERED ([SoID])
);
GO

-- ── PP_SupplyPlan  (공급계획 헤더)
CREATE TABLE dbo.PP_SupplyPlan (
  [PlanID                   ] INT IDENTITY         NOT NULL,
  [PlanCode                 ] VARCHAR(20)              NULL,
  [PlanPeriod               ] DATE                     NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [ConfirmedAt              ] DATETIME2                NULL,
  [ConfirmedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SapImportBatch           ] VARCHAR(40)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_SupplyPlan PRIMARY KEY CLUSTERED ([PlanID])
);
GO

-- ── PP_SupplyPlanDetail  (공급계획 상세)
CREATE TABLE dbo.PP_SupplyPlanDetail (
  [PlanDetailID             ] INT IDENTITY         NOT NULL,
  [PlanID                   ] INT                      NULL,  -- FK -> PP_SupplyPlan.PlanID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [PlannedQty               ] DECIMAL(14,3)            NULL,
  [FgOnHand                 ] DECIMAL(14,3)            NULL,
  [NetRequirement           ] DECIMAL(14,3)            NULL,
  [DueDate                  ] DATE                     NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_SupplyPlanDetail PRIMARY KEY CLUSTERED ([PlanDetailID])
);
GO

-- ── PP_WorkOrder  (★ 작업지시 (WO))
CREATE TABLE dbo.PP_WorkOrder (
  [WoID                     ] INT IDENTITY         NOT NULL,
  [WoNumber                 ] VARCHAR(20)              NULL,
  [PlanID                   ] INT                      NULL,  -- FK -> PP_SupplyPlan.PlanID
  [SoID                     ] INT                      NULL,  -- FK -> PP_CustomerOrder.SoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderQty                 ] DECIMAL(14,3)            NULL,
  [OpenQty                  ] DECIMAL(14,3)            NULL,
  [CompletedQty             ] DECIMAL(14,3)            NULL,
  [ScrapQty                 ] DECIMAL(14,3)            NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [MoldID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [RecipeID                 ] VARCHAR(20)              NULL,  -- FK -> MD_Recipe.RecipeID
  [BomVersion               ] VARCHAR(10)              NULL,
  [BopVersion               ] VARCHAR(10)              NULL,
  [Routing                  ] CHAR(1)                  NULL,
  [PlannedStart             ] DATETIME2                NULL,
  [PlannedEnd               ] DATETIME2                NULL,
  [ActualStart              ] DATETIME2                NULL,
  [ActualEnd                ] DATETIME2                NULL,
  [DueDate                  ] DATE                     NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [TerminalLock             ] VARCHAR(20)              NULL,
  [Priority                 ] TINYINT                  NULL,
  [ReleasedAt               ] DATETIME2                NULL,
  [ReleasedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_WorkOrder PRIMARY KEY CLUSTERED ([WoID])
);
GO

-- ── PP_WorkOrderRouting  (WO 라우팅 (BOP 스냅샷))
CREATE TABLE dbo.PP_WorkOrderRouting (
  [RoutingLineID            ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StepSeq                  ] TINYINT                  NULL,
  [ProcessCode              ] VARCHAR(10)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [StdCycleSec              ] INT                      NULL,
  [StdYieldPct              ] DECIMAL(5,2)             NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [ActualStart              ] DATETIME2                NULL,
  [ActualEnd                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_WorkOrderRouting PRIMARY KEY CLUSTERED ([RoutingLineID])
);
GO

-- ── PP_MaterialReservation  (WO 자재 예약)
CREATE TABLE dbo.PP_MaterialReservation (
  [ReservationID            ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RequiredQty              ] DECIMAL(14,3)            NULL,
  [ReservedQty              ] DECIMAL(14,3)            NULL,
  [IssuedQty                ] DECIMAL(14,3)            NULL,
  [RequiredAt               ] DATETIME2                NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_MaterialReservation PRIMARY KEY CLUSTERED ([ReservationID])
);
GO

-- ── PP_PurchaseRequest  (구매요청 (MRP 결과))
CREATE TABLE dbo.PP_PurchaseRequest (
  [PrID                     ] INT IDENTITY         NOT NULL,
  [PrNumber                 ] VARCHAR(20)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [VendorID                 ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [RequiredQty              ] DECIMAL(14,3)            NULL,
  [RequiredDate             ] DATE                     NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [Status                   ] VARCHAR(20)              NULL,
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt               ] DATETIME2                NULL,
  [SapPoNumber              ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_PurchaseRequest PRIMARY KEY CLUSTERED ([PrID])
);
GO

-- ── PP_PRSendLog  (PR SAP 송신 로그)
CREATE TABLE dbo.PP_PRSendLog (
  [SendLogID                ] BIGINT IDENTITY      NOT NULL,
  [PrID                     ] INT                      NULL,  -- FK -> PP_PurchaseRequest.PrID
  [AttemptNo                ] TINYINT                  NULL,
  [SentAt                   ] DATETIME2                NULL,
  [Endpoint                 ] VARCHAR(200)             NULL,
  [RequestPayload           ] NVARCHAR(MAX)            NULL,
  [ResponseCode             ] INT                      NULL,
  [ResponsePayload          ] NVARCHAR(MAX)            NULL,
  [Result                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_PRSendLog PRIMARY KEY CLUSTERED ([SendLogID])
);
GO

-- ── PP_MRPLog  (MRP 실행 로그)
CREATE TABLE dbo.PP_MRPLog (
  [MrpRunID                 ] INT IDENTITY         NOT NULL,
  [RunAt                    ] DATETIME2                NULL,
  [RunBy                    ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [HorizonStart             ] DATE                     NULL,
  [HorizonEnd               ] DATE                     NULL,
  [WosConsidered            ] INT                      NULL,
  [PrsCreated               ] INT                      NULL,
  [ShortageCount            ] INT                      NULL,
  [DurationMs               ] INT                      NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_MRPLog PRIMARY KEY CLUSTERED ([MrpRunID])
);
GO

-- ── PP_LineSchedule  (라인 스케줄 (LSB))
CREATE TABLE dbo.PP_LineSchedule (
  [ScheduleID               ] INT IDENTITY         NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ScheduleDate             ] DATE                     NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StartMin                 ] SMALLINT                 NULL,
  [EndMin                   ] SMALLINT                 NULL,
  [PlannedQty               ] DECIMAL(14,3)            NULL,
  [PatternID                ] VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [Status                   ] VARCHAR(20)              NULL,
  [PublishedAt              ] DATETIME2                NULL,
  [PublishedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_LineSchedule PRIMARY KEY CLUSTERED ([ScheduleID])
);
GO

-- ── PP_LineStateLog  (라인 상태 분단위 (ODM))
CREATE TABLE dbo.PP_LineStateLog (
  [StateLogID               ] BIGINT IDENTITY      NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [MinuteTS                 ] DATETIME2                NULL,
  [State                    ] VARCHAR(20)              NULL,
  [PlanState                ] VARCHAR(20)              NULL,
  [RunFlag                  ] BIT                      NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ClassifiedAt             ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_LineStateLog PRIMARY KEY CLUSTERED ([StateLogID])
);
GO

-- ── PP_LineDowntimeLog  (비가동 사유 (DTL))
CREATE TABLE dbo.PP_LineDowntimeLog (
  [DowntimeID               ] INT IDENTITY         NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [StartTS                  ] DATETIME2                NULL,
  [EndTS                    ] DATETIME2                NULL,
  [DurationMin              ] INT                      NULL,
  [ReasonCode               ] VARCHAR(20)              NULL,
  [CauseCode                ] VARCHAR(30)              NULL,
  [Comment                  ] NVARCHAR(500)            NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LoggedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AndonID                  ] INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_LineDowntimeLog PRIMARY KEY CLUSTERED ([DowntimeID])
);
GO

-- ── PP_LineOEE  (OEE 스냅샷)
CREATE TABLE dbo.PP_LineOEE (
  [OeeSnapshotID            ] INT IDENTITY         NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [PeriodDate               ] DATE                     NULL,
  [ShiftCode                ] VARCHAR(10)              NULL,
  [LoadingMin               ] INT                      NULL,
  [PlannedDownMin           ] INT                      NULL,
  [UnplannedDownMin         ] INT                      NULL,
  [OperatingMin             ] INT                      NULL,
  [TotalProducedQty         ] DECIMAL(14,3)            NULL,
  [GoodQty                  ] DECIMAL(14,3)            NULL,
  [Availability             ] DECIMAL(5,4)             NULL,
  [Performance              ] DECIMAL(5,4)             NULL,
  [Quality                  ] DECIMAL(5,4)             NULL,
  [OEE                      ] DECIMAL(5,4)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_LineOEE PRIMARY KEY CLUSTERED ([OeeSnapshotID])
);
GO

-- ── PP_ProductionCalendarOverride  (캘린더 변경)
CREATE TABLE dbo.PP_ProductionCalendarOverride (
  [OverrideID               ] INT IDENTITY         NOT NULL,
  [OverrideDate             ] DATE                     NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [DayType                  ] VARCHAR(20)              NULL,
  [PatternID                ] VARCHAR(20)              NULL,  -- FK -> MD_LineTimePattern.PatternID
  [CapacityFactor           ] DECIMAL(5,2)             NULL,
  [Reason                   ] NVARCHAR(200)            NULL,
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PP_ProductionCalendarOverride PRIMARY KEY CLUSTERED ([OverrideID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PR                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── tbl_Lot  (★ LOT 마스터 (전 모듈 앵커))
CREATE TABLE dbo.tbl_Lot (
  [LotID                    ] INT IDENTITY         NOT NULL,
  [LotCode                  ] VARCHAR(40)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode              ] VARCHAR(10)              NULL,
  [BatchSize                ] DECIMAL(14,3)            NULL,
  [RemainingQty             ] DECIMAL(14,3)            NULL,
  [ParentLotID              ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ProducedAt               ] DATETIME2                NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [QualityFlag              ] VARCHAR(10)              NULL,
  [CurrentLocationID        ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [ExpiryDate               ] DATE                     NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_tbl_Lot PRIMARY KEY CLUSTERED ([LotID])
);
GO

-- ── PR_PopSession  (POP 로그인 세션)
CREATE TABLE dbo.PR_PopSession (
  [SessionID                ] INT IDENTITY         NOT NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [TerminalID               ] VARCHAR(20)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ShiftCode                ] VARCHAR(10)              NULL,
  [AuthMethod               ] VARCHAR(20)              NULL,
  [StartedAt                ] DATETIME2                NULL,
  [ExpiresAt                ] DATETIME2                NULL,
  [LoggedOutAt              ] DATETIME2                NULL,
  [LogoutReason             ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_PopSession PRIMARY KEY CLUSTERED ([SessionID])
);
GO

-- ── PR_PopAuthLog  (POP 인증 감사)
CREATE TABLE dbo.PR_PopAuthLog (
  [AuthLogID                ] BIGINT IDENTITY      NOT NULL,
  [TerminalID               ] VARCHAR(20)              NULL,
  [AttemptedID              ] VARCHAR(50)              NULL,
  [AuthMethod               ] VARCHAR(20)              NULL,
  [Result                   ] VARCHAR(10)              NULL,
  [FailReason               ] VARCHAR(40)              NULL,
  [AttemptedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_PopAuthLog PRIMARY KEY CLUSTERED ([AuthLogID])
);
GO

-- ── PR_WoAcceptance  (WO 수락 (INJ-03))
CREATE TABLE dbo.PR_WoAcceptance (
  [AcceptID                 ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [TerminalID               ] VARCHAR(20)              NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AcceptedAt               ] DATETIME2                NULL,
  [CheckResults             ] NVARCHAR(MAX)            NULL,
  [CheckPassed              ] BIT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_WoAcceptance PRIMARY KEY CLUSTERED ([AcceptID])
);
GO

-- ── PR_ProductionResult  (★ 생산실적 (사이클별))
CREATE TABLE dbo.PR_ProductionResult (
  [ResultID                 ] INT IDENTITY         NOT NULL,
  [EntryNo                  ] VARCHAR(28)              NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode              ] VARCHAR(10)              NULL,
  [GoodQty                  ] INT                      NULL,
  [CycleSec                 ] INT                      NULL,
  [MoldID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [FabricRollID             ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [FabricConsumedM          ] DECIMAL(8,3)             NULL,
  [BondTempAvg              ] DECIMAL(5,1)             NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SessionID                ] INT                      NULL,  -- FK -> PR_PopSession.SessionID
  [DefectFlag               ] BIT                      NULL,
  [ReviewFlag               ] BIT                      NULL,
  [EntryAt                  ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_ProductionResult PRIMARY KEY CLUSTERED ([ResultID])
);
GO

-- ── PR_DefectDetail  (불량 상세)
CREATE TABLE dbo.PR_DefectDetail (
  [DefectID                 ] INT IDENTITY         NOT NULL,
  [ResultID                 ] INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ProcessCode              ] VARCHAR(10)              NULL,
  [DefectCode               ] VARCHAR(16)              NULL,  -- FK -> MD_DefectCode.DefectCode
  [Qty                      ] INT                      NULL,
  [SeqNos                   ] NVARCHAR(MAX)            NULL,
  [ReasonNote               ] NVARCHAR(500)            NULL,
  [PhotoUrl                 ] VARCHAR(300)             NULL,
  [CorrectiveAction         ] NVARCHAR(500)            NULL,
  [Disposition              ] VARCHAR(20)              NULL,
  [DetectedAt               ] DATETIME2                NULL,
  [RegisteredBy             ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_DefectDetail PRIMARY KEY CLUSTERED ([DefectID])
);
GO

-- ── PR_DefectAutoLink  (불량 자동 원인)
CREATE TABLE dbo.PR_DefectAutoLink (
  [LinkID                   ] INT IDENTITY         NOT NULL,
  [DefectID                 ] INT                      NULL,  -- FK -> PR_DefectDetail.DefectID
  [LinkType                 ] VARCHAR(30)              NULL,
  [RefDocType               ] VARCHAR(20)              NULL,
  [RefDocID                 ] INT                      NULL,
  [ConfidenceScore          ] DECIMAL(4,3)             NULL,
  [LinkedAt                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_DefectAutoLink PRIMARY KEY CLUSTERED ([LinkID])
);
GO

-- ── PR_CycleAnomalyLog  (CT 이탈 로그)
CREATE TABLE dbo.PR_CycleAnomalyLog (
  [AnomalyID                ] INT IDENTITY         NOT NULL,
  [ResultID                 ] INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [ExpectedCt               ] INT                      NULL,
  [ActualCt                 ] INT                      NULL,
  [DeviationPct             ] DECIMAL(6,2)             NULL,
  [DetectedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_CycleAnomalyLog PRIMARY KEY CLUSTERED ([AnomalyID])
);
GO

-- ── PR_MoldChange  (금형 교체 (INJ-06))
CREATE TABLE dbo.PR_MoldChange (
  [MoldChangeID             ] INT IDENTITY         NOT NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [OldMoldID                ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [NewMoldID                ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [OldMoldFinalShots        ] INT                      NULL,
  [NewMoldStartShots        ] INT                      NULL,
  [Reason                   ] VARCHAR(20)              NULL,
  [MntWoID                  ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [DowntimeMin              ] INT                      NULL,
  [StartedAt                ] DATETIME2                NULL,
  [CompletedAt              ] DATETIME2                NULL,
  [ChangedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_MoldChange PRIMARY KEY CLUSTERED ([MoldChangeID])
);
GO

-- ── PR_ShotCount  (금형 쇼트 이력)
CREATE TABLE dbo.PR_ShotCount (
  [ShotCountID              ] BIGINT IDENTITY      NOT NULL,
  [MoldID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [RecordDate               ] DATE                     NULL,
  [ShiftCode                ] VARCHAR(10)              NULL,
  [ShotsAdded               ] INT                      NULL,
  [CumulativeShots          ] INT                      NULL,
  [RatedShots               ] INT                      NULL,
  [RecordedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_ShotCount PRIMARY KEY CLUSTERED ([ShotCountID])
);
GO

-- ── PR_EquipStatusLog  (설비 상태 로그 (PLC))
CREATE TABLE dbo.PR_EquipStatusLog (
  [EquipStatusLogID         ] BIGINT IDENTITY      NOT NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [Status                   ] VARCHAR(20)              NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [StartedAt                ] DATETIME2                NULL,
  [DurationSec              ] INT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_EquipStatusLog PRIMARY KEY CLUSTERED ([EquipStatusLogID])
);
GO

-- ── PR_AndonCall  (★ 안돈 호출 (5년))
CREATE TABLE dbo.PR_AndonCall (
  [AndonID                  ] INT IDENTITY         NOT NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [TriggerSource            ] VARCHAR(20)              NULL,
  [RuleID                   ] VARCHAR(20)              NULL,
  [Severity                 ] VARCHAR(10)              NULL,
  [TriggeredAt              ] DATETIME2                NULL,
  [AckedBy                  ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AckedAt                  ] DATETIME2                NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [CorrectiveAction         ] NVARCHAR(500)            NULL,
  [ResumedAt                ] DATETIME2                NULL,
  [DowntimeSec              ] INT                      NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_AndonCall PRIMARY KEY CLUSTERED ([AndonID])
);
GO

-- ── PR_AndonPush  (안돈 송신 로그)
CREATE TABLE dbo.PR_AndonPush (
  [PushID                   ] BIGINT IDENTITY      NOT NULL,
  [AndonID                  ] INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [Recipient                ] VARCHAR(100)             NULL,
  [Channel                  ] VARCHAR(20)              NULL,
  [SentAt                   ] DATETIME2                NULL,
  [DeliveredAt              ] DATETIME2                NULL,
  [Result                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_AndonPush PRIMARY KEY CLUSTERED ([PushID])
);
GO

-- ── PR_PlcInterlock  (PLC 인터록)
CREATE TABLE dbo.PR_PlcInterlock (
  [InterlockID              ] INT IDENTITY         NOT NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LockedAt                 ] DATETIME2                NULL,
  [UnlockedAt               ] DATETIME2                NULL,
  [LockReason               ] VARCHAR(40)              NULL,
  [AndonID                  ] INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_PlcInterlock PRIMARY KEY CLUSTERED ([InterlockID])
);
GO

-- ── PR_ShiftHandover  (교대 인수인계)
CREATE TABLE dbo.PR_ShiftHandover (
  [HandoverID               ] INT IDENTITY         NOT NULL,
  [HandoverDate             ] DATE                     NULL,
  [ShiftCode                ] VARCHAR(10)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ProcessCode              ] VARCHAR(10)              NULL,
  [SummaryJson              ] NVARCHAR(MAX)            NULL,
  [PdfUrl                   ] VARCHAR(300)             NULL,
  [SignedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReceivedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SignedAt                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_ShiftHandover PRIMARY KEY CLUSTERED ([HandoverID])
);
GO

-- ── PR_DashTileCache  (POP 대시 캐시)
CREATE TABLE dbo.PR_DashTileCache (
  [LineID                   ] VARCHAR(20)          NOT NULL,
  [TileID                   ] VARCHAR(30)          NOT NULL,
  [Value                    ] NVARCHAR(200)            NULL,
  [UpdatedAt                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [TtlSec                   ] INT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_DashTileCache PRIMARY KEY CLUSTERED ([LineID], [TileID])
);
GO

-- ── PR_DefectRateCache  (불량률 캐시)
CREATE TABLE dbo.PR_DefectRateCache (
  [WoID                     ] INT                  NOT NULL,
  [TotalGood                ] INT                      NULL,
  [TotalDefect              ] INT                      NULL,
  [RatePct                  ] DECIMAL(6,3)             NULL,
  [UpdatedAt                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PR_DefectRateCache PRIMARY KEY CLUSTERED ([WoID])
);
GO

-- ── PR_FabricIssue  (IMG 원단 투입)
CREATE TABLE dbo.PR_FabricIssue (
  [FabricIssueID            ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [FabricRollLotID          ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ColorCode                ] VARCHAR(10)              NULL,
  [MountedAt                ] DATETIME2                NULL,
  [DismountedAt             ] DATETIME2                NULL,
  [InitialRemainingM        ] DECIMAL(8,3)             NULL,
  [FinalRemainingM          ] DECIMAL(8,3)             NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SessionID                ] INT                      NULL,  -- FK -> PR_PopSession.SessionID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_FabricIssue PRIMARY KEY CLUSTERED ([FabricIssueID])
);
GO

-- ── PR_FabricIssueAttempt  (원단 시도 감사)
CREATE TABLE dbo.PR_FabricIssueAttempt (
  [AttemptID                ] BIGINT IDENTITY      NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ScannedRollLotID         ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ScannedColor             ] VARCHAR(10)              NULL,
  [ExpectedColor            ] VARCHAR(10)              NULL,
  [Result                   ] VARCHAR(10)              NULL,
  [AttemptedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AttemptedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_FabricIssueAttempt PRIMARY KEY CLUSTERED ([AttemptID])
);
GO

-- ── PR_FabricDeductionLog  (원단 차감 (7년))
CREATE TABLE dbo.PR_FabricDeductionLog (
  [DeductionID              ] BIGINT IDENTITY      NOT NULL,
  [FabricRollLotID          ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ResultID                 ] INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [ConsumedM                ] DECIMAL(8,3)             NULL,
  [BeforeM                  ] DECIMAL(8,3)             NULL,
  [AfterM                   ] DECIMAL(8,3)             NULL,
  [DeductedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_FabricDeductionLog PRIMARY KEY CLUSTERED ([DeductionID])
);
GO

-- ── PR_BondSetup  (IMG 본드 설정)
CREATE TABLE dbo.PR_BondSetup (
  [BondSetupID              ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [RecipeID                 ] VARCHAR(20)              NULL,  -- FK -> MD_Recipe.RecipeID
  [PressureSp               ] DECIMAL(6,2)             NULL,
  [TempSp                   ] DECIMAL(5,1)             NULL,
  [HoldSecSp                ] INT                      NULL,
  [TensionSp                ] DECIMAL(6,2)             NULL,
  [LoadedAt                 ] DATETIME2                NULL,
  [LoadedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_BondSetup PRIMARY KEY CLUSTERED ([BondSetupID])
);
GO

-- ── PR_BondCycleLog  (본드 사이클 PLC)
CREATE TABLE dbo.PR_BondCycleLog (
  [BondCycleID              ] BIGINT IDENTITY      NOT NULL,
  [ResultID                 ] INT                      NULL,  -- FK -> PR_ProductionResult.ResultID
  [BondSetupID              ] INT                      NULL,  -- FK -> PR_BondSetup.BondSetupID
  [PressureAvg              ] DECIMAL(6,2)             NULL,
  [TempAvg                  ] DECIMAL(5,1)             NULL,
  [HoldActualSec            ] INT                      NULL,
  [TensionAvg               ] DECIMAL(6,2)             NULL,
  [WithinSpec               ] BIT                      NULL,
  [SampledAt                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_BondCycleLog PRIMARY KEY CLUSTERED ([BondCycleID])
);
GO

-- ── PR_BondSetupAudit  (본드 변경 감사 (7년))
CREATE TABLE dbo.PR_BondSetupAudit (
  [AuditID                  ] BIGINT IDENTITY      NOT NULL,
  [BondSetupID              ] INT                      NULL,  -- FK -> PR_BondSetup.BondSetupID
  [FieldName                ] VARCHAR(40)              NULL,
  [OldValue                 ] NVARCHAR(100)            NULL,
  [NewValue                 ] NVARCHAR(100)            NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [ChangedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChangedAt                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PR_BondSetupAudit PRIMARY KEY CLUSTERED ([AuditID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: PNT                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── PNT_DailyPlan  (일일 계획 (PNT-01))
CREATE TABLE dbo.PNT_DailyPlan (
  [PlanID                   ] INT IDENTITY         NOT NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [PlanDate                 ] DATE                     NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor                 ] VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [TargetQty                ] INT                      NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [OvenID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [StartTime                ] TIME                     NULL,
  [JigsRequired             ] INT                      NULL,
  [LotsRequired             ] INT                      NULL,
  [ReadyFlag                ] BIT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_DailyPlan PRIMARY KEY CLUSTERED ([PlanID])
);
GO

-- ── PNT_VirtualLot  (★ 가상 LOT (PNT-02))
CREATE TABLE dbo.PNT_VirtualLot (
  [VirtualLotID             ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PlanID                   ] INT                      NULL,  -- FK -> PNT_DailyPlan.PlanID
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor                 ] VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [TargetQty                ] INT                      NULL,
  [LoadedQty                ] INT                      NULL,
  [ConfirmedQty             ] INT                      NULL,
  [DefectQty                ] INT                      NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [EnhancedInspection       ] BIT                      NULL,
  [IssuedAt                 ] DATETIME2                NULL,
  [IssuedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [BindAt                   ] DATETIME2                NULL,
  [BindReason               ] VARCHAR(40)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_VirtualLot PRIMARY KEY CLUSTERED ([VirtualLotID])
);
GO

-- ── PNT_JigBindingLog  (지그 바인딩 이력)
CREATE TABLE dbo.PNT_JigBindingLog (
  [BindingLogID             ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [BoundAt                  ] DATETIME2                NULL,
  [UnboundAt                ] DATETIME2                NULL,
  [Reason                   ] VARCHAR(40)              NULL,
  [ActorID                  ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_JigBindingLog PRIMARY KEY CLUSTERED ([BindingLogID])
);
GO

-- ── PNT_SeqAllocator  (LotID 채번 락)
CREATE TABLE dbo.PNT_SeqAllocator (
  [PlanDate                 ] DATE                 NOT NULL,
  [LineID                   ] VARCHAR(20)          NOT NULL,
  [NextSeq                  ] INT                      NULL,
  [UpdatedAt                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PNT_SeqAllocator PRIMARY KEY CLUSTERED ([PlanDate], [LineID])
);
GO

-- ── PNT_JigLoad  (지그 로딩 (PNT-03))
CREATE TABLE dbo.PNT_JigLoad (
  [LoadID                   ] INT IDENTITY         NOT NULL,
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LoadedQty                ] INT                      NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PdaScanAt                ] DATETIME2                NULL,
  [R1ReadAt                 ] DATETIME2                NULL,
  [MatchStatus              ] VARCHAR(20)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_JigLoad PRIMARY KEY CLUSTERED ([LoadID])
);
GO

-- ── PNT_LineEvent  (★ RFID 통과 (R1/R2/R3, 5년))
CREATE TABLE dbo.PNT_LineEvent (
  [EventID                  ] BIGINT IDENTITY      NOT NULL,
  [TagID                    ] VARCHAR(24)              NULL,  -- FK -> MD_RfidTag.TagID
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ReaderID                 ] VARCHAR(20)              NULL,  -- FK -> MD_RfidReader.ReaderID
  [AntennaPort              ] TINYINT                  NULL,
  [TagRole                  ] VARCHAR(10)              NULL,
  [EventTS                  ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [Rssi                     ] SMALLINT                 NULL,
  [ReadCount                ] INT                      NULL,
  [TriggerType              ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_LineEvent PRIMARY KEY CLUSTERED ([EventID])
);
GO

-- ── PNT_TagFailureLog  (태그 실패 로그)
CREATE TABLE dbo.PNT_TagFailureLog (
  [FailureID                ] INT IDENTITY         NOT NULL,
  [TagID                    ] VARCHAR(24)              NULL,  -- FK -> MD_RfidTag.TagID
  [ReaderID                 ] VARCHAR(20)              NULL,  -- FK -> MD_RfidReader.ReaderID
  [FailedAt                 ] DATETIME2                NULL,
  [FailType                 ] VARCHAR(20)              NULL,
  [FallbackAction           ] VARCHAR(30)              NULL,
  [ResolvedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_TagFailureLog PRIMARY KEY CLUSTERED ([FailureID])
);
GO

-- ── PNT_OvenLog  (오븐 체류 (PNT-05))
CREATE TABLE dbo.PNT_OvenLog (
  [OvenLogID                ] INT IDENTITY         NOT NULL,
  [OvenID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [EntryTS                  ] DATETIME2                NULL,
  [ExitTS                   ] DATETIME2                NULL,
  [DwellSec                 ] INT                      NULL,
  [TempCurve                ] NVARCHAR(MAX)            NULL,
  [MinTemp                  ] DECIMAL(5,1)             NULL,
  [MaxTemp                  ] DECIMAL(5,1)             NULL,
  [AvgTemp                  ] DECIMAL(5,1)             NULL,
  [WithinSpec               ] BIT                      NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_OvenLog PRIMARY KEY CLUSTERED ([OvenLogID])
);
GO

-- ── PNT_OvenTempSample  (오븐 5초 샘플 (5년))
CREATE TABLE dbo.PNT_OvenTempSample (
  [SampleID                 ] BIGINT IDENTITY      NOT NULL,
  [OvenID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [ZoneID                   ] TINYINT                  NULL,
  [TempC                    ] DECIMAL(5,1)             NULL,
  [SampledAt                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_OvenTempSample PRIMARY KEY CLUSTERED ([SampleID])
);
GO

-- ── PNT_OvenDeviationLog  (오븐 온도 이탈)
CREATE TABLE dbo.PNT_OvenDeviationLog (
  [DeviationID              ] INT IDENTITY         NOT NULL,
  [OvenID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [StartTS                  ] DATETIME2                NULL,
  [EndTS                    ] DATETIME2                NULL,
  [MaxDelta                 ] DECIMAL(5,1)             NULL,
  [AffectedLots             ] NVARCHAR(MAX)            NULL,
  [MntWoID                  ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [AndonID                  ] INT                      NULL,  -- FK -> PR_AndonCall.AndonID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_OvenDeviationLog PRIMARY KEY CLUSTERED ([DeviationID])
);
GO

-- ── PNT_OvenSpikeLog  (오븐 단일 스파이크)
CREATE TABLE dbo.PNT_OvenSpikeLog (
  [SpikeID                  ] INT IDENTITY         NOT NULL,
  [OvenID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Oven.OvenID
  [DetectedAt               ] DATETIME2                NULL,
  [TempC                    ] DECIMAL(5,1)             NULL,
  [Delta                    ] DECIMAL(5,1)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_OvenSpikeLog PRIMARY KEY CLUSTERED ([SpikeID])
);
GO

-- ── PNT_JigUnload  (지그 언로딩 (PNT-06))
CREATE TABLE dbo.PNT_JigUnload (
  [UnloadID                 ] INT IDENTITY         NOT NULL,
  [JigID                    ] VARCHAR(20)              NULL,  -- FK -> MD_Jig.JigID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ConfirmedQty             ] INT                      NULL,
  [ExpectedQty              ] INT                      NULL,
  [ShortReason              ] VARCHAR(30)              NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [R3ReadAt                 ] DATETIME2                NULL,
  [ConfirmedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_JigUnload PRIMARY KEY CLUSTERED ([UnloadID])
);
GO

-- ── PNT_PartLossLog  (부품 손실 로그)
CREATE TABLE dbo.PNT_PartLossLog (
  [LossID                   ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LossQty                  ] INT                      NULL,
  [ReasonCode               ] VARCHAR(30)              NULL,
  [ReasonNote               ] NVARCHAR(300)            NULL,
  [LoggedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [LoggedAt                 ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_PartLossLog PRIMARY KEY CLUSTERED ([LossID])
);
GO

-- ── PNT_StationStatsCache  (라인보드 캐시)
CREATE TABLE dbo.PNT_StationStatsCache (
  [StationID                ] VARCHAR(20)          NOT NULL,
  [ActiveCount              ] INT                      NULL,
  [AvgDwellSec              ] INT                      NULL,
  [BottleneckFlag           ] BIT                      NULL,
  [UpdatedAt                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  CONSTRAINT PK_PNT_StationStatsCache PRIMARY KEY CLUSTERED ([StationID])
);
GO

-- ── PNT_LotLabel  (LOT 라벨 (PNT-07))
CREATE TABLE dbo.PNT_LotLabel (
  [LabelID                  ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [SeqNo                    ] INT                      NULL,
  [TotalQty                 ] INT                      NULL,
  [PrintedAt                ] DATETIME2                NULL,
  [PrintedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [AppliedAt                ] DATETIME2                NULL,
  [AppliedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_LotLabel PRIMARY KEY CLUSTERED ([LabelID])
);
GO

-- ── PNT_LabelPrintJob  (라벨 프린트 잡)
CREATE TABLE dbo.PNT_LabelPrintJob (
  [JobID                    ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [PrinterID                ] VARCHAR(20)              NULL,
  [Zpl                      ] NVARCHAR(MAX)            NULL,
  [SubmittedAt              ] DATETIME2                NULL,
  [CompletedAt              ] DATETIME2                NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [FailReason               ] VARCHAR(200)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_LabelPrintJob PRIMARY KEY CLUSTERED ([JobID])
);
GO

-- ── PNT_LabelScanLog  (라벨 스캔 이력)
CREATE TABLE dbo.PNT_LabelScanLog (
  [ScanID                   ] BIGINT IDENTITY      NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [LabelID                  ] INT                      NULL,  -- FK -> PNT_LotLabel.LabelID
  [ScannedSeq               ] INT                      NULL,
  [Position                 ] VARCHAR(10)              NULL,
  [ScannedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ScannedAt                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_LabelScanLog PRIMARY KEY CLUSTERED ([ScanID])
);
GO

-- ── PNT_ShiftReport  (교대 보고서 헤더)
CREATE TABLE dbo.PNT_ShiftReport (
  [ReportID                 ] INT IDENTITY         NOT NULL,
  [ShiftDate                ] DATE                     NULL,
  [ShiftType                ] VARCHAR(10)              NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [LoadedQty                ] INT                      NULL,
  [ConfirmedQty             ] INT                      NULL,
  [DefectQty                ] INT                      NULL,
  [OvenDeviations           ] INT                      NULL,
  [Fallbacks                ] INT                      NULL,
  [SpareTagsUsed            ] INT                      NULL,
  [JigSwaps                 ] INT                      NULL,
  [YieldPct                 ] DECIMAL(5,2)             NULL,
  [CompiledAt               ] DATETIME2                NULL,
  [SignedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [SignedAt                 ] DATETIME2                NULL,
  [PdfUrl                   ] VARCHAR(300)             NULL,
  [Version                  ] TINYINT                  NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_ShiftReport PRIMARY KEY CLUSTERED ([ReportID])
);
GO

-- ── PNT_ShiftReportLineItem  (교대 WO 명세)
CREATE TABLE dbo.PNT_ShiftReportLineItem (
  [LineItemID               ] INT IDENTITY         NOT NULL,
  [ReportID                 ] INT                      NULL,  -- FK -> PNT_ShiftReport.ReportID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [RalColor                 ] VARCHAR(12)              NULL,  -- FK -> MD_RalColor.RALCode
  [PlanQty                  ] INT                      NULL,
  [LoadedQty                ] INT                      NULL,
  [ConfirmedQty             ] INT                      NULL,
  [DefectQty                ] INT                      NULL,
  [YieldPct                 ] DECIMAL(5,2)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_ShiftReportLineItem PRIMARY KEY CLUSTERED ([LineItemID])
);
GO

-- ── PNT_ShiftReportAudit  (교대 수정 감사 (7년))
CREATE TABLE dbo.PNT_ShiftReportAudit (
  [AuditID                  ] INT IDENTITY         NOT NULL,
  [ReportID                 ] INT                      NULL,  -- FK -> PNT_ShiftReport.ReportID
  [ChangedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChangedAt                ] DATETIME2                NULL,
  [FieldName                ] VARCHAR(40)              NULL,
  [OldValue                 ] NVARCHAR(200)            NULL,
  [NewValue                 ] NVARCHAR(200)            NULL,
  [Reason                   ] NVARCHAR(300)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_ShiftReportAudit PRIMARY KEY CLUSTERED ([AuditID])
);
GO

-- ── PNT_DailyReport  (일일 합산 보고서)
CREATE TABLE dbo.PNT_DailyReport (
  [DailyID                  ] INT IDENTITY         NOT NULL,
  [ReportDate               ] DATE                     NULL,
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [TotalConfirmed           ] INT                      NULL,
  [TotalDefect              ] INT                      NULL,
  [DailyYieldPct            ] DECIMAL(5,2)             NULL,
  [TwoShiftRollupJson       ] NVARCHAR(MAX)            NULL,
  [GeneratedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_DailyReport PRIMARY KEY CLUSTERED ([DailyID])
);
GO

-- ── PNT_QcQueue  (QC 인계 대기열)
CREATE TABLE dbo.PNT_QcQueue (
  [QueueID                  ] INT IDENTITY         NOT NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [EnqueuedAt               ] DATETIME2                NULL,
  [EnhancedFlag             ] BIT                      NULL,
  [SlaDueAt                 ] DATETIME2                NULL,
  [Status                   ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_PNT_QcQueue PRIMARY KEY CLUSTERED ([QueueID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: QC                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── QC_Inspection  (★ 검사 (IQC/IPQC/FQC))
CREATE TABLE dbo.QC_Inspection (
  [InspectionID             ] INT IDENTITY         NOT NULL,
  [InspectionNo             ] VARCHAR(24)              NULL,
  [InspectionType           ] VARCHAR(15)              NULL,
  [PoID                     ] INT                      NULL,  -- FK -> WH_PurchaseOrder.PoID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [StdID                    ] INT                      NULL,  -- FK -> QC_InspectionStd.StdID
  [CustomerCode             ] VARCHAR(20)              NULL,
  [Mode                     ] VARCHAR(15)              NULL,
  [EnhanceReason            ] VARCHAR(60)              NULL,
  [SampleSize               ] INT                      NULL,
  [BatchQty                 ] DECIMAL(12,3)            NULL,
  [CumulativeGood           ] INT                      NULL,
  [DefectQtyTotal           ] INT                      NULL,
  [Verdict                  ] VARCHAR(15)              NULL,
  [CriticalFlag             ] BIT                      NULL,
  [CorrectiveAction         ] NVARCHAR(500)            NULL,
  [ResultJSON               ] NVARCHAR(MAX)            NULL,
  [NcrID                    ] INT                      NULL,  -- FK -> QC_NCR.NcrID
  [HoldID                   ] INT                      NULL,  -- FK -> QC_Hold.HoldID
  [InspectorID              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApproverID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ResumeBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [InsStartTS               ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [InsEndTS                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_Inspection PRIMARY KEY CLUSTERED ([InspectionID])
);
GO

-- ── QC_InspectionItem  (검사 항목별 측정값)
CREATE TABLE dbo.QC_InspectionItem (
  [InspectionItemID         ] INT IDENTITY         NOT NULL,
  [InspectionID             ] INT                      NULL,  -- FK -> QC_Inspection.InspectionID
  [ItemSeq                  ] INT                      NULL,
  [ItemName                 ] NVARCHAR(100)        NOT NULL,
  [Standard                 ] NVARCHAR(100)            NULL,
  [Measured                 ] NVARCHAR(100)            NULL,
  [Result                   ] VARCHAR(10)              NULL,
  [PhotoURL                 ] VARCHAR(255)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_InspectionItem PRIMARY KEY CLUSTERED ([InspectionItemID])
);
GO

-- ── QC_InspectionStd  (검사 기준서 (버전))
CREATE TABLE dbo.QC_InspectionStd (
  [StdID                    ] INT IDENTITY         NOT NULL,
  [StdCode                  ] VARCHAR(30)              NULL,
  [VerNo                    ] VARCHAR(8)               NULL,
  [StdName                  ] NVARCHAR(120)            NULL,
  [InsType                  ] VARCHAR(15)              NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CustomerCode             ] VARCHAR(20)              NULL,
  [Mode                     ] VARCHAR(15)              NULL,
  [AQLLevel                 ] DECIMAL(3,2)             NULL,
  [SampleInterval           ] INT                      NULL,
  [InspItemsJSON            ] NVARCHAR(MAX)            NULL,
  [KPITargetsJSON           ] NVARCHAR(MAX)            NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [EffectiveDate            ] DATE                     NULL,
  [DraftedBy                ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CapaLinkID               ] INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_InspectionStd PRIMARY KEY CLUSTERED ([StdID])
);
GO

-- ── QC_NCR  (★ 부적합 보고서)
CREATE TABLE dbo.QC_NCR (
  [NcrID                    ] INT IDENTITY         NOT NULL,
  [NcrNumber                ] VARCHAR(24)              NULL,
  [SourceType               ] VARCHAR(20)              NULL,
  [SourceID                 ] VARCHAR(24)              NULL,
  [InspectionID             ] INT                      NULL,  -- FK -> QC_Inspection.InspectionID
  [Severity                 ] VARCHAR(10)              NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [AffectedQty              ] DECIMAL(12,3)            NULL,
  [CustomerCode             ] VARCHAR(20)              NULL,
  [DefectsJSON              ] NVARCHAR(MAX)            NULL,
  [Cause4M                  ] VARCHAR(15)              NULL,
  [Disposition              ] VARCHAR(15)              NULL,
  [HoldID                   ] INT                      NULL,  -- FK -> QC_Hold.HoldID
  [CapaID                   ] INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [Status                   ] VARCHAR(15)              NULL,
  [ReportedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReportedAt               ] DATETIME2                NULL,
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClosedAt                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_NCR PRIMARY KEY CLUSTERED ([NcrID])
);
GO

-- ── QC_NCR_Action  (NCR 처리 이력)
CREATE TABLE dbo.QC_NCR_Action (
  [ActionID                 ] INT IDENTITY         NOT NULL,
  [NcrID                    ] INT                      NULL,  -- FK -> QC_NCR.NcrID
  [ActionType               ] VARCHAR(20)              NULL,
  [ActionRefID              ] VARCHAR(24)              NULL,
  [ActionNote               ] NVARCHAR(500)            NULL,
  [ActionTS                 ] DATETIME2                NULL,
  [ActionBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_NCR_Action PRIMARY KEY CLUSTERED ([ActionID])
);
GO

-- ── QC_Hold  (보류/격리)
CREATE TABLE dbo.QC_Hold (
  [HoldID                   ] INT IDENTITY         NOT NULL,
  [HoldNumber               ] VARCHAR(24)              NULL,
  [SourceNcrID              ] INT                      NULL,  -- FK -> QC_NCR.NcrID
  [Severity                 ] VARCHAR(10)              NULL,
  [AffectedType             ] VARCHAR(15)              NULL,
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [FgStockID                ] INT                      NULL,  -- FK -> FG_Stock.StockID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [HeldQty                  ] DECIMAL(12,3)            NULL,
  [PhysicalLocation         ] VARCHAR(20)              NULL,
  [LabelPrintedTS           ] DATETIME2                NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [HeldBy                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [HeldAt                   ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_Hold PRIMARY KEY CLUSTERED ([HoldID])
);
GO

-- ── QC_HoldRelease  (보류 해제 이력)
CREATE TABLE dbo.QC_HoldRelease (
  [ReleaseID                ] INT IDENTITY         NOT NULL,
  [HoldID                   ] INT                      NULL,  -- FK -> QC_Hold.HoldID
  [EventType                ] VARCHAR(15)              NULL,
  [ReleaseAction            ] VARCHAR(15)              NULL,
  [ReleaseReason            ] NVARCHAR(500)            NULL,
  [PlantMgrApprovalID       ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ActorPINHash             ] VARCHAR(255)             NULL,
  [ReleasedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReleasedAt               ] DATETIME2                NULL,
  [Note                     ] NVARCHAR(500)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_HoldRelease PRIMARY KEY CLUSTERED ([ReleaseID])
);
GO

-- ── QC_CAPA  (★ 시정·예방 조치)
CREATE TABLE dbo.QC_CAPA (
  [CapaID                   ] INT IDENTITY         NOT NULL,
  [CapaNumber               ] VARCHAR(24)              NULL,
  [Type                     ] VARCHAR(15)              NULL,
  [TriggerType              ] VARCHAR(20)              NULL,
  [LinkedNcrIDs             ] NVARCHAR(MAX)            NULL,
  [Phase                    ] VARCHAR(10)              NULL,
  [Status                   ] VARCHAR(25)              NULL,
  [FiveWhyJSON              ] NVARCHAR(MAX)            NULL,
  [RootCause                ] NVARCHAR(1000)           NULL,
  [Cause4M                  ] VARCHAR(15)              NULL,
  [ActionsJSON              ] NVARCHAR(MAX)            NULL,
  [EffectivenessJSON        ] NVARCHAR(MAX)            NULL,
  [OwnerID                  ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [QcManagerID              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CustomerImpact           ] VARCHAR(10)              NULL,
  [CustomerNotified         ] BIT                      NULL,
  [OpenedAt                 ] DATETIME2                NULL,
  [DueDate                  ] DATE                     NULL,
  [ClosedAt                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_CAPA PRIMARY KEY CLUSTERED ([CapaID])
);
GO

-- ── QC_CAPA_Action  (CAPA 단계 이력)
CREATE TABLE dbo.QC_CAPA_Action (
  [CapaActionID             ] INT IDENTITY         NOT NULL,
  [CapaID                   ] INT                      NULL,  -- FK -> QC_CAPA.CapaID
  [ActionType               ] VARCHAR(20)              NULL,
  [CheckDay                 ] INT                      NULL,
  [Description              ] NVARCHAR(500)            NULL,
  [Metric                   ] NVARCHAR(60)             NULL,
  [TargetValue              ] NVARCHAR(60)             NULL,
  [ActualValue              ] NVARCHAR(60)             NULL,
  [Verdict                  ] VARCHAR(10)              NULL,
  [OwnerID                  ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [DueDate                  ] DATE                     NULL,
  [CompletedAt              ] DATETIME2                NULL,
  [EvidenceURL              ] VARCHAR(255)             NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_CAPA_Action PRIMARY KEY CLUSTERED ([CapaActionID])
);
GO

-- ── QC_Disposition  (처분 결정)
CREATE TABLE dbo.QC_Disposition (
  [DispositionID            ] INT IDENTITY         NOT NULL,
  [NcrID                    ] INT                      NULL,  -- FK -> QC_NCR.NcrID
  [HoldID                   ] INT                      NULL,  -- FK -> QC_Hold.HoldID
  [DispositionAction        ] VARCHAR(15)              NULL,
  [DispositionQty           ] DECIMAL(12,3)            NULL,
  [Reason                   ] NVARCHAR(500)            NULL,
  [CustomerApprovalURL      ] VARCHAR(255)             NULL,
  [DownstreamRefType        ] VARCHAR(15)              NULL,
  [DownstreamRefID          ] VARCHAR(24)              NULL,
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_QC_Disposition PRIMARY KEY CLUSTERED ([DispositionID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: FG                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── FG_Stock  (★ 완제품 재고)
CREATE TABLE dbo.FG_Stock (
  [StockID                  ] INT IDENTITY         NOT NULL,
  [StockNumber              ] VARCHAR(24)              NULL,
  [FgTriggerID              ] INT                      NULL,
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [CustomerCode             ] VARCHAR(20)              NULL,
  [Qty                      ] DECIMAL(12,3)            NULL,
  [Location                 ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [Status                   ] VARCHAR(15)              NULL,
  [HoldFlag                 ] BIT                      NULL,
  [HoldID                   ] INT                      NULL,  -- FK -> QC_Hold.HoldID
  [ReservationID            ] INT                      NULL,
  [StockTS                  ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_Stock PRIMARY KEY CLUSTERED ([StockID])
);
GO

-- ── FG_PutAway  (완제품 적치 (FG-01))
CREATE TABLE dbo.FG_PutAway (
  [PutAwayID                ] INT IDENTITY         NOT NULL,
  [StockID                  ] INT                      NULL,  -- FK -> FG_Stock.StockID
  [WoID                     ] INT                      NULL,  -- FK -> PP_WorkOrder.WoID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [Qty                      ] DECIMAL(12,3)            NULL,
  [SuggestedLoc             ] VARCHAR(20)              NULL,
  [ActualLoc                ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [LocOverrideReason        ] VARCHAR(60)              NULL,
  [PalletCount              ] INT                      NULL,
  [PalletQty                ] INT                      NULL,
  [LabelPrintedTS           ] DATETIME2                NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status                   ] VARCHAR(15)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_PutAway PRIMARY KEY CLUSTERED ([PutAwayID])
);
GO

-- ── FG_ShipmentOrder  (★ 출하 지시 헤더)
CREATE TABLE dbo.FG_ShipmentOrder (
  [ShipmentOrderID          ] INT IDENTITY         NOT NULL,
  [ShipOrderNumber          ] VARCHAR(24)              NULL,
  [CustomerCode             ] VARCHAR(20)              NULL,
  [CustomerPO               ] VARCHAR(40)              NULL,
  [Source                   ] VARCHAR(10)              NULL,
  [ShipDate                 ] DATE                     NULL,
  [CarrierCode              ] VARCHAR(20)              NULL,
  [DestPlant                ] VARCHAR(30)              NULL,
  [DestDock                 ] VARCHAR(30)              NULL,
  [ReceiverName             ] VARCHAR(50)              NULL,
  [ReceiverPhone            ] VARCHAR(30)              NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [PickslipID               ] VARCHAR(24)              NULL,
  [OTDFlag                  ] VARCHAR(10)              NULL,
  [ConfirmedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ConfirmedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_ShipmentOrder PRIMARY KEY CLUSTERED ([ShipmentOrderID])
);
GO

-- ── FG_ShipmentOrderLine  (출하 라인 (SO×LOT))
CREATE TABLE dbo.FG_ShipmentOrderLine (
  [ShipmentOrderLineID      ] INT IDENTITY         NOT NULL,
  [ShipmentOrderID          ] INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [LineSeq                  ] INT                      NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [OrderedQty               ] DECIMAL(12,3)            NULL,
  [AllocatedQty             ] DECIMAL(12,3)            NULL,
  [StockID                  ] INT                      NULL,  -- FK -> FG_Stock.StockID
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [Location                 ] VARCHAR(20)              NULL,  -- FK -> MD_Location.LocationID
  [ReservationStatus        ] VARCHAR(15)              NULL,
  [ReservedAt               ] DATETIME2                NULL,
  [ReleasedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_ShipmentOrderLine PRIMARY KEY CLUSTERED ([ShipmentOrderLineID])
);
GO

-- ── FG_PickingFifo  (FIFO 피킹 세션)
CREATE TABLE dbo.FG_PickingFifo (
  [PickID                   ] INT IDENTITY         NOT NULL,
  [PickNumber               ] VARCHAR(24)              NULL,
  [PickslipID               ] VARCHAR(24)              NULL,
  [ShipmentOrderID          ] INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [PickerID                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [StartTS                  ] DATETIME2                NULL,
  [EndTS                    ] DATETIME2                NULL,
  [PicksJSON                ] NVARCHAR(MAX)            NULL,
  [FifoViolations           ] INT                      NULL,
  [OverrideCount            ] INT                      NULL,
  [OverrideApprovedBy       ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PartialPicksJSON         ] NVARCHAR(MAX)            NULL,
  [PickedQty                ] DECIMAL(12,3)            NULL,
  [OrderedQty               ] DECIMAL(12,3)            NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_PickingFifo PRIMARY KEY CLUSTERED ([PickID])
);
GO

-- ── FG_LoadingConfirm  (상차 (Chain-of-Custody))
CREATE TABLE dbo.FG_LoadingConfirm (
  [LoadingID                ] INT IDENTITY         NOT NULL,
  [LoadingNumber            ] VARCHAR(24)              NULL,
  [ShipmentOrderID          ] INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [PickID                   ] INT                      NULL,  -- FK -> FG_PickingFifo.PickID
  [LicensePlate             ] VARCHAR(20)              NULL,
  [CarrierCode              ] VARCHAR(20)              NULL,
  [DriverID                 ] VARCHAR(30)              NULL,
  [DriverName               ] VARCHAR(50)              NULL,
  [DriverPhone              ] VARCHAR(30)              NULL,
  [DockNo                   ] VARCHAR(10)              NULL,
  [ArrivalTS                ] DATETIME2                NULL,
  [DepartureTS              ] DATETIME2                NULL,
  [PalletsLoadedJSON        ] NVARCHAR(MAX)            NULL,
  [SealNo                   ] VARCHAR(20)              NULL,
  [DriverSigURL             ] VARCHAR(255)             NULL,
  [DriverPhotoURL           ] VARCHAR(255)             NULL,
  [GPSCoord                 ] VARCHAR(30)              NULL,
  [OTDStatus                ] VARCHAR(10)              NULL,
  [OperatorID               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ConfirmedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_LoadingConfirm PRIMARY KEY CLUSTERED ([LoadingID])
);
GO

-- ── FG_DeliveryNote  (★ 거래명세서 / BOL)
CREATE TABLE dbo.FG_DeliveryNote (
  [DeliveryNoteID           ] INT IDENTITY         NOT NULL,
  [DnNumber                 ] VARCHAR(30)              NULL,
  [ShipmentOrderID          ] INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [LoadingID                ] INT                      NULL,  -- FK -> FG_LoadingConfirm.LoadingID
  [CustomerCode             ] VARCHAR(20)              NULL,
  [FormatTemplate           ] VARCHAR(40)              NULL,
  [Revision                 ] INT                      NULL,
  [RevisionReason           ] NVARCHAR(200)            NULL,
  [IssuedAt                 ] DATETIME2                NULL,
  [IssuedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [PdfUrl                   ] VARCHAR(255)             NULL,
  [EdiMsgID                 ] VARCHAR(40)              NULL,
  [EdiStatus                ] VARCHAR(15)              NULL,
  [CustomerAckTS            ] DATETIME2                NULL,
  [LinesJSON                ] NVARCHAR(MAX)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_DeliveryNote PRIMARY KEY CLUSTERED ([DeliveryNoteID])
);
GO

-- ── FG_DayEndClose  (일 마감 스냅샷)
CREATE TABLE dbo.FG_DayEndClose (
  [DayEndCloseID            ] INT IDENTITY         NOT NULL,
  [CloseNumber              ] VARCHAR(24)              NULL,
  [CloseDate                ] DATE                     NULL,
  [ClosedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClosedAt                 ] DATETIME2                NULL,
  [CloseMode                ] VARCHAR(20)              NULL,
  [ChecklistJSON            ] NVARCHAR(MAX)            NULL,
  [KpiJSON                  ] NVARCHAR(MAX)            NULL,
  [PendingItemsJSON         ] NVARCHAR(MAX)            NULL,
  [SnapshotURL              ] VARCHAR(255)             NULL,
  [ErpFeedTS                ] DATETIME2                NULL,
  [ErpFeedStatus            ] VARCHAR(15)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_DayEndClose PRIMARY KEY CLUSTERED ([DayEndCloseID])
);
GO

-- ── FG_CustomerReturn  (고객 반품 (RMA))
CREATE TABLE dbo.FG_CustomerReturn (
  [ReturnID                 ] INT IDENTITY         NOT NULL,
  [ReturnNumber             ] VARCHAR(24)              NULL,
  [RMANo                    ] VARCHAR(40)              NULL,
  [CustomerCode             ] VARCHAR(20)              NULL,
  [CustomerClaimID          ] VARCHAR(24)              NULL,
  [OriginalShipmentOrderID  ] INT                      NULL,  -- FK -> FG_ShipmentOrder.ShipmentOrderID
  [OriginalDeliveryNoteID   ] INT                      NULL,  -- FK -> FG_DeliveryNote.DeliveryNoteID
  [ReturnReason             ] VARCHAR(60)              NULL,
  [ItemsJSON                ] NVARCHAR(MAX)            NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [ReceivedAt               ] DATETIME2                NULL,
  [ReceivedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [NcrID                    ] INT                      NULL,  -- FK -> QC_NCR.NcrID
  [CapaTriggered            ] BIT                      NULL,
  [ClosedAt                 ] DATETIME2                NULL,
  [ClosedBy                 ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_CustomerReturn PRIMARY KEY CLUSTERED ([ReturnID])
);
GO

-- ── FG_ReturnDisposition  (반품 처분)
CREATE TABLE dbo.FG_ReturnDisposition (
  [ReturnDispositionID      ] INT IDENTITY         NOT NULL,
  [ReturnID                 ] INT                      NULL,  -- FK -> FG_CustomerReturn.ReturnID
  [PalletSeq                ] INT                      NULL,
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [LotID                    ] INT                      NULL,  -- FK -> tbl_Lot.LotID
  [Qty                      ] DECIMAL(12,3)            NULL,
  [Action                   ] VARCHAR(15)              NULL,
  [Reason                   ] NVARCHAR(500)            NULL,
  [DownstreamRefID          ] VARCHAR(24)              NULL,
  [ApprovedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ApprovedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_FG_ReturnDisposition PRIMARY KEY CLUSTERED ([ReturnDispositionID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: MNT                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── MNT_EquipmentStatus  (설비 실시간 상태)
CREATE TABLE dbo.MNT_EquipmentStatus (
  [EquipStatusID            ] INT IDENTITY         NOT NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [Status                   ] VARCHAR(10)              NULL,
  [TodayOEE                 ] DECIMAL(5,2)             NULL,
  [RuntimeHours             ] DECIMAL(10,1)            NULL,
  [CycleCount               ] BIGINT                   NULL,
  [NextPMDate               ] DATE                     NULL,
  [MountedMoldID            ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [LastFailureID            ] INT                      NULL,  -- FK -> MNT_FailureRegister.FailureID
  [OpenWoID                 ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [PLCConnTS                ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_EquipmentStatus PRIMARY KEY CLUSTERED ([EquipStatusID])
);
GO

-- ── MNT_FailureRegister  (★ 고장 등록)
CREATE TABLE dbo.MNT_FailureRegister (
  [FailureID                ] INT IDENTITY         NOT NULL,
  [FailureNumber            ] VARCHAR(24)              NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [FailureType              ] VARCHAR(15)              NULL,
  [Symptom                  ] NVARCHAR(500)            NULL,
  [Urgency                  ] VARCHAR(10)              NULL,
  [PhotoURLs                ] NVARCHAR(MAX)            NULL,
  [Source                   ] VARCHAR(15)              NULL,
  [AndonRefID               ] VARCHAR(24)              NULL,
  [WorkOrderID              ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [DowntimeID               ] INT                      NULL,  -- FK -> PP_LineDowntimeLog.DowntimeID
  [Status                   ] VARCHAR(15)              NULL,
  [ReportedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ReportedAt               ] DATETIME2                NULL,
  [ResolvedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_FailureRegister PRIMARY KEY CLUSTERED ([FailureID])
);
GO

-- ── MNT_FailureAction  (고장 조치 이력)
CREATE TABLE dbo.MNT_FailureAction (
  [FailureActionID          ] INT IDENTITY         NOT NULL,
  [FailureID                ] INT                      NULL,  -- FK -> MNT_FailureRegister.FailureID
  [ActionType               ] VARCHAR(20)              NULL,
  [Description              ] NVARCHAR(500)            NULL,
  [EvidenceURL              ] VARCHAR(255)             NULL,
  [TechnicianID             ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ActionAt                 ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_FailureAction PRIMARY KEY CLUSTERED ([FailureActionID])
);
GO

-- ── MNT_OEELog  (OEE 측정 (설비×시각))
CREATE TABLE dbo.MNT_OEELog (
  [OEELogID                 ] INT IDENTITY         NOT NULL,
  [OEERecordNumber          ] VARCHAR(40)              NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [LineID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Line.LineID
  [AggLevel                 ] VARCHAR(10)              NULL,
  [AggDate                  ] DATE                     NULL,
  [ShiftCode                ] VARCHAR(10)              NULL,
  [PlannedTimeMin           ] INT                      NULL,
  [DowntimeMin              ] INT                      NULL,
  [Availability             ] DECIMAL(5,2)             NULL,
  [Performance              ] DECIMAL(5,2)             NULL,
  [Quality                  ] DECIMAL(5,2)             NULL,
  [OEE                      ] DECIMAL(5,2)             NULL,
  [GoodQty                  ] DECIMAL(12,3)            NULL,
  [TotalQty                 ] DECIMAL(12,3)            NULL,
  [LossBreakdownJSON        ] NVARCHAR(MAX)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_OEELog PRIMARY KEY CLUSTERED ([OEELogID])
);
GO

-- ── MNT_PMSchedule  (PM 일정)
CREATE TABLE dbo.MNT_PMSchedule (
  [PMScheduleID             ] INT IDENTITY         NOT NULL,
  [PMPlanNumber             ] VARCHAR(30)              NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [PMType                   ] VARCHAR(60)              NULL,
  [CycleBasis               ] VARCHAR(10)              NULL,
  [CycleValue               ] INT                      NULL,
  [LastPMDate               ] DATE                     NULL,
  [NextDueDate              ] DATE                     NULL,
  [ChecklistID              ] VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [AssignedTechID           ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Status                   ] VARCHAR(10)              NULL,
  [ActiveWoID               ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_PMSchedule PRIMARY KEY CLUSTERED ([PMScheduleID])
);
GO

-- ── MNT_PMExecution  (PM 실행 이력)
CREATE TABLE dbo.MNT_PMExecution (
  [PMExecutionID            ] INT IDENTITY         NOT NULL,
  [PMScheduleID             ] INT                      NULL,  -- FK -> MNT_PMSchedule.PMScheduleID
  [WorkOrderID              ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [CompletedAt              ] DATETIME2                NULL,
  [TechnicianID             ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Result                   ] VARCHAR(15)              NULL,
  [ResultNote               ] NVARCHAR(500)            NULL,
  [ChecklistResultsJSON     ] NVARCHAR(MAX)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_PMExecution PRIMARY KEY CLUSTERED ([PMExecutionID])
);
GO

-- ── MNT_WorkOrder  (★ 정비 WO)
CREATE TABLE dbo.MNT_WorkOrder (
  [WorkOrderID              ] INT IDENTITY         NOT NULL,
  [WoNumber                 ] VARCHAR(28)              NULL,
  [WoType                   ] VARCHAR(15)              NULL,
  [EquipID                  ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [Priority                 ] VARCHAR(10)              NULL,
  [SourceType               ] VARCHAR(15)              NULL,
  [SourceRefID              ] VARCHAR(24)              NULL,
  [AssignedTechID           ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ChecklistID              ] VARCHAR(20)              NULL,  -- FK -> MD_PmTemplate.PMTemplateID
  [ChecklistResultsJSON     ] NVARCHAR(MAX)            NULL,
  [PartsUsedJSON            ] NVARCHAR(MAX)            NULL,
  [LaborMinutes             ] INT                      NULL,
  [ActionDesc               ] NVARCHAR(1000)           NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [IssuedAt                 ] DATETIME2                NULL,
  [StartedAt                ] DATETIME2                NULL,
  [CompletedAt              ] DATETIME2                NULL,
  [ClosedAt                 ] DATETIME2                NULL,
  [DowntimeID               ] INT                      NULL,  -- FK -> PP_LineDowntimeLog.DowntimeID
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_WorkOrder PRIMARY KEY CLUSTERED ([WorkOrderID])
);
GO

-- ── MNT_WorkOrderTask  (정비 WO 작업 항목)
CREATE TABLE dbo.MNT_WorkOrderTask (
  [WorkOrderTaskID          ] INT IDENTITY         NOT NULL,
  [WorkOrderID              ] INT                      NULL,  -- FK -> MNT_WorkOrder.WorkOrderID
  [TaskSeq                  ] INT                      NULL,
  [TaskName                 ] NVARCHAR(120)            NULL,
  [TaskType                 ] VARCHAR(20)              NULL,
  [Result                   ] VARCHAR(10)              NULL,
  [Note                     ] NVARCHAR(500)            NULL,
  [EvidenceURL              ] VARCHAR(255)             NULL,
  [CompletedBy              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CompletedAt              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_WorkOrderTask PRIMARY KEY CLUSTERED ([WorkOrderTaskID])
);
GO

-- ── MNT_SparePartsTxn  (정비 자재 입출고)
CREATE TABLE dbo.MNT_SparePartsTxn (
  [SparePartsTxnID          ] INT IDENTITY         NOT NULL,
  [PartNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_SparePart.PartNo
  [PartName                 ] NVARCHAR(60)             NULL,
  [Category                 ] VARCHAR(15)              NULL,
  [MoveType                 ] VARCHAR(10)              NULL,
  [Qty                      ] INT                      NULL,
  [BalanceAfter             ] INT                      NULL,
  [UnitPrice                ] DECIMAL(12,2)            NULL,
  [StorageLoc               ] VARCHAR(20)              NULL,
  [RefType                  ] VARCHAR(15)              NULL,
  [RefID                    ] VARCHAR(24)              NULL,
  [SupplierCode             ] VARCHAR(20)              NULL,  -- FK -> MD_Vendor.VendorID
  [Note                     ] NVARCHAR(500)            NULL,
  [TxnAt                    ] DATETIME2                NULL,
  [ActorID                  ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_SparePartsTxn PRIMARY KEY CLUSTERED ([SparePartsTxnID])
);
GO

-- ── MNT_MoldShotCount  (금형 쇼트 운영 카운터)
CREATE TABLE dbo.MNT_MoldShotCount (
  [MoldShotCountID          ] INT IDENTITY         NOT NULL,
  [MoldID                   ] VARCHAR(20)              NULL,  -- FK -> MD_Mold.MoldID
  [ItemNo                   ] VARCHAR(20)              NULL,  -- FK -> MD_Item.ItemNo
  [CurrentShots             ] INT                      NULL,
  [LifetimeShots            ] INT                      NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [MountedEquipID           ] VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID
  [StorageLoc               ] VARCHAR(20)              NULL,
  [ThresholdLevel           ] VARCHAR(15)              NULL,
  [LastRefurbishTS          ] DATETIME2                NULL,
  [RefurbishCount           ] INT                      NULL,
  [HistoryJSON              ] NVARCHAR(MAX)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_MNT_MoldShotCount PRIMARY KEY CLUSTERED ([MoldShotCountID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: SYS                                                          ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── SYS_UserProfile  (사용자 추가 속성)
CREATE TABLE dbo.SYS_UserProfile (
  [UserProfileID            ] INT IDENTITY         NOT NULL,
  [UserID                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [EmployeeNo               ] VARCHAR(20)              NULL,
  [EmployeeName             ] NVARCHAR(50)             NULL,
  [Department               ] VARCHAR(30)              NULL,
  [Plant                    ] VARCHAR(20)              NULL,
  [DefaultShift             ] VARCHAR(10)              NULL,
  [AssignedLines            ] NVARCHAR(MAX)            NULL,
  [AccountStatus            ] VARCHAR(10)              NULL,
  [FailedLoginCount         ] INT                      NULL,
  [LastLoginTS              ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_UserProfile PRIMARY KEY CLUSTERED ([UserProfileID])
);
GO

-- ── SYS_RolePermission  (★ RBAC 매트릭스)
CREATE TABLE dbo.SYS_RolePermission (
  [RolePermissionID         ] INT IDENTITY         NOT NULL,
  [RoleID                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetRoles.Id
  [RoleName                 ] VARCHAR(40)              NULL,
  [ModuleCode               ] VARCHAR(10)              NULL,
  [ScreenCode               ] VARCHAR(20)              NULL,
  [PermissionLevel          ] VARCHAR(10)              NULL,
  [IsSystemRole             ] BIT                      NULL,
  [EffectiveTS              ] DATETIME2                NULL,
  [ModifiedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_RolePermission PRIMARY KEY CLUSTERED ([RolePermissionID])
);
GO

-- ── SYS_AuditLog  (★ 감사 로그 (append-only))
CREATE TABLE dbo.SYS_AuditLog (
  [LogID                    ] BIGINT IDENTITY      NOT NULL,
  [EventTS                  ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ActorUserID              ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ModuleCode               ] VARCHAR(10)              NULL,
  [ScreenCode               ] VARCHAR(20)              NULL,
  [ActionType               ] VARCHAR(15)              NULL,
  [TargetEntity             ] VARCHAR(40)              NULL,
  [TargetID                 ] VARCHAR(40)              NULL,
  [BeforeValueJSON          ] NVARCHAR(MAX)            NULL,
  [AfterValueJSON           ] NVARCHAR(MAX)            NULL,
  [IPAddress                ] VARCHAR(45)              NULL,
  [Result                   ] VARCHAR(10)              NULL,
  [Note                     ] NVARCHAR(500)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_AuditLog PRIMARY KEY CLUSTERED ([LogID])
);
GO

-- ── SYS_NotificationRule  (알림 규칙)
CREATE TABLE dbo.SYS_NotificationRule (
  [NotificationRuleID       ] INT IDENTITY         NOT NULL,
  [EventTypeCode            ] VARCHAR(20)              NULL,
  [EventName                ] NVARCHAR(60)             NULL,
  [SourceModule             ] VARCHAR(10)              NULL,
  [TriggerCondition         ] NVARCHAR(500)            NULL,
  [IsEnabled                ] BIT                      NULL DEFAULT 1,
  [ChannelsJSON             ] NVARCHAR(200)            NULL,
  [RecipientRolesJSON       ] NVARCHAR(500)            NULL,
  [ModifiedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_NotificationRule PRIMARY KEY CLUSTERED ([NotificationRuleID])
);
GO

-- ── SYS_NotificationChannel  (사용자별 알림 채널)
CREATE TABLE dbo.SYS_NotificationChannel (
  [NotificationChannelID    ] INT IDENTITY         NOT NULL,
  [UserID                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Channel                  ] VARCHAR(10)              NULL,
  [Address                  ] VARCHAR(255)             NULL,
  [IsEnabled                ] BIT                      NULL DEFAULT 1,
  [QuietHoursStart          ] TIME                     NULL,
  [QuietHoursEnd            ] TIME                     NULL,
  [VerifiedAt               ] DATETIME2                NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_NotificationChannel PRIMARY KEY CLUSTERED ([NotificationChannelID])
);
GO

-- ── SYS_NotificationHistory  (알림 발송 이력)
CREATE TABLE dbo.SYS_NotificationHistory (
  [NotificationHistoryID    ] BIGINT IDENTITY      NOT NULL,
  [NotificationRuleID       ] INT                      NULL,  -- FK -> SYS_NotificationRule.NotificationRuleID
  [EventTypeCode            ] VARCHAR(20)              NULL,
  [RecipientUserID          ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [Channel                  ] VARCHAR(10)              NULL,
  [Address                  ] VARCHAR(255)             NULL,
  [Subject                  ] NVARCHAR(200)            NULL,
  [Body                     ] NVARCHAR(MAX)            NULL,
  [SourceRefType            ] VARCHAR(20)              NULL,
  [SourceRefID              ] VARCHAR(40)              NULL,
  [Status                   ] VARCHAR(15)              NULL,
  [RetryCount               ] INT                      NULL,
  [SentAt                   ] DATETIME2                NULL,
  [ReadAt                   ] DATETIME2                NULL,
  [ErrorMsg                 ] NVARCHAR(500)            NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_NotificationHistory PRIMARY KEY CLUSTERED ([NotificationHistoryID])
);
GO

-- ── SYS_Config  (환경설정 (Key-Value))
CREATE TABLE dbo.SYS_Config (
  [ConfigID                 ] INT IDENTITY         NOT NULL,
  [ConfigKey                ] VARCHAR(60)              NULL,
  [ConfigType               ] VARCHAR(15)              NULL,
  [Category                 ] VARCHAR(30)              NULL,
  [ConfigValue              ] NVARCHAR(500)            NULL,
  [CodeName                 ] NVARCHAR(80)             NULL,
  [Unit                     ] VARCHAR(10)              NULL,
  [UsedByModulesJSON        ] NVARCHAR(500)            NULL,
  [SortOrder                ] INT                      NULL,
  [IsActive                 ] BIT                      NULL DEFAULT 1,
  [ModifiedBy               ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_Config PRIMARY KEY CLUSTERED ([ConfigID])
);
GO

-- ── SYS_InterfaceMonitor  (인터페이스 상태)
CREATE TABLE dbo.SYS_InterfaceMonitor (
  [InterfaceMonitorID       ] INT IDENTITY         NOT NULL,
  [InterfaceCode            ] VARCHAR(20)              NULL,
  [InterfaceName            ] NVARCHAR(60)             NULL,
  [Direction                ] VARCHAR(15)              NULL,
  [Endpoint                 ] VARCHAR(255)             NULL,
  [Protocol                 ] VARCHAR(15)              NULL,
  [ConnStatus               ] VARCHAR(10)              NULL,
  [LastSyncTS               ] DATETIME2                NULL,
  [MaxGapMinutes            ] INT                      NULL,
  [LastRecordCount          ] INT                      NULL,
  [RetryCount               ] INT                      NULL,
  [LastErrorMsg             ] NVARCHAR(1000)           NULL,
  [IsEnabled                ] BIT                      NULL DEFAULT 1,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_InterfaceMonitor PRIMARY KEY CLUSTERED ([InterfaceMonitorID])
);
GO

-- ── SYS_FactoryCalendar  (공장 캘린더 (교대 인스턴스))
CREATE TABLE dbo.SYS_FactoryCalendar (
  [FactoryCalendarID        ] INT IDENTITY         NOT NULL,
  [CalendarDate             ] DATE                     NULL,
  [DayType                  ] VARCHAR(10)              NULL,
  [HolidayName              ] NVARCHAR(40)             NULL,
  [ShiftCount               ] INT                      NULL,
  [ShiftCode                ] VARCHAR(10)              NULL,
  [StartTime                ] TIME                     NULL,
  [EndTime                  ] TIME                     NULL,
  [BreakMinutes             ] INT                      NULL,
  [NetWorkHours             ] DECIMAL(4,1)             NULL,
  [CalendarYear             ] INT                      NULL,
  [Plant                    ] VARCHAR(20)              NULL,
  [CreatedBy                ] VARCHAR(50)          NOT NULL,
  [CreatedTS                ] DATETIME2                NULL DEFAULT SYSDATETIME(),
  [ModifiedTS               ] DATETIME2                NULL,
  CONSTRAINT PK_SYS_FactoryCalendar PRIMARY KEY CLUSTERED ([FactoryCalendarID])
);
GO

-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Module: ID                                                           ║
-- ╚══════════════════════════════════════════════════════════════════════╝

-- ── AspNetUsers  (★ 사용자)
CREATE TABLE dbo.AspNetUsers (
  [Id                       ] NVARCHAR(450)        NOT NULL,
  [UserName                 ] NVARCHAR(256)            NULL,
  [NormalizedUserName       ] NVARCHAR(256)            NULL,
  [Email                    ] NVARCHAR(256)            NULL,
  [NormalizedEmail          ] NVARCHAR(256)            NULL,
  [EmailConfirmed           ] BIT                      NULL,
  [PasswordHash             ] NVARCHAR(MAX)            NULL,
  [SecurityStamp            ] NVARCHAR(MAX)            NULL,
  [ConcurrencyStamp         ] NVARCHAR(MAX)            NULL,
  [PhoneNumber              ] NVARCHAR(MAX)            NULL,
  [PhoneNumberConfirmed     ] BIT                      NULL,
  [TwoFactorEnabled         ] BIT                      NULL,
  [LockoutEnd               ] DATETIMEOFFSET           NULL,
  [LockoutEnabled           ] BIT                      NULL,
  [AccessFailedCount        ] INT                      NULL,
  CONSTRAINT PK_AspNetUsers PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetRoles  (역할)
CREATE TABLE dbo.AspNetRoles (
  [Id                       ] NVARCHAR(450)        NOT NULL,
  [Name                     ] NVARCHAR(256)            NULL,
  [NormalizedName           ] NVARCHAR(256)            NULL,
  [ConcurrencyStamp         ] NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetRoles PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetUserRoles  (사용자×역할)
CREATE TABLE dbo.AspNetUserRoles (
  [UserId                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [RoleId                   ] NVARCHAR(450)            NULL  -- FK -> AspNetRoles.Id
);
GO

-- ── AspNetUserClaims  (사용자 클레임)
CREATE TABLE dbo.AspNetUserClaims (
  [Id                       ] INT IDENTITY         NOT NULL,
  [UserId                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [ClaimType                ] NVARCHAR(MAX)            NULL,
  [ClaimValue               ] NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetUserClaims PRIMARY KEY CLUSTERED ([Id])
);
GO

-- ── AspNetUserLogins  (외부 로그인)
CREATE TABLE dbo.AspNetUserLogins (
  [LoginProvider            ] NVARCHAR(450)        NOT NULL,
  [ProviderKey              ] NVARCHAR(450)        NOT NULL,
  [ProviderDisplayName      ] NVARCHAR(MAX)            NULL,
  [UserId                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  CONSTRAINT PK_AspNetUserLogins PRIMARY KEY CLUSTERED ([LoginProvider], [ProviderKey])
);
GO

-- ── AspNetUserTokens  (사용자 토큰)
CREATE TABLE dbo.AspNetUserTokens (
  [UserId                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetUsers.Id
  [LoginProvider            ] NVARCHAR(450)        NOT NULL,
  [Name                     ] NVARCHAR(450)        NOT NULL,
  [Value                    ] NVARCHAR(MAX)            NULL,
  CONSTRAINT PK_AspNetUserTokens PRIMARY KEY CLUSTERED ([LoginProvider], [Name])
);
GO

-- ── AspNetRoleClaims  (역할 클레임)
CREATE TABLE dbo.AspNetRoleClaims (
  [Id                       ] INT IDENTITY         NOT NULL,
  [RoleId                   ] NVARCHAR(450)            NULL,  -- FK -> AspNetRoles.Id
  [ClaimType                ] NVARCHAR(MAX)            NULL,
  [ClaimValue               ] NVARCHAR(MAX)            NULL,
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
  ('CUS-SAV',    'SAV',  N'SEYON E-HWA Detroit',   'SAV (Detroit Plant)',     'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-GEO',    'GEO',  N'SEYON E-HWA Birmingham','GEO (Birmingham Plant)',  'PLANT', 'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-FORD',   'FORD', N'Ford Motor Company',    'Ford Motor Company',      'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-GM',     'GM',   N'General Motors',        'General Motors',          'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-STEL',   'STEL', N'Stellantis NA',         'Stellantis North America','OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-HMMA',   'HMMA', N'Hyundai Motor Mfg AL',  'HMMA',                    'OEM',   'USA', 1, 'USD', 'ACTIVE', 'admin', SYSDATETIME()),
  ('CUS-BYD',    'BYD',  N'BYD Motors',            'BYD Motors',              'OEM',   'USA', 0, 'USD', 'ACTIVE', 'admin', SYSDATETIME());
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
INSERT INTO dbo.MD_Line (LineID, LineName, LineType, PlantID, DailyCap, ShiftPattern, RfidEnabledFlag, Status, CreatedBy, CreatedTS) VALUES
  ('LINE-INJ-01', N'Injection Line 1 (650T)',  'INJECTION', 'SAV', 4800, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-INJ-02', N'Injection Line 2 (850T)',  'INJECTION', 'SAV', 3600, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-IMG-01', N'Wrapping Line 1',           'WRAPPING',  'SAV', 1200, '2-SHIFT', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-01', N'Paint Line 1 (Powder)',     'PAINTING',  'GEO',  800, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-02', N'Paint Line 2 (Liquid)',     'PAINTING',  'GEO',  600, '3-SHIFT', 1, 'ACTIVE', 'admin', SYSDATETIME());
GO

-- Items
INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemNameEN, ItemType, ItemCategory, DefaultUOM, RoutingType, MinStock, SafetyStock, UnitCost, CustItemNoSAV, CustItemNoGEO, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('FIN-CONS-01', N'Console Upper (Black)',  'Console Upper - Black',  'FINISHED', N'Console',     'EA',  'A', 50,  100, 24.50,  'SAV-CONS-01', NULL,         1, 'admin', SYSDATETIME()),
  ('FIN-GRIL-02', N'Radiator Grille',        'Radiator Grille',        'FINISHED', N'Grille',      'EA',  'B', 30,   80, 18.20,  'SAV-GRIL-02', 'GEO-GRIL-02',1, 'admin', SYSDATETIME()),
  ('FIN-DOOR-LH',N'Door Trim LH (Gray)',     'Door Trim LH - Gray',    'FINISHED', N'Door Trim',   'EA',  'A', 40,  120, 31.00,  'SAV-DOOR-LH', NULL,         1, 'admin', SYSDATETIME()),
  ('FIN-DOOR-RH',N'Door Trim RH (Gray)',     'Door Trim RH - Gray',    'FINISHED', N'Door Trim',   'EA',  'A', 40,  120, 31.00,  'SAV-DOOR-RH', NULL,         1, 'admin', SYSDATETIME()),
  ('FIN-BUMP-FR',N'Front Bumper',            'Front Bumper',           'FINISHED', N'Bumper',      'EA',  'B', 20,   60, 78.50,  NULL,          'GEO-BUMP-FR',1, 'admin', SYSDATETIME()),
  ('RAW-PP-NAT', N'PP Pellet Natural',       'PP Pellet Natural',      'RAW',      N'Resin',       'LB', NULL, 2000, 5000, 1.20,  NULL,          NULL,         1, 'admin', SYSDATETIME()),
  ('RAW-ABS-BLK',N'ABS Resin Black',         'ABS Resin Black',        'RAW',      N'Resin',       'LB', NULL, 1500, 3000, 1.85,  NULL,          NULL,         1, 'admin', SYSDATETIME()),
  ('FAB-GRY-01', N'Vinyl Gray',              'Vinyl Gray',             'FABRIC',   N'Vinyl',       'M',  NULL,  500, 1500, 4.20,  NULL,          NULL,         1, 'admin', SYSDATETIME()),
  ('POW-RAL9005',N'Powder Black RAL9005',    'Powder Coat Black 9005', 'POWDER',   N'Paint',       'LB', NULL,  300, 1000, 12.50, NULL,          NULL,         1, 'admin', SYSDATETIME()),
  ('POW-RAL7042',N'Powder Gray RAL7042',     'Powder Coat Gray 7042',  'POWDER',   N'Paint',       'LB', NULL,  200,  800, 12.50, NULL,          NULL,         1, 'admin', SYSDATETIME());
GO

-- Equipment
INSERT INTO dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, MakerModel, InstallDate, TheoreticalCycle, TargetOEE, PlcAddress, Status, ActiveFlag, CreatedBy, CreatedTS) VALUES
  ('INJ-650-01',  N'Husky 650T Injection',     'INJ_MACHINE',  'LINE-INJ-01', N'Husky H650 RS135/132', '2023-06-15', 45.0, 85.00, '192.168.10.21', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('INJ-850-02',  N'Husky 850T Injection',     'INJ_MACHINE',  'LINE-INJ-02', N'Husky H850 RS180/180', '2023-08-22', 52.0, 85.00, '192.168.10.22', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('IMG-PRESS-01',N'Vinyl Wrapping Press',     'WRAP_PRESS',   'LINE-IMG-01', N'Dieffenbacher VP-400', '2024-02-10', 60.0, 82.00, '192.168.10.31', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('PNT-ROBOT-01',N'Paint Robot ABB IRB-6700', 'PNT_ROBOT',    'LINE-PNT-01', N'ABB IRB-6700-235',     '2024-04-05', 30.0, 80.00, '192.168.10.41', 'IDLE', 1, 'admin', SYSDATETIME()),
  ('OVEN-A1',     N'Cure Oven Zone A1',        'OVEN_UNIT',    'LINE-PNT-01', N'Eisenmann CT-180',     '2024-04-05', 0.0,  90.00, '192.168.10.42', 'IDLE', 1, 'admin', SYSDATETIME());
GO

PRINT '✓ Seed data inserted: 9 UOMs, 7 Customers, 5 Vendors, 5 Lines, 10 Items, 5 Equipment';
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
