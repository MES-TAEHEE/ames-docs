using System.Data;
using AMES.Data.Connection;
using AMES.Data.Scheduling;
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
        string? WoNumber, string? WoStatus, bool ItemExists);

    public sealed record CustomerOrderImportRow(string SoNumber, int? SoLineNo, string ItemNo,
        decimal OrderQty, decimal ShippedQty, DateTime? OrderDate, DateTime? RequestedDeliveryDate);

    public sealed record SupplyPlanRow(int PlanId, string? PlanCode, DateTime? PlanPeriod,
        string? Status, int LineCount, decimal TotalPlannedQty);

    // PP-003 계획 검토 라인 — WO 미생성 확정 수주 + FG 재고 + 라인 부하.
    public sealed record PlanLineRow(int SoId, string? SoNumber, int? SoLineNo, string? CustomerId,
        string ItemNo, string? ItemName, decimal OrderQty, decimal FgOnHand, DateTime? DueDate, bool ItemExists,
        string? LineId, int? LineLoadPct, string? RoutingType)
    {
        public decimal NetReq => Math.Max(0m, OrderQty - FgOnHand);
        public bool NoRouting => ItemExists && RoutingType is null;
        // 품목마스터 미완성(BR-PP-001) 또는 라우팅 미지정 → WO 생성 불가
        public bool Blocked => !ItemExists || RoutingType is null;
    }

    public sealed record MrpRunRow(int MrpRunId, DateTime? RunAt, DateTime? HorizonStart,
        DateTime? HorizonEnd, int WosConsidered, int PrsCreated, int ShortageCount,
        int DurationMs, string? Status);

    public sealed record PrRow(int PrId, string? PrNumber, string ItemNo, string? ItemName,
        string? VendorId, decimal RequiredQty, DateTime? RequiredDate, string? Status,
        string? SapPoNumber);

    public sealed record WoLite(int WoId, string? WoNumber, string ItemNo, string? ItemName,
        decimal OrderQty, decimal CompletedQty, string? RouteLines, DateTime? DueDate,
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

    /// <summary>
    /// PP-OTD 수주 1행. PP_CustomerOrder 에는 실제 출하일이 없으므로
    ///   DaysLate      = 미출하(ShippedQty &lt; OrderQty)이면서 요청납기가 경과한 일수, 그 외 0
    ///   PromiseGapDays= 약속납기 − 요청납기 (양수면 요청보다 늦게 약속)
    /// CustomerId 는 MD_Customer 로 정규화한 마스터 ID (원본은 ID/코드가 섞여 있다).
    /// </summary>
    public sealed record OtdRow(int SoId, string? SoNumber, int? SoLineNo,
        string? CustomerId, string? CustomerCode, string? CustomerName, string? CustomerNameEn,
        string ItemNo, string? ItemName, string? ItemNameEn,
        decimal OrderQty, decimal ShippedQty,
        DateTime? OrderDate, DateTime? RequestedDeliveryDate, DateTime? PromisedDate,
        int DaysLate, int? PromiseGapDays, string Status)
    {
        public bool    IsShipped   => ShippedQty >= OrderQty;
        public bool    IsLate      => !IsShipped && DaysLate > 0;
        public bool    IsOpen      => !IsShipped && DaysLate <= 0;
        public decimal ProgressPct => OrderQty > 0 ? Math.Min(100m, ShippedQty / OrderQty * 100m) : 0m;
    }

    public sealed record OtdCustomerRow(string CustomerId, string? CustomerCode, string? CustomerName, string? CustomerNameEn, int Orders);

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
                   wo.WoNumber, wo.WoStatus,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
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
                   wo.WoNumber, wo.WoStatus,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
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

    // ── PP-003 계획 확정 — WO 미생성 확정 수주 후보 (납기·고객 필터) ───────
    public List<PlanLineRow> ListPlanCandidates(string customerId, DateTime? dueFrom, DateTime? dueTo, int take = 500)
    {
        var sql = $$"""
            SELECT TOP ({{take}}) s.SoID, s.SoNumber, s.SoLineNo, s.CustomerID,
                   s.ItemNo, i.ItemName,
                   ISNULL(s.OrderQty,0) AS OrderQty,
                   ISNULL(fg.OnHand,0)  AS FgOnHand,
                   s.RequestedDeliveryDate AS DueDate,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists,
                   i.RoutingType,
                   ln.LineID AS LineId,
                   CASE WHEN ml.DailyCap > 0
                        THEN CAST(ld.OpenLoad * 100.0 / (ml.DailyCap * 7) AS INT)
                        ELSE NULL END AS LineLoadPct
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            LEFT JOIN dbo.PP_WorkOrder wo ON wo.SoID = s.SoID AND wo.Status <> 'Cancelled'
            OUTER APPLY (SELECT SUM(f.Qty) AS OnHand FROM dbo.FG_Stock f
                         WHERE f.ItemNo = s.ItemNo AND f.Status NOT IN ('SHIPPED','SCRAPPED')) fg
            OUTER APPLY (SELECT TOP 1 r.LineID FROM dbo.PP_WorkOrderRouting r
                         JOIN dbo.PP_WorkOrder w2 ON w2.WoID = r.WoID
                         WHERE w2.ItemNo = s.ItemNo AND r.LineID IS NOT NULL
                         ORDER BY w2.CreatedTS DESC, r.StepSeq) ln
            LEFT JOIN dbo.MD_Line ml ON ml.LineID = ln.LineID
            OUTER APPLY (SELECT ISNULL(SUM(w3.OpenQty),0) AS OpenLoad FROM dbo.PP_WorkOrder w3
                         WHERE EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r3
                                       WHERE r3.WoID = w3.WoID AND r3.LineID = ln.LineID)
                           AND w3.Status IN ('Draft','Planned','Released','In Progress')) ld
            WHERE  s.Status = 'Confirmed' AND wo.WoID IS NULL
               AND (@Cust = '' OR s.CustomerID = @Cust)
               AND (@From IS NULL OR s.RequestedDeliveryDate >= @From)
               AND (@To   IS NULL OR s.RequestedDeliveryDate <= @To)
            ORDER BY s.RequestedDeliveryDate, s.SoNumber, s.SoLineNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Cust", SqlDbType.VarChar, 20).Value = customerId ?? "";
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = (object?)dueFrom ?? DBNull.Value;
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = (object?)dueTo   ?? DBNull.Value;
        using var rdr = cmd.ExecuteReader();
        var list = new List<PlanLineRow>();
        while (rdr.Read())
            list.Add(new PlanLineRow(
                (int)rdr["SoID"], rdr["SoNumber"] as string, rdr["SoLineNo"] as int?, rdr["CustomerID"] as string,
                rdr["ItemNo"] as string ?? "", rdr["ItemName"] as string,
                rdr.GetDecimal(rdr.GetOrdinal("OrderQty")),
                rdr.GetDecimal(rdr.GetOrdinal("FgOnHand")),
                rdr["DueDate"] as DateTime?, (int)rdr["ItemExists"] == 1,
                rdr["LineId"] as string, rdr["LineLoadPct"] as int?,
                rdr["RoutingType"] as string));
        return list;
    }

    /// <summary>
    /// PP-003 선택 확정 수주 → Draft 작업지시 일괄 생성. 확정·품목마스터 존재·라우팅 지정·WO 미생성
    /// (취소 WO 제외) 건만 삽입. useNetReq면 수량 = max(0, 수주 − FG재고), 0 이하 건 skip.
    /// WoNumber = WO-yyyyMMdd-NNN. 생성된 WoNumber 목록 반환.
    /// </summary>
    public List<string> CreateWorkOrdersForOrders(IReadOnlyList<int> soIds, string actor, bool useNetReq = false)
    {
        var created = new List<string>();
        if (soIds.Count == 0) return created;

        var prefix = $"WO-{DateTime.Today:yyyyMMdd}-";
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var seq = NextWoSeq(conn, tx, prefix);
            using var ins = BuildInsertWoForOrder(conn, tx, actor, useNetReq);
            foreach (var soId in soIds)
            {
                var wo = $"{prefix}{(seq + 1):D3}";
                if (ExecInsertWoForOrder(ins, wo, soId) is not null) { created.Add(wo); seq++; }
            }
            tx.Commit();
            return created;
        }
        catch { tx.Rollback(); throw; }
    }

    // ── PP-003 계획 확정 + 단계별 라인 스케줄 ─────────────────────────────
    /// <summary>라인 필수 단계 하나의 배치 계획 — 어느 라인, 어느 날, 몇 분.</summary>
    public sealed record StepPlan(int StepSeq, string LineId, DateTime Date, int DurationMin);
    public sealed record OrderPlan(int SoId, IReadOnlyList<StepPlan> Steps);
    public sealed record UnplacedStep(string WoNumber, int StepSeq, string LineId, DateTime Date);
    public sealed record ScheduledCreateResult(List<string> Created, int PlacedSteps, List<UnplacedStep> Unplaced);

    /// <summary>
    /// PP-003 일괄 생성(스케줄 포함). 수주별로 WO 생성 → Release(단계 행, 계획의 라인) → 단계마다 (라인, 일자)에
    /// 빈 자리를 찾아 PP_LineSchedule 슬롯(DRAFT) 추가. 전체가 한 트랜잭션이라 라인 검증 실패는 배치 전체 롤백.
    /// 자리가 없는 단계는 슬롯 없이 Unplaced 로 보고하고 WO 는 그대로 Released 로 남긴다.
    /// 슬롯은 같은 (라인, 일자)의 기존 WO 슬롯 뒤에 이어 붙이고, 같은 날 뒤 단계는 앞 단계 슬롯 종료 이후에 놓는다.
    /// </summary>
    public ScheduledCreateResult CreateScheduledWorkOrders(IReadOnlyList<OrderPlan> plans, string actor, bool useNetReq = false)
    {
        var created  = new List<string>();
        var unplaced = new List<UnplacedStep>();
        int placed   = 0;
        if (plans.Count == 0) return new(created, placed, unplaced);

        var prefix = $"WO-{DateTime.Today:yyyyMMdd}-";
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var seq = NextWoSeq(conn, tx, prefix);
            using var ins = BuildInsertWoForOrder(conn, tx, actor, useNetReq);
            foreach (var plan in plans)
            {
                var wo = $"{prefix}{(seq + 1):D3}";
                if (ExecInsertWoForOrder(ins, wo, plan.SoId) is not (int woId, decimal qty)) continue;
                seq++;
                created.Add(wo);

                var template = WorkOrderRepository.ReadPreview(conn, tx, woId);
                var choices  = template.Select(t => new WorkOrderRepository.StepLineChoice(
                        t.StepSeq, plan.Steps.FirstOrDefault(s => s.StepSeq == t.StepSeq)?.LineId))
                    .ToList();
                if (WorkOrderRepository.ReleaseCore(conn, tx, woId, choices, actor) == 0)
                    throw new InvalidOperationException($"{wo}: release failed.");

                (DateTime Date, int End)? prev = null;
                foreach (var step in plan.Steps.OrderBy(s => s.StepSeq))
                {
                    var date = step.Date.Date;
                    var cap  = LineScheduleRepository.ReadDayCapacity(conn, tx, step.LineId, date);
                    int? notBefore = cap.LastWoEnd;
                    if (prev is { } p && p.Date == date)
                        notBefore = Later(cap.DayStart, notBefore, p.End);

                    if (SlotPacker.Place(cap.OperatingBands, cap.Occupied, step.DurationMin, cap.DayStart, notBefore) is not { } slot)
                    {
                        unplaced.Add(new UnplacedStep(wo, step.StepSeq, step.LineId, date));
                        continue;
                    }
                    LineScheduleRepository.AppendWoSlot(conn, tx, step.LineId, date, cap.PatternId,
                                                        woId, slot.StartMin, slot.EndMin, qty, actor);
                    placed++;
                    prev = (date, slot.EndMin);
                }
            }
            tx.Commit();
            return new(created, placed, unplaced);
        }
        catch { tx.Rollback(); throw; }
    }

    // 축(dayStart 기준)에서 더 늦은 시각
    static int Later(int dayStart, int? a, int b)
    {
        if (a is not int av) return b;
        int Axis(int m) { int r = (m - dayStart) % 1440; return r < 0 ? r + 1440 : r; }
        return Axis(av) >= Axis(b) ? av : b;
    }

    const string InsertWoForOrderSql = """
        INSERT INTO dbo.PP_WorkOrder
               (WoNumber, SoID, ItemNo, OrderQty, OpenQty, DueDate, RoutingType, Status, CreatedBy, CreatedTS)
        OUTPUT INSERTED.WoID, INSERTED.OrderQty
        SELECT @Wo, s.SoID, s.ItemNo, q.Qty, q.Qty, s.RequestedDeliveryDate, i.RoutingType,
               'Draft', @Actor, SYSDATETIME()
        FROM   dbo.PP_CustomerOrder s
        JOIN   dbo.MD_Item i ON i.ItemNo = s.ItemNo
        OUTER APPLY (SELECT SUM(f.Qty) AS OnHand FROM dbo.FG_Stock f
                     WHERE f.ItemNo = s.ItemNo AND f.Status NOT IN ('SHIPPED','SCRAPPED')) fg
        CROSS APPLY (SELECT CASE WHEN @UseNet = 1
                                 THEN IIF(ISNULL(s.OrderQty,0) > ISNULL(fg.OnHand,0),
                                          ISNULL(s.OrderQty,0) - ISNULL(fg.OnHand,0), 0)
                                 ELSE ISNULL(s.OrderQty,0) END AS Qty) q
        WHERE  s.SoID = @SoID AND s.Status = 'Confirmed' AND q.Qty > 0
           AND i.RoutingType IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.PP_WorkOrder w
                           WHERE w.SoID = s.SoID AND w.Status <> 'Cancelled');
        """;

    static SqlCommand BuildInsertWoForOrder(SqlConnection conn, SqlTransaction tx, string actor, bool useNetReq)
    {
        var ins = new SqlCommand(InsertWoForOrderSql, conn, tx);
        ins.Parameters.Add("@Wo",     SqlDbType.VarChar, 20);
        ins.Parameters.Add("@SoID",   SqlDbType.Int);
        ins.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;
        ins.Parameters.Add("@UseNet", SqlDbType.Bit).Value = useNetReq;
        return ins;
    }

    /// <summary>1행 삽입되면 (WoID, 수량). 조건 미충족(미확정·라우팅 없음·WO 기존재·수량 0)이면 null.</summary>
    static (int WoId, decimal Qty)? ExecInsertWoForOrder(SqlCommand ins, string wo, int soId)
    {
        ins.Parameters["@Wo"].Value   = wo;
        ins.Parameters["@SoID"].Value = soId;
        using var rdr = ins.ExecuteReader();
        if (!rdr.Read()) return null;
        return ((int)rdr["WoID"], rdr.GetDecimal(rdr.GetOrdinal("OrderQty")));
    }

    /// <summary>
    /// 접두사 내 마지막 채번 번호. UPDLOCK/HOLDLOCK으로 트랜잭션 종료까지 범위를 잠가
    /// 동시 생성 시 중복 채번을 방지한다 (WoNumber 유니크 인덱스가 최종 방어선).
    /// </summary>
    internal static int NextWoSeq(SqlConnection conn, SqlTransaction tx, string prefix)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(WoNumber, LEN(@P) + 1, 10) AS INT)), 0)
            FROM   dbo.PP_WorkOrder WITH (UPDLOCK, HOLDLOCK)
            WHERE  WoNumber LIKE @P + '%';
            """, conn, tx);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 20).Value = prefix;
        return (int)cmd.ExecuteScalar();
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
                   (SELECT STRING_AGG(CAST(COALESCE(r.LineID, r.ProcessCode + N'(—)') AS nvarchar(40)), N' → ')
                               WITHIN GROUP (ORDER BY r.StepSeq)
                    FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID) AS RouteLines,
                   w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
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
                   (SELECT STRING_AGG(CAST(COALESCE(r.LineID, r.ProcessCode + N'(—)') AS nvarchar(40)), N' → ')
                               WITHIN GROUP (ORDER BY r.StepSeq)
                    FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID) AS RouteLines,
                   w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
            FROM   dbo.PP_WorkOrder w
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  (@LineID IS NULL
                    OR EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID AND r.LineID = @LineID))
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
    // CreatedBy 는 NOT NULL·기본값 없음 — 빠뜨리면 INSERT 자체가 실패한다.
    public void AddDowntimeEvent(string lineId, DateTime startTs, DateTime endTs,
        string? reasonCode, string? causeCode, string? comment, int? woId = null, string? createdBy = null)
    {
        const string sql = """
            INSERT INTO dbo.PP_LineDowntimeLog
                   (LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment, WoID, LoggedBy, CreatedBy, CreatedTS)
            VALUES (@LineId, @Start, @End,
                    DATEDIFF(minute, @Start, @End),
                    @Reason, @Cause, @Comment, @WoId, @By, @By, SYSDATETIME());
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
        cmd.Parameters.Add("@By",      SqlDbType.VarChar,   50).Value  = string.IsNullOrWhiteSpace(createdBy) ? "web" : createdBy;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-ODM/DTL 비가동 사유 수정/보완 (사유 미입력 또는 오기입 수정). 감사 컬럼도 함께 남긴다.</summary>
    public void UpdateDowntimeReason(int downtimeId, string? reasonCode, string? causeCode, string? comment, string? modifiedBy = null)
    {
        const string sql = """
            UPDATE dbo.PP_LineDowntimeLog
            SET ReasonCode = @Reason,
                CauseCode  = @Cause,
                Comment    = @Comment,
                ModifiedBy = @By,
                ModifiedTS = SYSDATETIME()
            WHERE DowntimeID = @Id;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Id",      SqlDbType.Int).Value            = downtimeId;
        cmd.Parameters.Add("@Reason",  SqlDbType.VarChar,   30).Value  = (object?)reasonCode ?? DBNull.Value;
        cmd.Parameters.Add("@Cause",   SqlDbType.VarChar,   30).Value  = (object?)causeCode  ?? DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value  = (object?)comment    ?? DBNull.Value;
        cmd.Parameters.Add("@By",      SqlDbType.NVarChar, 450).Value  = string.IsNullOrWhiteSpace(modifiedBy) ? "web" : modifiedBy;
        cmd.ExecuteNonQuery();
    }

    /// <summary>PP-DTL 사유 필터 옵션 — 공통코드 그룹이 없어 실데이터의 DISTINCT 값을 쓴다.</summary>
    public List<string> ListDowntimeReasons()
    {
        const string sql = """
            SELECT DISTINCT ReasonCode FROM dbo.PP_LineDowntimeLog
            WHERE ReasonCode IS NOT NULL ORDER BY ReasonCode;
            """;
        return Query(sql, r => (string)r["ReasonCode"]);
    }

    /// <summary>가장 최근 비가동 시작 시각 — 기본 조회 기간을 데이터가 있는 구간으로 맞추는 용도.</summary>
    public DateTime? LatestDowntimeStart()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT MAX(StartTS) FROM dbo.PP_LineDowntimeLog;", conn);
        return cmd.ExecuteScalar() is DateTime d ? d : null;
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

    public sealed record LineStateTotalRow(string? LineId, string? State, string? PlanState, int Minutes);

    /// <summary>[from, to] 구간의 분단위 상태 — 모니터 타임라인용. 기준시각을 넘겨 과거 시점 재생도 가능.</summary>
    public List<LineStateRow> ListLineStatesWindow(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT LineID, MinuteTS, State, PlanState, ISNULL(RunFlag, 0) AS RunFlag, WoID
            FROM   dbo.PP_LineStateLog
            WHERE  LineID IS NOT NULL AND MinuteTS >= @F AND MinuteTS <= @T
            ORDER BY LineID, MinuteTS;
            """;
        return Query(sql, MapLineState, ("@F", from), ("@T", to));
    }

    /// <summary>기준시각 이전 라인별 최종 로그 1건 — 로그가 오래돼도 마지막 상태·시각을 보여주기 위함.</summary>
    public List<LineStateRow> ListLatestLineStates(DateTime asOf)
    {
        const string sql = """
            SELECT LineID, MinuteTS, State, PlanState, ISNULL(RunFlag, 0) AS RunFlag, WoID
            FROM  (SELECT LineID, MinuteTS, State, PlanState, RunFlag, WoID,
                          ROW_NUMBER() OVER (PARTITION BY LineID ORDER BY MinuteTS DESC) AS rn
                   FROM   dbo.PP_LineStateLog
                   WHERE  LineID IS NOT NULL AND MinuteTS <= @T) x
            WHERE  rn = 1
            ORDER BY LineID;
            """;
        return Query(sql, MapLineState, ("@T", asOf));
    }

    /// <summary>[from, to] 구간 라인×상태×계획상태 분 합계 — 당일 누적(가동/비가동/유휴/계획정지)용.</summary>
    public List<LineStateTotalRow> ListLineStateTotals(DateTime from, DateTime to)
    {
        const string sql = """
            SELECT LineID, State, PlanState, COUNT(*) AS Minutes
            FROM   dbo.PP_LineStateLog
            WHERE  LineID IS NOT NULL AND MinuteTS >= @F AND MinuteTS <= @T
            GROUP  BY LineID, State, PlanState;
            """;
        return Query(sql, r => new LineStateTotalRow(
            r["LineID"] as string, r["State"] as string, r["PlanState"] as string, Convert.ToInt32(r["Minutes"])),
            ("@F", from), ("@T", to));
    }

    static LineStateRow MapLineState(IDataReader r) => new(
        r["LineID"] as string, r["MinuteTS"] as DateTime?,
        r["State"] as string, r["PlanState"] as string,
        Convert.ToBoolean(r["RunFlag"]), r["WoID"] as int?);

    // ── PP-OTD On-Time Delivery ─────────────────────────────────────────
    // 공통 SELECT — 고객은 마스터 ID/코드 양쪽으로 조인해 정규화, 지연은 '미출하 + 요청납기 경과' 로만 산정
    const string OtdSelect = """
        SELECT o.SoID, o.SoNumber, o.SoLineNo,
               ISNULL(c.CustomerID, o.CustomerID) AS CustomerID, c.CustomerCode, c.CustomerName, c.CustomerNameEn,
               o.ItemNo, i.ItemName, i.ItemNameEN,
               ISNULL(o.OrderQty,0)   AS OrderQty,
               ISNULL(o.ShippedQty,0) AS ShippedQty,
               o.OrderDate, o.RequestedDeliveryDate, o.PromisedDate,
               CASE WHEN ISNULL(o.ShippedQty,0) < ISNULL(o.OrderQty,0) AND o.RequestedDeliveryDate < @Today
                    THEN DATEDIFF(day, o.RequestedDeliveryDate, @Today) ELSE 0 END AS DaysLate,
               DATEDIFF(day, o.RequestedDeliveryDate, o.PromisedDate) AS PromiseGapDays,
               ISNULL(o.Status,'?') AS Status
        FROM   dbo.PP_CustomerOrder o
        LEFT JOIN dbo.MD_Customer c ON c.CustomerID = o.CustomerID OR c.CustomerCode = o.CustomerID
        LEFT JOIN dbo.MD_Item     i ON i.ItemNo = o.ItemNo
        """;

    static OtdRow MapOtd(IDataReader r) => new(
        (int)r["SoID"], r["SoNumber"] as string, r["SoLineNo"] as int?,
        r["CustomerID"] as string, r["CustomerCode"] as string, r["CustomerName"] as string, r["CustomerNameEn"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string, r["ItemNameEN"] as string,
        Convert.ToDecimal(r["OrderQty"]), Convert.ToDecimal(r["ShippedQty"]),
        r["OrderDate"] as DateTime?, r["RequestedDeliveryDate"] as DateTime?, r["PromisedDate"] as DateTime?,
        Convert.ToInt32(r["DaysLate"]),
        r["PromiseGapDays"] is DBNull ? null : Convert.ToInt32(r["PromiseGapDays"]),
        r["Status"] as string ?? "?");

    /// <summary>홈 대시보드·API 용: 요청납기가 최근 N일 이후인 수주.</summary>
    public List<OtdRow> ListOtd(int daysBack = 30)
    {
        const string sql = OtdSelect + """

            WHERE  o.RequestedDeliveryDate > DATEADD(day, -@D, @Today)
            ORDER BY o.RequestedDeliveryDate;
            """;
        return Query(sql, MapOtd, ("@D", daysBack), ("@Today", DateTime.Today));
    }

    /// <summary>PP-OTD 필터 조회 — 기간·고객(정규화 ID)·상태 조합.</summary>
    public List<OtdRow> ListOtdFiltered(
        DateTime? from       = null,
        DateTime? to         = null,
        string?  customerId  = null,
        string?  status      = null,   // "shipped" | "late" | "open" | null=all
        int      limit       = 500)
    {
        var sql = OtdSelect.Replace("SELECT o.SoID", "SELECT TOP (@Limit) o.SoID") + $"""

            WHERE  1=1
            {(from       != null ? "AND o.RequestedDeliveryDate >= @From "                         : "")}
            {(to         != null ? "AND o.RequestedDeliveryDate <  @To "                           : "")}
            {(customerId != null ? "AND ISNULL(c.CustomerID, o.CustomerID) = @Customer "           : "")}
            {(status == "shipped" ? "AND ISNULL(o.ShippedQty,0) >= ISNULL(o.OrderQty,0) "          : "")}
            {(status == "late"    ? "AND ISNULL(o.ShippedQty,0) <  ISNULL(o.OrderQty,0) AND o.RequestedDeliveryDate < @Today " : "")}
            {(status == "open"    ? "AND ISNULL(o.ShippedQty,0) <  ISNULL(o.OrderQty,0) AND (o.RequestedDeliveryDate IS NULL OR o.RequestedDeliveryDate >= @Today) " : "")}
            ORDER BY o.RequestedDeliveryDate DESC, o.SoNumber, o.SoLineNo;
            """;
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Limit", SqlDbType.Int).Value  = limit;
        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = DateTime.Today;
        if (from       != null) cmd.Parameters.Add("@From",     SqlDbType.Date).Value        = from.Value.Date;
        if (to         != null) cmd.Parameters.Add("@To",       SqlDbType.Date).Value        = to.Value.Date.AddDays(1);
        if (customerId != null) cmd.Parameters.Add("@Customer", SqlDbType.VarChar, 30).Value = customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<OtdRow>();
        while (rdr.Read()) list.Add(MapOtd(rdr));
        return list;
    }

    /// <summary>PP-OTD 고객 필터 옵션 — 마스터 ID 로 정규화(원본의 ID/코드 혼용을 하나로 묶는다).</summary>
    public List<OtdCustomerRow> ListOtdCustomers()
    {
        const string sql = """
            SELECT ISNULL(c.CustomerID, o.CustomerID) AS CustomerID,
                   MAX(c.CustomerCode) AS CustomerCode, MAX(c.CustomerName) AS CustomerName, MAX(c.CustomerNameEn) AS CustomerNameEn,
                   COUNT(*) AS Orders
            FROM   dbo.PP_CustomerOrder o
            LEFT JOIN dbo.MD_Customer c ON c.CustomerID = o.CustomerID OR c.CustomerCode = o.CustomerID
            WHERE  o.CustomerID IS NOT NULL
            GROUP  BY ISNULL(c.CustomerID, o.CustomerID)
            ORDER  BY 1;
            """;
        return Query(sql, r => new OtdCustomerRow(
            (string)r["CustomerID"], r["CustomerCode"] as string, r["CustomerName"] as string, r["CustomerNameEn"] as string,
            Convert.ToInt32(r["Orders"])));
    }

    /// <summary>요청납기의 최소/최대 — 기본 조회 기간을 데이터가 있는 구간으로 맞추는 용도.</summary>
    public (DateTime? Min, DateTime? Max) OtdDateExtent()
    {
        using var conn = _f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT MIN(RequestedDeliveryDate), MAX(RequestedDeliveryDate) FROM dbo.PP_CustomerOrder;", conn);
        using var rdr  = cmd.ExecuteReader();
        if (!rdr.Read()) return (null, null);
        return (rdr[0] as DateTime?, rdr[1] as DateTime?);
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
        r["WoNumber"] as string, r["WoStatus"] as string,
        (int)r["ItemExists"] == 1);
    private static WoLite MapWoLite(IDataReader r) => new(
        (int)r["WoID"], r["WoNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("CompletedQty")),
        r["RouteLines"] as string, r["DueDate"] as DateTime?,
        r["Status"] as string, r["ReleasedAt"] as DateTime?);
}
