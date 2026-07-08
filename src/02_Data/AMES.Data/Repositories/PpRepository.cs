using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Production Planning (PP) module queries — used by the Office Web.
/// Each method maps to one PP-XX screen. Lookups return rows in display
/// order; aggregates return summary DTOs.
/// </summary>
public sealed class PpRepository
{
    private readonly AmesConnectionFactory _f;
    public PpRepository(AmesConnectionFactory f) => _f = f;

    // ── DTOs (PP-only, kept local to avoid AMES.Contracts churn) ────────
    public sealed record ForecastRow(int ForecastId, string? Batch, string? CustomerId,
        string ItemNo, string? ItemName, DateTime? ForecastMonth, decimal ForecastQty,
        string? Confidence, string? Source);

    public sealed record WeeklyCell(string? CustomerId, string ItemNo, string? ItemName, string? PartName,
        string? Unit, decimal? BaseInv, DateTime WeekStartDate, string? WeekLabel, decimal Qty, bool ItemExists);

    public sealed record WeeklyImportRow(string ItemNo, string PartName, string Unit,
        decimal BaseInv, DateTime WeekStartDate, string WeekLabel, decimal Qty);

    public sealed record WeeklyImportBatch(string Batch, string? CustomerId, DateTime? ImportedAt,
        string? ImportedBy, int Rows, int Items, DateTime? WeekFrom, DateTime? WeekTo);

    public sealed record SoRow(int SoId, string? SoNumber, int? SoLineNo, string? CustomerId,
        string ItemNo, string? ItemName, decimal OrderQty, decimal ShippedQty,
        DateTime? OrderDate, DateTime? RequestedDeliveryDate, DateTime? PromisedDate, string? Status,
        string? WoNumber, string? WoStatus);

    public sealed record CustomerOrderImportRow(string SoNumber, int? SoLineNo, string ItemNo,
        decimal OrderQty, decimal ShippedQty, DateTime? OrderDate, DateTime? RequestedDeliveryDate);

    public sealed record SupplyPlanRow(int PlanId, string? PlanCode, DateTime? PlanPeriod,
        string? Status, int LineCount, decimal TotalPlannedQty);

    public sealed record MrpRunRow(int MrpRunId, DateTime? RunAt, DateTime? HorizonStart,
        DateTime? HorizonEnd, int WosConsidered, int PrsCreated, int ShortageCount,
        int DurationMs, string? Status);

    public sealed record PrRow(int PrId, string? PrNumber, string ItemNo, string? ItemName,
        string? VendorId, decimal RequiredQty, DateTime? RequiredDate, string? Status,
        string? SapPoNumber);

    public sealed record WoLite(int WoId, string? WoNumber, string ItemNo, string? ItemName,
        decimal OrderQty, decimal CompletedQty, string? LineId, DateTime? DueDate,
        string? Status, DateTime? ReleasedAt);

    public sealed record CalendarRow(int OverrideId, DateTime? OverrideDate, string? LineId,
        string? DayType, string? PatternId, decimal? CapacityFactor, string? Reason);

    public sealed record ScheduleRow(int ScheduleId, string? LineId, DateTime? ScheduleDate,
        int? WoId, string? WoNumber, int? StartMin, int? EndMin, decimal PlannedQty, string? Status);

    public sealed record OeeRow(int OeeSnapshotId, string? LineId, DateTime? PeriodDate, string? ShiftCode,
        int LoadingMin, int PlannedDownMin, int UnplannedDownMin, int OperatingMin,
        decimal TotalProducedQty, decimal GoodQty,
        decimal Availability, decimal Performance, decimal Quality, decimal OEE);

    public sealed record DowntimeRow(int DowntimeId, string? LineId, DateTime? StartTs, DateTime? EndTs,
        int DurationMin, string? ReasonCode, string? CauseCode, string? Comment, int? WoId);

    public sealed record LineStateRow(string? LineId, DateTime? MinuteTs, string? State,
        string? PlanState, bool RunFlag, int? WoId);

    public sealed record OtdRow(int SoId, string? SoNumber, string? CustomerId, string ItemNo,
        decimal OrderQty, decimal ShippedQty, DateTime? RequestedDeliveryDate,
        DateTime? PromisedDate, int DaysLate, string Status);

    // ── PP-001 Forecast ──────────────────────────────────────────────────
    public List<ForecastRow> ListForecast(int monthsBack = 6, int monthsAhead = 6)
    {
        const string sql = """
            SELECT TOP 200 f.ForecastID, f.ForecastBatch, f.CustomerID,
                   f.ItemNo, i.ItemName, f.ForecastMonth,
                   ISNULL(f.ForecastQty,0) AS ForecastQty,
                   f.Confidence, f.Source
            FROM   dbo.PP_Forecast f
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = f.ItemNo
            WHERE  f.ForecastMonth BETWEEN DATEADD(month, -@B, GETDATE())
                                      AND DATEADD(month,  @A, GETDATE())
            ORDER BY f.ForecastMonth, f.CustomerID, f.ItemNo;
            """;
        return Query(sql, r => new ForecastRow(
            (int)r["ForecastID"], r["ForecastBatch"] as string, r["CustomerID"] as string,
            r["ItemNo"] as string ?? "", r["ItemName"] as string,
            r["ForecastMonth"] as DateTime?,
            r.GetDecimal(r.GetOrdinal("ForecastQty")),
            r["Confidence"] as string, r["Source"] as string),
            ("@B", monthsBack), ("@A", monthsAhead));
    }

    /// <summary>PP-001 주간 구매계획 조회 — 화면에서 품목×주차로 피벗. customerId null/빈값 = 전체 고객.</summary>
    public List<WeeklyCell> ListWeeklyForecast(string? customerId, DateTime from, DateTime to)
    {
        const string sql = """
            SELECT f.CustomerID, f.ItemNo, i.ItemName, f.PartName, f.Unit, f.BaseInv,
                   f.WeekStartDate, f.WeekLabel, ISNULL(f.ForecastQty,0) AS Qty,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
            FROM   dbo.PP_Forecast f
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = f.ItemNo
            WHERE  (@Cust IS NULL OR f.CustomerID = @Cust)
              AND  f.WeekStartDate BETWEEN @From AND @To
            ORDER BY f.CustomerID, f.ItemNo, f.WeekStartDate;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value =
            string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.Date;
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<WeeklyCell>();
        while (rdr.Read())
            list.Add(new WeeklyCell(
                rdr["CustomerID"] as string,
                rdr["ItemNo"] as string ?? "", rdr["ItemName"] as string,
                rdr["PartName"] as string, rdr["Unit"] as string,
                rdr["BaseInv"] as decimal?, (DateTime)rdr["WeekStartDate"],
                rdr["WeekLabel"] as string,
                rdr.GetDecimal(rdr.GetOrdinal("Qty")),
                (int)rdr["ItemExists"] == 1));
        return list;
    }

    /// <summary>PP-001 주간 구매계획 업로드 이력 — ForecastBatch(=업로드 1건) 단위 집계.
    /// customerId null/빈값 = 전체. uploadedFrom/To(업로드 일시) null = 무제한.</summary>
    public List<WeeklyImportBatch> ListWeeklyImportBatches(string? customerId,
        DateTime? uploadedFrom = null, DateTime? uploadedTo = null, int take = 100)
    {
        const string sql = """
            SELECT TOP (@Take)
                   f.ForecastBatch          AS Batch,
                   MAX(f.CustomerID)        AS CustomerID,
                   MAX(f.ImportedAt)        AS ImportedAt,
                   MAX(f.ImportedBy)        AS ImportedBy,
                   COUNT(*)                 AS Rows,
                   COUNT(DISTINCT f.ItemNo) AS Items,
                   MIN(f.WeekStartDate)     AS WeekFrom,
                   MAX(f.WeekStartDate)     AS WeekTo
            FROM   dbo.PP_Forecast f
            WHERE  f.Source = 'SRM_WEEKLY' AND f.ForecastBatch IS NOT NULL
              AND  (@Cust IS NULL OR f.CustomerID = @Cust)
              AND  (@From IS NULL OR f.ImportedAt >= @From)
              AND  (@To   IS NULL OR f.ImportedAt <  DATEADD(day, 1, @To))
            GROUP BY f.ForecastBatch
            ORDER BY MAX(f.ImportedAt) DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = take;
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value =
            string.IsNullOrEmpty(customerId) ? DBNull.Value : customerId;
        cmd.Parameters.Add("@From", SqlDbType.DateTime2).Value = (object?)uploadedFrom?.Date ?? DBNull.Value;
        cmd.Parameters.Add("@To",   SqlDbType.DateTime2).Value = (object?)uploadedTo?.Date   ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();
        var list = new List<WeeklyImportBatch>();
        while (rdr.Read())
            list.Add(new WeeklyImportBatch(
                rdr["Batch"] as string ?? "",
                rdr["CustomerID"] as string,
                rdr["ImportedAt"] as DateTime?,
                rdr["ImportedBy"] as string,
                (int)rdr["Rows"], (int)rdr["Items"],
                rdr["WeekFrom"] as DateTime?, rdr["WeekTo"] as DateTime?));
        return list;
    }

    /// <summary>PP-001 업로드 검증 — MD_Item에 존재하는 품번만 반환.</summary>
    public HashSet<string> ListExistingItemNos(IEnumerable<string> itemNos)
    {
        const string sql = """
            SELECT i.ItemNo
            FROM   dbo.MD_Item i
            JOIN   STRING_SPLIT(@List, ',') s ON s.value = i.ItemNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@List", SqlDbType.NVarChar, -1).Value = string.Join(',', itemNos.Distinct());
        using var rdr = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (rdr.Read()) set.Add((string)rdr["ItemNo"]);
        return set;
    }

    /// <summary>
    /// PP-001 주간 구매계획 업서트 — 고객+품목+주차 기준 UPDATE, 없으면 INSERT.
    /// 수량이 바뀐 UPDATE는 PP_ForecastHistory에 이전/신규 수량 기록.
    /// </summary>
    public (int Inserted, int Updated, int QtyChanged) UpsertWeeklyForecast(
        string customerId, string batch, IReadOnlyList<WeeklyImportRow> rows, string actor)
    {
        const string updSql = """
            UPDATE dbo.PP_Forecast
            SET    ForecastBatch = @Batch, ForecastQty = @Qty,
                   BaseInv = @BaseInv, PartName = @PartName, Unit = @Unit, WeekLabel = @WeekLabel,
                   ForecastMonth = DATEFROMPARTS(YEAR(@Week), MONTH(@Week), 1),
                   Source = 'SRM_WEEKLY', ImportedAt = SYSDATETIME(), ImportedBy = @Actor,
                   ModifiedTS = SYSDATETIME(), ModifiedBy = @Actor
            OUTPUT inserted.ForecastID, deleted.ForecastQty, deleted.ForecastBatch
            WHERE  CustomerID = @Cust AND ItemNo = @Item AND WeekStartDate = @Week;
            """;
        const string insSql = """
            INSERT INTO dbo.PP_Forecast
                   (ForecastBatch, CustomerID, ItemNo, ForecastMonth, ForecastQty,
                    WeekStartDate, WeekLabel, BaseInv, PartName, Unit,
                    Source, ImportedAt, ImportedBy, CreatedBy)
            VALUES (@Batch, @Cust, @Item, DATEFROMPARTS(YEAR(@Week), MONTH(@Week), 1), @Qty,
                    @Week, @WeekLabel, @BaseInv, @PartName, @Unit,
                    'SRM_WEEKLY', SYSDATETIME(), @Actor, @Actor);
            """;
        const string histSql = """
            INSERT INTO dbo.PP_ForecastHistory
                   (ForecastID, PrevBatch, PrevQty, NewQty, ChangedAt, ChangedBy, CreatedBy)
            VALUES (@Fid, @PrevBatch, @PrevQty, @NewQty, SYSDATETIME(), @Actor, @Actor);
            """;

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using var upd  = new SqlCommand(updSql,  conn, tx);
            using var ins  = new SqlCommand(insSql,  conn, tx);
            using var hist = new SqlCommand(histSql, conn, tx);
            foreach (var c in new[] { upd, ins })
            {
                c.Parameters.Add("@Batch",     SqlDbType.VarChar,    20);
                c.Parameters.Add("@Cust",      SqlDbType.VarChar,    20);
                c.Parameters.Add("@Item",      SqlDbType.VarChar,    20);
                c.Parameters.Add("@Week",      SqlDbType.Date);
                c.Parameters.Add("@Qty",       SqlDbType.Decimal).Precision = 14;
                c.Parameters["@Qty"].Scale = 3;
                c.Parameters.Add("@WeekLabel", SqlDbType.VarChar,    10);
                c.Parameters.Add("@BaseInv",   SqlDbType.Decimal).Precision = 14;
                c.Parameters["@BaseInv"].Scale = 3;
                c.Parameters.Add("@PartName",  SqlDbType.NVarChar,  100);
                c.Parameters.Add("@Unit",      SqlDbType.VarChar,    10);
                c.Parameters.Add("@Actor",     SqlDbType.NVarChar,  450);
                c.Parameters["@Batch"].Value = batch;
                c.Parameters["@Cust"].Value  = customerId;
                c.Parameters["@Actor"].Value = actor;
            }
            hist.Parameters.Add("@Fid",       SqlDbType.Int);
            hist.Parameters.Add("@PrevBatch", SqlDbType.VarChar, 20);
            hist.Parameters.Add("@PrevQty",   SqlDbType.Decimal).Precision = 14;
            hist.Parameters["@PrevQty"].Scale = 3;
            hist.Parameters.Add("@NewQty",    SqlDbType.Decimal).Precision = 14;
            hist.Parameters["@NewQty"].Scale = 3;
            hist.Parameters.Add("@Actor",     SqlDbType.NVarChar, 450).Value = actor;

            int inserted = 0, updated = 0, qtyChanged = 0;
            foreach (var r in rows)
            {
                foreach (var c in new[] { upd, ins })
                {
                    c.Parameters["@Item"].Value      = r.ItemNo;
                    c.Parameters["@Week"].Value      = r.WeekStartDate;
                    c.Parameters["@Qty"].Value       = r.Qty;
                    c.Parameters["@WeekLabel"].Value = r.WeekLabel;
                    c.Parameters["@BaseInv"].Value   = r.BaseInv;
                    c.Parameters["@PartName"].Value  = r.PartName;
                    c.Parameters["@Unit"].Value      = r.Unit;
                }

                int?     fid      = null;
                decimal? prevQty  = null;
                string?  prevBatch = null;
                using (var rdr = upd.ExecuteReader())
                    if (rdr.Read())
                    {
                        fid       = (int)rdr["ForecastID"];
                        prevQty   = rdr["ForecastQty"] as decimal?;
                        prevBatch = rdr["ForecastBatch"] as string;
                    }

                if (fid is null)
                {
                    ins.ExecuteNonQuery();
                    inserted++;
                }
                else
                {
                    updated++;
                    if (prevQty != r.Qty)
                    {
                        hist.Parameters["@Fid"].Value       = fid.Value;
                        hist.Parameters["@PrevBatch"].Value = (object?)prevBatch ?? DBNull.Value;
                        hist.Parameters["@PrevQty"].Value   = (object?)prevQty   ?? DBNull.Value;
                        hist.Parameters["@NewQty"].Value    = r.Qty;
                        hist.ExecuteNonQuery();
                        qtyChanged++;
                    }
                }
            }
            tx.Commit();
            return (inserted, updated, qtyChanged);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── PP-002 Supply Plan Import (customer orders synced from SAP) ─────
    public List<SoRow> ListSupplyPlanImports(int daysBack = 30)
    {
        const string sql = """
            SELECT TOP 100 s.SoID, s.SoNumber, s.SoLineNo, s.CustomerID,
                   s.ItemNo, i.ItemName,
                   ISNULL(s.OrderQty,0)   AS OrderQty,
                   ISNULL(s.ShippedQty,0) AS ShippedQty,
                   s.OrderDate, s.RequestedDeliveryDate, s.PromisedDate, s.Status,
                   wo.WoNumber, wo.WoStatus
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            OUTER APPLY (SELECT TOP 1 w.WoNumber, w.Status AS WoStatus
                         FROM dbo.PP_WorkOrder w WHERE w.SoID = s.SoID
                         ORDER BY w.CreatedTS DESC) wo
            WHERE  s.SapSyncedAt > DATEADD(day, -@D, SYSDATETIME())
               OR  s.OrderDate   > DATEADD(day, -@D, GETDATE())
            ORDER BY s.OrderDate DESC, s.SoNumber;
            """;
        return Query(sql, MapSo, ("@D", daysBack));
    }

    // ── PP-002 filtered read — customer/order-date range (SAP import grid) ──
    public List<SoRow> ListCustomerOrders(string customerId, DateTime? from, DateTime? to, int take = 500)
    {
        var sql = $$"""
            SELECT TOP ({{take}}) s.SoID, s.SoNumber, s.SoLineNo, s.CustomerID,
                   s.ItemNo, i.ItemName,
                   ISNULL(s.OrderQty,0)   AS OrderQty,
                   ISNULL(s.ShippedQty,0) AS ShippedQty,
                   s.OrderDate, s.RequestedDeliveryDate, s.PromisedDate, s.Status,
                   wo.WoNumber, wo.WoStatus
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            OUTER APPLY (SELECT TOP 1 w.WoNumber, w.Status AS WoStatus
                         FROM dbo.PP_WorkOrder w WHERE w.SoID = s.SoID
                         ORDER BY w.CreatedTS DESC) wo
            WHERE  (@Cust = '' OR s.CustomerID = @Cust)
               AND (@From IS NULL OR s.OrderDate >= @From)
               AND (@To   IS NULL OR s.OrderDate <= @To)
            ORDER BY s.OrderDate DESC, s.SoNumber, s.SoLineNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId ?? "";
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = (object?)from ?? DBNull.Value;
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = (object?)to   ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();
        var list = new List<SoRow>();
        while (rdr.Read()) list.Add(MapSo(rdr));
        return list;
    }

    /// <summary>
    /// PP-002 SAP 구매오더(고객주문) 업서트 — (SoNumber, SoLineNo, CustomerID) 기준
    /// UPDATE, 없으면 INSERT. 신규 행은 Status='Open'으로 삽입, 기존 행의 Status는
    /// 보존(Confirmed 상태가 재임포트로 덮이지 않음). 매 실행 시 SapSyncedAt 갱신.
    /// </summary>
    public (int Inserted, int Updated) UpsertCustomerOrders(
        string customerId, IReadOnlyList<CustomerOrderImportRow> rows, string actor)
    {
        const string updSql = """
            UPDATE dbo.PP_CustomerOrder
            SET    ItemNo = @Item, OrderQty = @OrderQty, ShippedQty = @ShippedQty,
                   OrderDate = @OrderDate, RequestedDeliveryDate = @ReqDate,
                   SapSyncedAt = SYSDATETIME(),
                   ModifiedTS = SYSDATETIME(), ModifiedBy = @Actor
            OUTPUT inserted.SoID
            WHERE  SoNumber = @So AND ISNULL(SoLineNo,-1) = ISNULL(@Line,-1) AND CustomerID = @Cust;
            """;
        const string insSql = """
            INSERT INTO dbo.PP_CustomerOrder
                   (SoNumber, SoLineNo, CustomerID, ItemNo, OrderQty, ShippedQty,
                    OrderDate, RequestedDeliveryDate, Status, SapSyncedAt, CreatedBy)
            VALUES (@So, @Line, @Cust, @Item, @OrderQty, @ShippedQty,
                    @OrderDate, @ReqDate, 'Open', SYSDATETIME(), @Actor);
            """;

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using var upd = new SqlCommand(updSql, conn, tx);
            using var ins = new SqlCommand(insSql, conn, tx);
            foreach (var c in new[] { upd, ins })
            {
                c.Parameters.Add("@So",   SqlDbType.VarChar, 20);
                c.Parameters.Add("@Line", SqlDbType.Int);
                c.Parameters.Add("@Cust", SqlDbType.VarChar, 20);
                c.Parameters.Add("@Item", SqlDbType.VarChar, 20);
                c.Parameters.Add("@OrderQty",   SqlDbType.Decimal).Precision = 14;
                c.Parameters["@OrderQty"].Scale = 3;
                c.Parameters.Add("@ShippedQty", SqlDbType.Decimal).Precision = 14;
                c.Parameters["@ShippedQty"].Scale = 3;
                c.Parameters.Add("@OrderDate", SqlDbType.Date);
                c.Parameters.Add("@ReqDate",   SqlDbType.Date);
                c.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450);
                c.Parameters["@Cust"].Value  = customerId;
                c.Parameters["@Actor"].Value = actor;
            }

            int inserted = 0, updated = 0;
            foreach (var r in rows)
            {
                foreach (var c in new[] { upd, ins })
                {
                    c.Parameters["@So"].Value         = r.SoNumber;
                    c.Parameters["@Line"].Value       = (object?)r.SoLineNo ?? DBNull.Value;
                    c.Parameters["@Item"].Value       = r.ItemNo;
                    c.Parameters["@OrderQty"].Value   = r.OrderQty;
                    c.Parameters["@ShippedQty"].Value = r.ShippedQty;
                    c.Parameters["@OrderDate"].Value  = (object?)r.OrderDate ?? DBNull.Value;
                    c.Parameters["@ReqDate"].Value    = (object?)r.RequestedDeliveryDate ?? DBNull.Value;
                }

                var hit = upd.ExecuteScalar();
                if (hit is null || hit is DBNull)
                {
                    ins.ExecuteNonQuery();
                    inserted++;
                }
                else updated++;
            }
            tx.Commit();
            return (inserted, updated);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── PP-003 Plan Confirm — list supply plans with line count rollup ──
    public List<SupplyPlanRow> ListSupplyPlans(int topN = 30)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) p.PlanID, p.PlanCode, p.PlanPeriod, p.Status,
                   (SELECT COUNT(*)            FROM dbo.PP_SupplyPlanDetail d WHERE d.PlanID = p.PlanID) AS LineCount,
                   ISNULL((SELECT SUM(d.PlannedQty) FROM dbo.PP_SupplyPlanDetail d WHERE d.PlanID = p.PlanID), 0) AS TotalPlannedQty
            FROM   dbo.PP_SupplyPlan p
            ORDER BY p.PlanPeriod DESC, p.PlanID DESC;
            """;
        return Query(sql, r => new SupplyPlanRow(
            (int)r["PlanID"], r["PlanCode"] as string, r["PlanPeriod"] as DateTime?,
            r["Status"] as string, (int)r["LineCount"],
            r.GetDecimal(r.GetOrdinal("TotalPlannedQty"))));
    }

    // ── PP-004 Work Order: covered by WorkOrderRepository ───────────────

    // ── PP-005 MRP — last N runs ────────────────────────────────────────
    public List<MrpRunRow> ListMrpRuns(int topN = 20)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) MrpRunID, RunAt, HorizonStart, HorizonEnd,
                   ISNULL(WosConsidered,0) AS WosConsidered,
                   ISNULL(PrsCreated,0)    AS PrsCreated,
                   ISNULL(ShortageCount,0) AS ShortageCount,
                   ISNULL(DurationMs,0)    AS DurationMs,
                   Status
            FROM   dbo.PP_MRPLog
            ORDER BY RunAt DESC, MrpRunID DESC;
            """;
        return Query(sql, r => new MrpRunRow(
            (int)r["MrpRunID"], r["RunAt"] as DateTime?,
            r["HorizonStart"] as DateTime?, r["HorizonEnd"] as DateTime?,
            (int)r["WosConsidered"], (int)r["PrsCreated"], (int)r["ShortageCount"],
            (int)r["DurationMs"], r["Status"] as string));
    }

    // ── PP-006 Purchase Request ─────────────────────────────────────────
    public List<PrRow> ListPurchaseRequests(int topN = 50)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) p.PrID, p.PrNumber, p.ItemNo, i.ItemName,
                   p.VendorID, ISNULL(p.RequiredQty,0) AS RequiredQty,
                   p.RequiredDate, p.Status, p.SapPoNumber
            FROM   dbo.PP_PurchaseRequest p
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = p.ItemNo
            ORDER BY ISNULL(p.RequiredDate, '9999-01-01'), p.PrID DESC;
            """;
        return Query(sql, r => new PrRow(
            (int)r["PrID"], r["PrNumber"] as string,
            r["ItemNo"] as string ?? "", r["ItemName"] as string,
            r["VendorID"] as string, r.GetDecimal(r.GetOrdinal("RequiredQty")),
            r["RequiredDate"] as DateTime?, r["Status"] as string,
            r["SapPoNumber"] as string));
    }

    // ── PP-007 WO Release — draft/planned WOs awaiting release ─────────
    public List<WoLite> ListReleasable(int topN = 50)
    {
        var sql = $$"""
            SELECT TOP ({{topN}}) w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OrderQty,0)     AS OrderQty,
                   ISNULL(w.CompletedQty,0) AS CompletedQty,
                   w.LineID, w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            ORDER BY CASE WHEN ISNULL(w.Status,'Draft') IN ('Draft','Planned') THEN 0 ELSE 1 END,
                     ISNULL(w.DueDate,'9999-01-01'), w.WoID;
            """;
        return Query(sql, MapWoLite);
    }

    /// <summary>PP-007 관리 화면용: 전체 상태 WO 조회. Closed는 최근 <paramref name="recentClosedDays"/>일만.</summary>
    public List<WoLite> ListAllWo(string? lineId = null, string? status = null, int recentClosedDays = 30)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OrderQty,0)     AS OrderQty,
                   ISNULL(w.CompletedQty,0) AS CompletedQty,
                   w.LineID, w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  (@LineID IS NULL OR w.LineID = @LineID)
              AND  (@Status IS NULL OR ISNULL(w.Status,'Draft') = @Status)
              AND  (ISNULL(w.Status,'Draft') <> 'Closed'
                    OR w.ActualEnd >= DATEADD(day, -@Days, CAST(GETDATE() AS date)))
            ORDER BY CASE ISNULL(w.Status,'Draft')
                          WHEN 'Draft'       THEN 0
                          WHEN 'Planned'     THEN 1
                          WHEN 'Released'    THEN 2
                          WHEN 'In Progress' THEN 3
                          ELSE 4 END,
                     ISNULL(w.DueDate,'9999-01-01'), w.WoID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = (object?)lineId  ?? DBNull.Value;
        cmd.Parameters.Add("@Status", SqlDbType.VarChar, 20).Value = (object?)status  ?? DBNull.Value;
        cmd.Parameters.Add("@Days",   SqlDbType.Int          ).Value = recentClosedDays;
        using var rdr  = cmd.ExecuteReader();
        var list = new List<WoLite>();
        while (rdr.Read()) list.Add(MapWoLite(rdr));
        return list;
    }

    /// <summary>SO 상태를 Confirmed로 변경 (PP-002). Open 건만 대상. 변경 행수 반환.</summary>
    public int ConfirmCustomerOrder(int soId, string actor)
    {
        const string sql = """
            UPDATE dbo.PP_CustomerOrder
            SET    Status     = 'Confirmed',
                   ModifiedTS = SYSDATETIME(),
                   ModifiedBy = @Actor
            WHERE  SoID = @SoID AND Status = 'Open';
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@SoID",  SqlDbType.Int).Value = soId;
        cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
        return cmd.ExecuteNonQuery();
    }

    /// <summary>WO 상태를 Released로 변경하고 ReleasedAt을 기록합니다 (PP-007).</summary>
    public void ReleaseWo(int woId)
    {
        const string sql = """
            UPDATE dbo.PP_WorkOrder
            SET    Status     = 'Released',
                   ReleasedAt = SYSDATETIME(),
                   ModifiedTS = SYSDATETIME()
            WHERE  WoID   = @WoID
              AND  Status IN ('Draft','Planned');
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        cmd.ExecuteNonQuery();
    }

    // ── PP-CAL Calendar overrides ───────────────────────────────────────
    public List<CalendarRow> ListCalendarOverrides(int daysBack = 7, int daysAhead = 30)
    {
        const string sql = """
            SELECT TOP 100 OverrideID, OverrideDate, LineID, DayType, PatternID,
                   CapacityFactor, Reason
            FROM   dbo.PP_ProductionCalendarOverride
            WHERE  OverrideDate BETWEEN DATEADD(day, -@B, GETDATE())
                                   AND DATEADD(day,  @A, GETDATE())
            ORDER BY OverrideDate;
            """;
        return Query(sql, r => new CalendarRow(
            (int)r["OverrideID"], r["OverrideDate"] as DateTime?,
            r["LineID"] as string, r["DayType"] as string,
            r["PatternID"] as string, r["CapacityFactor"] as decimal?,
            r["Reason"] as string),
            ("@B", daysBack), ("@A", daysAhead));
    }

    // ── PP-LSB Line Schedule ────────────────────────────────────────────
    public List<ScheduleRow> ListSchedule(int daysAhead = 7)
    {
        const string sql = """
            SELECT TOP 200 s.ScheduleID, s.LineID, s.ScheduleDate,
                   s.WoID, w.WoNumber, s.StartMin, s.EndMin,
                   ISNULL(s.PlannedQty,0) AS PlannedQty, s.Status
            FROM   dbo.PP_LineSchedule s
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = s.WoID
            WHERE  s.ScheduleDate BETWEEN CAST(GETDATE() AS DATE)
                                      AND DATEADD(day, @A, CAST(GETDATE() AS DATE))
            ORDER BY s.ScheduleDate, s.LineID, s.StartMin;
            """;
        return Query(sql, r => new ScheduleRow(
            (int)r["ScheduleID"], r["LineID"] as string, r["ScheduleDate"] as DateTime?,
            r["WoID"] as int?, r["WoNumber"] as string,
            r["StartMin"] as int?, r["EndMin"] as int?,
            r.GetDecimal(r.GetOrdinal("PlannedQty")), r["Status"] as string),
            ("@A", daysAhead));
    }

    // ── PP-OEE Line OEE ─────────────────────────────────────────────────
    public List<OeeRow> ListOee(int daysBack = 14)
    {
        const string sql = """
            SELECT TOP 100 OeeSnapshotID, LineID, PeriodDate, ShiftCode,
                   ISNULL(LoadingMin,0)        AS LoadingMin,
                   ISNULL(PlannedDownMin,0)    AS PlannedDownMin,
                   ISNULL(UnplannedDownMin,0)  AS UnplannedDownMin,
                   ISNULL(OperatingMin,0)      AS OperatingMin,
                   ISNULL(TotalProducedQty,0)  AS TotalProducedQty,
                   ISNULL(GoodQty,0)           AS GoodQty,
                   ISNULL(Availability,0)      AS Availability,
                   ISNULL(Performance,0)       AS Performance,
                   ISNULL(Quality,0)           AS Quality,
                   ISNULL(OEE,0)               AS OEE
            FROM   dbo.PP_LineOEE
            WHERE  PeriodDate > DATEADD(day, -@D, GETDATE())
            ORDER BY PeriodDate DESC, LineID, ShiftCode;
            """;
        return Query(sql, r => new OeeRow(
            (int)r["OeeSnapshotID"], r["LineID"] as string,
            r["PeriodDate"] as DateTime?, r["ShiftCode"] as string,
            (int)r["LoadingMin"], (int)r["PlannedDownMin"], (int)r["UnplannedDownMin"],
            (int)r["OperatingMin"],
            r.GetDecimal(r.GetOrdinal("TotalProducedQty")),
            r.GetDecimal(r.GetOrdinal("GoodQty")),
            r.GetDecimal(r.GetOrdinal("Availability")),
            r.GetDecimal(r.GetOrdinal("Performance")),
            r.GetDecimal(r.GetOrdinal("Quality")),
            r.GetDecimal(r.GetOrdinal("OEE"))),
            ("@D", daysBack));
    }

    // ── PP-DTL Downtime Log ─────────────────────────────────────────────
    public List<DowntimeRow> ListDowntime(int daysBack = 7)
    {
        const string sql = """
            SELECT TOP 100 DowntimeID, LineID, StartTS, EndTS,
                   ISNULL(DurationMin,0) AS DurationMin,
                   ReasonCode, CauseCode, Comment, WoID
            FROM   dbo.PP_LineDowntimeLog
            WHERE  StartTS > DATEADD(day, -@D, SYSDATETIME())
            ORDER BY StartTS DESC;
            """;
        return Query(sql, r => new DowntimeRow(
            (int)r["DowntimeID"], r["LineID"] as string,
            r["StartTS"] as DateTime?, r["EndTS"] as DateTime?,
            (int)r["DurationMin"], r["ReasonCode"] as string,
            r["CauseCode"] as string, r["Comment"] as string,
            r["WoID"] as int?),
            ("@D", daysBack));
    }

    /// <summary>PP-DTL 필터 조회 — 라인·기간·상태·사유코드 조합.</summary>
    public List<DowntimeRow> ListDowntimeFiltered(
        string?  lineId       = null,
        DateTime? from        = null,
        DateTime? to          = null,
        string?  status       = null,   // "open" | "closed" | null=all
        string?  reasonCode   = null,
        int      limit        = 500)
    {
        var sql = $"""
            SELECT TOP (@Limit) DowntimeID, LineID, StartTS, EndTS,
                   ISNULL(DurationMin,0) AS DurationMin,
                   ReasonCode, CauseCode, Comment, WoID
            FROM   dbo.PP_LineDowntimeLog
            WHERE  1=1
            {(lineId     != null ? "AND LineID     = @LineId "      : "")}
            {(from       != null ? "AND StartTS   >= @From "        : "")}
            {(to         != null ? "AND StartTS   <  @To "          : "")}
            {(status == "open"   ? "AND EndTS     IS NULL "         : "")}
            {(status == "closed" ? "AND EndTS     IS NOT NULL "     : "")}
            {(reasonCode != null ? "AND ReasonCode = @ReasonCode "  : "")}
            ORDER BY StartTS DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        if (lineId     != null) cmd.Parameters.Add("@LineId",     SqlDbType.VarChar, 20).Value = lineId;
        if (from       != null) cmd.Parameters.Add("@From",       SqlDbType.DateTime2).Value   = from.Value;
        if (to         != null) cmd.Parameters.Add("@To",         SqlDbType.DateTime2).Value   = to.Value.Date.AddDays(1);
        if (reasonCode != null) cmd.Parameters.Add("@ReasonCode", SqlDbType.VarChar, 30).Value = reasonCode;
        using var rdr = cmd.ExecuteReader();
        var list = new List<DowntimeRow>();
        while (rdr.Read())
            list.Add(new DowntimeRow(
                (int)rdr["DowntimeID"], rdr["LineID"] as string,
                rdr["StartTS"] as DateTime?, rdr["EndTS"] as DateTime?,
                (int)rdr["DurationMin"], rdr["ReasonCode"] as string,
                rdr["CauseCode"] as string, rdr["Comment"] as string,
                rdr["WoID"] as int?));
        return list;
    }

    /// <summary>PP-ODM 비계획 비가동 확정 시 PP_LineDowntimeLog에 이벤트 기록.</summary>
    public void AddDowntimeEvent(string lineId, DateTime startTs, DateTime endTs,
        string? reasonCode, string? causeCode, string? comment, int? woId = null)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineDowntimeLog
                   (LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment, WoID)
            VALUES (@LineId, @Start, @End,
                    DATEDIFF(minute, @Start, @End),
                    @Reason, @Cause, @Comment, @WoId);
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@LineId",  SqlDbType.VarChar,   20).Value  = lineId;
        cmd.Parameters.Add("@Start",   SqlDbType.DateTime2).Value      = startTs;
        cmd.Parameters.Add("@End",     SqlDbType.DateTime2).Value      = endTs;
        cmd.Parameters.Add("@Reason",  SqlDbType.VarChar,   30).Value  = (object?)reasonCode ?? DBNull.Value;
        cmd.Parameters.Add("@Cause",   SqlDbType.VarChar,   30).Value  = (object?)causeCode  ?? DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value  = (object?)comment    ?? DBNull.Value;
        cmd.Parameters.Add("@WoId",    SqlDbType.Int).Value            = (object?)woId       ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-ODM 비가동 사유 수정/보완 (사유 미입력 또는 오기입 수정).</summary>
    public void UpdateDowntimeReason(int downtimeId, string? reasonCode, string? causeCode, string? comment)
    {
        const string sql = """
            UPDATE dbo.PP_LineDowntimeLog
            SET ReasonCode = @Reason,
                CauseCode  = @Cause,
                Comment    = @Comment
            WHERE DowntimeID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",      SqlDbType.Int).Value            = downtimeId;
        cmd.Parameters.Add("@Reason",  SqlDbType.VarChar,   30).Value  = (object?)reasonCode ?? DBNull.Value;
        cmd.Parameters.Add("@Cause",   SqlDbType.VarChar,   30).Value  = (object?)causeCode  ?? DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value  = (object?)comment    ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-DTL/ODM 라인 목록 (PP_LineDowntimeLog 기준).</summary>
    public List<string> ListDowntimeLines()
    {
        const string sql = """
            SELECT DISTINCT LineID FROM dbo.PP_LineDowntimeLog
            WHERE LineID IS NOT NULL ORDER BY LineID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["LineID"]);
        return list;
    }

    // ── PP-ODM Downtime Monitor (last 4 hours of state log per line) ────
    public List<LineStateRow> ListLineStates(int hoursBack = 4)
    {
        const string sql = """
            SELECT TOP 500 LineID, MinuteTS, State, PlanState,
                   ISNULL(RunFlag, 0) AS RunFlag, WoID
            FROM   dbo.PP_LineStateLog
            WHERE  MinuteTS > DATEADD(hour, -@H, SYSDATETIME())
            ORDER BY MinuteTS DESC, LineID;
            """;
        return Query(sql, r => new LineStateRow(
            r["LineID"] as string, r["MinuteTS"] as DateTime?,
            r["State"] as string, r["PlanState"] as string,
            Convert.ToBoolean(r["RunFlag"]), r["WoID"] as int?),
            ("@H", hoursBack));
    }

    // ── PP-OTD On-Time Delivery ─────────────────────────────────────────
    public List<OtdRow> ListOtd(int daysBack = 30)
    {
        const string sql = """
            SELECT TOP 100 SoID, SoNumber, CustomerID, ItemNo,
                   ISNULL(OrderQty,0)   AS OrderQty,
                   ISNULL(ShippedQty,0) AS ShippedQty,
                   RequestedDeliveryDate, PromisedDate,
                   CASE WHEN ShippedQty >= OrderQty AND RequestedDeliveryDate IS NOT NULL
                        THEN DATEDIFF(day, RequestedDeliveryDate, GETDATE())
                        ELSE 0 END AS DaysLate,
                   ISNULL(Status,'?') AS Status
            FROM   dbo.PP_CustomerOrder
            WHERE  RequestedDeliveryDate > DATEADD(day, -@D, GETDATE())
            ORDER BY RequestedDeliveryDate;
            """;
        return Query(sql, r => new OtdRow(
            (int)r["SoID"], r["SoNumber"] as string, r["CustomerID"] as string,
            r["ItemNo"] as string ?? "",
            r.GetDecimal(r.GetOrdinal("OrderQty")),
            r.GetDecimal(r.GetOrdinal("ShippedQty")),
            r["RequestedDeliveryDate"] as DateTime?,
            r["PromisedDate"] as DateTime?,
            (int)r["DaysLate"], r["Status"] as string ?? "?"),
            ("@D", daysBack));
    }

    /// <summary>PP-OTD 필터 조회 — 기간·고객·상태 조합.</summary>
    public List<OtdRow> ListOtdFiltered(
        DateTime? from       = null,
        DateTime? to         = null,
        string?  customerId  = null,
        string?  status      = null,   // "ontime" | "late" | "open" | null=all
        int      limit       = 500)
    {
        var sql = $"""
            SELECT TOP (@Limit)
                   SoID, SoNumber, CustomerID, ItemNo,
                   ISNULL(OrderQty,0)   AS OrderQty,
                   ISNULL(ShippedQty,0) AS ShippedQty,
                   RequestedDeliveryDate, PromisedDate,
                   CASE WHEN ShippedQty >= OrderQty AND RequestedDeliveryDate IS NOT NULL
                        THEN DATEDIFF(day, RequestedDeliveryDate, GETDATE())
                        ELSE 0 END AS DaysLate,
                   ISNULL(Status,'?') AS Status
            FROM   dbo.PP_CustomerOrder
            WHERE  1=1
            {(from       != null ? "AND RequestedDeliveryDate >= @From "       : "")}
            {(to         != null ? "AND RequestedDeliveryDate <  @To "         : "")}
            {(customerId != null ? "AND CustomerID = @Customer "               : "")}
            {(status == "ontime" ? "AND ShippedQty >= OrderQty AND DATEDIFF(day, RequestedDeliveryDate, GETDATE()) <= 0 " : "")}
            {(status == "late"   ? "AND DATEDIFF(day, RequestedDeliveryDate, GETDATE()) > 0 "                            : "")}
            {(status == "open"   ? "AND ShippedQty < OrderQty "                : "")}
            ORDER BY RequestedDeliveryDate DESC;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        if (from       != null) cmd.Parameters.Add("@From",     SqlDbType.Date).Value        = from.Value.Date;
        if (to         != null) cmd.Parameters.Add("@To",       SqlDbType.Date).Value        = to.Value.Date.AddDays(1);
        if (customerId != null) cmd.Parameters.Add("@Customer", SqlDbType.VarChar, 30).Value = customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<OtdRow>();
        while (rdr.Read())
            list.Add(new OtdRow(
                (int)rdr["SoID"], rdr["SoNumber"] as string, rdr["CustomerID"] as string,
                rdr["ItemNo"] as string ?? "",
                rdr.GetDecimal(rdr.GetOrdinal("OrderQty")),
                rdr.GetDecimal(rdr.GetOrdinal("ShippedQty")),
                rdr["RequestedDeliveryDate"] as DateTime?,
                rdr["PromisedDate"] as DateTime?,
                (int)rdr["DaysLate"], rdr["Status"] as string ?? "?"));
        return list;
    }

    /// <summary>PP-OTD 고객 목록 (필터 드롭다운용).</summary>
    public List<string> ListOtdCustomers()
    {
        const string sql = """
            SELECT DISTINCT CustomerID FROM dbo.PP_CustomerOrder
            WHERE CustomerID IS NOT NULL ORDER BY CustomerID;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read()) list.Add((string)rdr["CustomerID"]);
        return list;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private List<T> Query<T>(string sql, Func<IDataReader, T> map, params (string Name, object Value)[] pars)
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v);
        using var rdr  = cmd.ExecuteReader();
        var list = new List<T>();
        while (rdr.Read()) list.Add(map(rdr));
        return list;
    }
    private static SoRow MapSo(IDataReader r) => new(
        (int)r["SoID"], r["SoNumber"] as string, r["SoLineNo"] as int?, r["CustomerID"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("ShippedQty")),
        r["OrderDate"] as DateTime?, r["RequestedDeliveryDate"] as DateTime?,
        r["PromisedDate"] as DateTime?, r["Status"] as string,
        r["WoNumber"] as string, r["WoStatus"] as string);
    private static WoLite MapWoLite(IDataReader r) => new(
        (int)r["WoID"], r["WoNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("CompletedQty")),
        r["LineID"] as string, r["DueDate"] as DateTime?,
        r["Status"] as string, r["ReleasedAt"] as DateTime?);
}
