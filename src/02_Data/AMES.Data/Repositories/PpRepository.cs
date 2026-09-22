using System.Data;
using AMES.Data.Connection;
using AMES.Data.Scheduling;
using AMES.Data.Services;
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
        string? WoNumber, string? WoStatus, bool ItemExists, int WoCount = 0);

    public sealed record CustomerOrderImportRow(string SoNumber, int? SoLineNo, string ItemNo,
        decimal OrderQty, decimal ShippedQty, DateTime? OrderDate, DateTime? RequestedDeliveryDate);

    public sealed record SupplyPlanRow(int PlanId, string? PlanCode, DateTime? PlanPeriod,
        string? Status, int LineCount, decimal TotalPlannedQty);

    // PP-003 계획 검토 라인 — WO 미생성 확정 수주 + FG 재고 + 라인 부하.
    // LineLoadPct = 그 라인의 앞으로 7일(근무일만) PP_LineSchedule WO 슬롯 분 ÷ 가동 분 — 스케줄러가 실제로 채운 시간 기준.
    public sealed record PlanLineRow(int SoId, string? SoNumber, int? SoLineNo, string? CustomerId,
        string ItemNo, string? ItemName, decimal OrderQty, decimal FgOnHand, DateTime? DueDate, bool ItemExists,
        string? LineId, int? LineLoadPct, string? RoutingType, decimal IssuedQty = 0)
    {
        /// <summary>아직 WO 로 발행되지 않은 수량. 분할 발행(같은 수주에 WO 여러 개) 뒤에도 후보에 남는 기준.</summary>
        public decimal RemainQty => Math.Max(0m, OrderQty - IssuedQty);
        public decimal NetReq => Math.Max(0m, OrderQty - FgOnHand - IssuedQty);
        public bool NoRouting => ItemExists && RoutingType is null;
        // 품목마스터 미완성(BR-PP-001) 또는 라우팅 미지정 → WO 생성 불가
        public bool Blocked => !ItemExists || RoutingType is null;
    }

    public sealed record MrpRunRow(int MrpRunId, DateTime? RunAt, DateTime? HorizonStart,
        DateTime? HorizonEnd, int WosConsidered, int PrsCreated, int ShortageCount,
        int DurationMs, string? Status);

    public sealed record PrRow(int PrId, string? PrNumber, string ItemNo, string? ItemName,
        string? VendorId, decimal RequiredQty, DateTime? RequiredDate, string? Status,
        string? SapPoNumber,
        int? WoId = null, string? WoNumber = null, string? ApprovedBy = null, DateTime? ApprovedAt = null,
        string? CreatedBy = null, DateTime? CreatedTs = null,
        string? SapDocNum = null, DateTime? SentAt = null, int RetryCount = 0, string? LastError = null,
        string? Uom = null);

    /// <summary>PP_PRSendLog 한 행 — Result = Sent/Failed/Approved, Message = 실패 사유·PO 번호 등 응답 요약.</summary>
    public sealed record PrSendLogRow(long SendLogId, int AttemptNo, DateTime? SentAt, string? Result, string? Message, string? By);

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
                   wo.WoNumber, wo.WoStatus, ISNULL(wo.WoCount,0) AS WoCount,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            OUTER APPLY (SELECT TOP 1 w.WoNumber, w.Status AS WoStatus,
                                (SELECT COUNT(*) FROM dbo.PP_WorkOrder x WHERE x.SoID = s.SoID AND x.Status <> 'Cancelled') AS WoCount
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
                   wo.WoNumber, wo.WoStatus, ISNULL(wo.WoCount,0) AS WoCount,
                   CASE WHEN i.ItemNo IS NULL THEN 0 ELSE 1 END AS ItemExists
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            OUTER APPLY (SELECT TOP 1 w.WoNumber, w.Status AS WoStatus,
                                (SELECT COUNT(*) FROM dbo.PP_WorkOrder x WHERE x.SoID = s.SoID AND x.Status <> 'Cancelled') AS WoCount
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
                   ISNULL(iss.Qty,0) AS IssuedQty
            FROM   dbo.PP_CustomerOrder s
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            OUTER APPLY (SELECT SUM(ISNULL(w.OrderQty,0)) AS Qty FROM dbo.PP_WorkOrder w
                         WHERE w.SoID = s.SoID AND w.Status <> 'Cancelled') iss
            OUTER APPLY (SELECT SUM(f.Qty) AS OnHand FROM dbo.FG_Inventory f
                         WHERE f.ItemNo = s.ItemNo AND f.Status NOT IN ('SHIPPED','SCRAPPED')) fg
            OUTER APPLY (SELECT TOP 1 r.LineID FROM dbo.PP_WorkOrderRouting r
                         JOIN dbo.PP_WorkOrder w2 ON w2.WoID = r.WoID
                         WHERE w2.ItemNo = s.ItemNo AND r.LineID IS NOT NULL
                         ORDER BY w2.CreatedTS DESC, r.StepSeq) ln
            WHERE  s.Status = 'Confirmed' AND ISNULL(s.OrderQty,0) - ISNULL(iss.Qty,0) > 0   -- 잔량 있는 수주(분할 발행 가능)
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
                rdr["LineId"] as string, null,
                rdr["RoutingType"] as string,
                rdr.GetDecimal(rdr.GetOrdinal("IssuedQty"))));
        rdr.Close();

        var load = ReadWeekLoad(conn, list.Select(r => r.LineId).OfType<string>().Distinct());
        return list.Select(r => r.LineId is string l && load.TryGetValue(l, out var pct) ? r with { LineLoadPct = pct } : r).ToList();
    }

    public const int LoadWindowDays = 7;

    /// <summary>
    /// 라인별 주간 부하 — 오늘부터 7일 중 근무일의 WO 슬롯 분 ÷ 가동 분(PM 제외). 능력 해석은 보드·스케줄러와 같은
    /// ReadDayCapacity 를 쓴다. 예전 "열린 WO 수량 ÷ DailyCap×7" 은 사이클을 무시해 사이클이 긴 품번의 부하가 보이지 않았다.
    /// 가동 분이 0 이면 null.
    /// </summary>
    static Dictionary<string, int?> ReadWeekLoad(SqlConnection conn, IEnumerable<string> lineIds)
    {
        var lines = lineIds.ToList();
        var map   = new Dictionary<string, int?>();
        if (lines.Count == 0) return map;

        var today = ReadNow(conn, null).Date;
        var cal   = new WorkdayCalendar(ReadCalendar(conn, null, today, today.AddDays(LoadWindowDays)));
        foreach (var line in lines)
        {
            int operating = 0, wo = 0;
            for (var d = today; d < today.AddDays(LoadWindowDays); d = d.AddDays(1))
            {
                if (!cal.IsWorkday(d)) continue;
                var cap = LineScheduleRepository.ReadDayCapacity(conn, null, line, d);
                operating += cap.OperatingMin;
                wo        += cap.WoLoadMin;
            }
            map[line] = operating > 0 ? (int)(wo * 100L / operating) : null;
        }
        return map;
    }

    /// <summary>
    /// PP-003 선택 확정 수주 → Draft 작업지시 일괄 생성. 확정·품목마스터 존재·라우팅 지정 건만 삽입.
    /// 수량 = (useNetReq ? 수주 − FG재고 : 수주) − 기발행 WO 수량(취소 제외). 0 이하 건 skip — 같은 수주에 WO 를 나눠 낼 수 있다.
    /// WoNumber = WO-yyyyMMdd-NNN. 생성된 WoNumber 목록 반환.
    /// </summary>
    public List<string> CreateWorkOrdersForOrders(IReadOnlyList<int> soIds, string actor, bool useNetReq = false)
    {
        var created = new List<string>();
        if (soIds.Count == 0) return created;

        var prefix = $"WO-{DbClock.Today:yyyyMMdd}-";
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            var seq = NextWoSeq(conn, tx, prefix);
            using var ins = BuildInsertWoForOrder(conn, tx, actor, useNetReq);
            foreach (var soId in soIds)
            {
                var wo = $"{prefix}{(seq + 1):D3}";
                if (ExecInsertWoForOrder(ins, wo, soId, null) is not null) { created.Add(wo); seq++; }
            }
            tx.Commit();
            return created;
        }
        catch { tx.Rollback(); throw; }
    }

    // ── PP-003 계획 확정 + 마감일 기준 자동 배치 ─────────────────────────
    /// <summary>라인 필수 단계 하나의 라인 선택. 날짜·분은 패커가 정한다.</summary>
    public sealed record StepChoice(int StepSeq, string LineId);
    /// <param name="Qty">직접 입력 수량. null 이면 useNetReq 기준(순수요/수주량)에서 기발행분을 뺀 잔량. 수주량 초과 허용.</param>
    public sealed record OrderPlan(int SoId, IReadOnlyList<StepChoice> Steps, decimal? Qty = null);

    public sealed record OrderOutcome(string WoNumber, int SoId, DateTime? Deadline, DateTime? DueDate,
                                      IReadOnlyList<DeadlinePacker.Placement> Placements,
                                      IReadOnlyList<DeadlinePacker.StepShortfall> Shortfalls,
                                      IReadOnlyList<DeadlinePacker.MoldChange> MoldChanges)
    {
        public DateTime? FirstDate => Placements.Count == 0 ? null : Placements.Min(p => p.Date);
        public DateTime? LastDate  => Placements.Count == 0 ? null : Placements.Max(p => p.Date);
        public decimal   LateQty   => Placements.Where(p => p.Late).Sum(p => p.Qty);
        public decimal   ShortQty  => Shortfalls.Sum(s => s.Qty);
    }

    /// <summary>계획에서 제외된 수주. Reason = RejectNoMold(INJ 단계 품번에 금형 매핑 없음).</summary>
    public sealed record RejectedOrder(int SoId, string? SoNumber, string? ItemNo, string Reason);
    public const string RejectNoMold = "NoMold";

    public sealed record ScheduledCreateResult(List<OrderOutcome> Orders, List<RejectedOrder> Rejected)
    {
        public int Created       => Orders.Count;
        public int Late          => Orders.Count(o => o.LateQty  > 0);
        public int Short         => Orders.Count(o => o.ShortQty > 0);
        public int RejectedCount => Rejected.Count;
    }

    public const string BufferWorkdaysKey = "PP_PROD_BUFFER_WORKDAYS";
    public const int    BufferWorkdaysDefault = 3;

    /// <summary>
    /// PP-003 일괄 생성(스케줄 포함). 수주를 납기 오름차순으로 돌며 WO 생성(ProdDeadline = 납기 − 버퍼 근무일) →
    /// Release(단계 행, 계획의 라인) → DeadlinePacker 가 정한 슬롯을 PP_LineSchedule(DRAFT) 에 추가.
    /// 전체가 한 트랜잭션이라 라인 검증 실패는 배치 전체 롤백. 자리가 모자란 수량은 Shortfall 로 보고하고 WO 는 Released 로 남긴다.
    /// startDate 가 오늘보다 뒤면 그 날부터 배치(테스트·미래 계획용), 아니면 서버 현재 시각 이후부터.
    /// INJ 단계는 MoldResolver 로 금형을 정하고 직전 금형과 다르면 EntryType='MC' 행을 슬롯 앞에 넣으며,
    /// 활성 MD_MoldItem 이 없는 품번의 수주는 WO 를 만들지 않고 Rejected(NoMold) 로 돌려준다.
    /// </summary>
    public ScheduledCreateResult CreateScheduledWorkOrders(IReadOnlyList<OrderPlan> plans, string actor,
                                                           bool useNetReq = false, DateTime? startDate = null)
    {
        var orders   = new List<OrderOutcome>();
        var rejected = new List<RejectedOrder>();
        if (plans.Count == 0) return new(orders, rejected);

        var prefix = $"WO-{DbClock.Today:yyyyMMdd}-";
        using var conn = _f.OpenConnection();
        // PP-003 다이얼로그 미리보기도 같은 메서드로 읽으므로 둘의 값이 반드시 일치해야 한다.
        int bufferDays = new SysRepository(_f).GetConfigInt(BufferWorkdaysKey, BufferWorkdaysDefault);
        using var tx = conn.BeginTransaction();
        try
        {
            var now   = ReadNow(conn, tx);
            var today = now.Date;
            int nowMin = now.Hour * 60 + now.Minute;
            if (startDate is { } sd && sd.Date > today) { today = sd.Date; nowMin = 0; }

            var so       = ReadOrderKeys(conn, tx, plans.Select(p => p.SoId));
            var lastDue  = so.Values.Select(v => v.Due).Where(d => d is not null).DefaultIfEmpty(today).Max() ?? today;
            var calEnd   = lastDue > today.AddDays(61) ? lastDue.AddDays(1) : today.AddDays(61);
            var cal      = new WorkdayCalendar(ReadCalendar(conn, tx, today.AddDays(-1), calEnd));
            var dailyCap = ReadDailyCap(conn, tx);
            var days = new DeadlinePacker.DayStateCache(
                (line, date) => LineScheduleRepository.ReadDayCapacity(conn, tx, line, date),
                (line, date) => LineScheduleRepository.LineLastMoldBefore(conn, tx, line, date));

            // 순방향 탐욕 채움은 순서에 민감하다 — 급한 납기부터(EDD). 그리드 정렬과 무관하게 서버가 정한다.
            var ordered = plans
                .OrderBy(p => so.TryGetValue(p.SoId, out var k) && k.Due is DateTime d ? d : DateTime.MaxValue)
                .ThenBy(p => so.TryGetValue(p.SoId, out var k) ? k.SoNumber : null)
                .ThenBy(p => so.TryGetValue(p.SoId, out var k) ? k.SoLineNo : null)
                .ToList();

            var seq = NextWoSeq(conn, tx, prefix);
            using var ins = BuildInsertWoForOrder(conn, tx, actor, useNetReq);
            foreach (var plan in ordered)
            {
                if (so.GetValueOrDefault(plan.SoId) is not { } key) continue;
                DateTime? deadline = key.Due is DateTime due ? cal.SubtractWorkdays(due, bufferDays) : null;

                // 템플릿은 WO 생성 전에 품목·라우팅으로 읽는다 — 금형 거부는 WO 를 만들기 전에 판정해야 한다
                var template = key.ItemNo is null || key.RoutingType is null
                    ? new List<WorkOrderRepository.RoutingStepPreview>()
                    : WorkOrderRepository.ReadPreview(conn, tx, key.ItemNo, key.RoutingType);
                var molds = ResolveMolds(conn, tx, key.ItemNo, template, plan.Steps, days, today);
                if (molds.Values.Any(m => m is null))
                {
                    rejected.Add(new RejectedOrder(plan.SoId, key.SoNumber, key.ItemNo, RejectNoMold));
                    continue;
                }

                var wo = $"{prefix}{(seq + 1):D3}";
                if (ExecInsertWoForOrder(ins, wo, plan.SoId, deadline, plan.Qty) is not (int woId, decimal qty)) continue;
                seq++;

                var choices  = template.Select(t => new WorkOrderRepository.StepLineChoice(
                        t.StepSeq, plan.Steps.FirstOrDefault(s => s.StepSeq == t.StepSeq)?.LineId))
                    .ToList();
                if (WorkOrderRepository.ReleaseCore(conn, tx, woId, choices, actor) == 0)
                    throw new InvalidOperationException($"{wo}: release failed.");

                var demands = plan.Steps.OrderBy(s => s.StepSeq).Select(s =>
                    {
                        var mold = molds.GetValueOrDefault(s.StepSeq);
                        return new DeadlinePacker.StepDemand(
                            s.StepSeq, s.LineId, qty,
                            template.FirstOrDefault(t => t.StepSeq == s.StepSeq)?.StdCycleSec,
                            dailyCap.GetValueOrDefault(s.LineId),
                            mold?.MoldId, mold?.ChangeMin ?? 0);
                    })
                    .ToList();

                var packed = DeadlinePacker.Pack(demands, today, nowMin, deadline, key.Due, cal, days);
                foreach (var m in packed.MoldChanges)
                    LineScheduleRepository.AppendMoldChangeSlot(conn, tx, m.LineId, m.Date, days.Get(m.LineId, m.Date).PatternId,
                                                                woId, m.FromMoldId, m.ToMoldId, m.StartMin, m.EndMin, actor);
                foreach (var p in packed.Placements)
                    LineScheduleRepository.AppendWoSlot(conn, tx, p.LineId, p.Date, days.Get(p.LineId, p.Date).PatternId,
                                                        woId, p.StartMin, p.EndMin, p.Qty, p.MoldId, actor);

                orders.Add(new OrderOutcome(wo, plan.SoId, deadline, key.Due, packed.Placements, packed.Shortfalls, packed.MoldChanges));
            }
            tx.Commit();
            return new(orders, rejected);
        }
        catch { tx.Rollback(); throw; }
    }

    static DateTime ReadNow(SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand("SELECT SYSDATETIME();", conn, tx);
        return (DateTime)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// INJ 단계마다 금형을 정한다(키 = StepSeq). 후보가 없으면 값이 null — 호출부가 그 수주를 거부한다.
    /// 직전 금형은 그 라인의 "오늘 꼬리"(이미 배치된 앞 수주 포함) 기준. 미리보기(PlanConfirmBatchDialog)도 같은 규칙.
    /// </summary>
    static Dictionary<int, MoldResolver.MoldCandidate?> ResolveMolds(SqlConnection conn, SqlTransaction tx, string? itemNo,
        List<WorkOrderRepository.RoutingStepPreview> template, IReadOnlyList<StepChoice> steps,
        DeadlinePacker.IDayState days, DateTime today)
    {
        var map = new Dictionary<int, MoldResolver.MoldCandidate?>();
        if (itemNo is null) return map;
        foreach (var s in steps)
        {
            var t = template.FirstOrDefault(x => x.StepSeq == s.StepSeq);
            if (t is null || !MoldResolver.NeedsMold(t.ProcessCode)) continue;
            var cands = MasterDataRepository.ReadMoldCandidates(conn, tx, itemNo, s.LineId);
            map[s.StepSeq] = MoldResolver.Choose(cands, days.LastMoldId(s.LineId, today));
        }
        return map;
    }

    sealed record OrderKey(DateTime? Due, string? SoNumber, int? SoLineNo, string? ItemNo, string? RoutingType);

    static Dictionary<int, OrderKey> ReadOrderKeys(SqlConnection conn, SqlTransaction tx, IEnumerable<int> soIds)
    {
        var ids = soIds.Distinct().ToList();
        var map = new Dictionary<int, OrderKey>();
        if (ids.Count == 0) return map;
        // ids 는 정수 ID 만 이어 붙이므로 인젝션 여지가 없다.
        var sql = $"""
            SELECT s.SoID, s.RequestedDeliveryDate, s.SoNumber, s.SoLineNo, s.ItemNo, i.RoutingType
            FROM   dbo.PP_CustomerOrder s LEFT JOIN dbo.MD_Item i ON i.ItemNo = s.ItemNo
            WHERE  s.SoID IN ({string.Join(",", ids)});
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            map[(int)rdr["SoID"]] = new OrderKey(rdr["RequestedDeliveryDate"] as DateTime?, rdr["SoNumber"] as string,
                                                 rdr["SoLineNo"] as int?, rdr["ItemNo"] as string, rdr["RoutingType"] as string);
        return map;
    }

    static List<(DateTime Date, string? DayType)> ReadCalendar(SqlConnection conn, SqlTransaction? tx, DateTime from, DateTime to)
    {
        using var cmd = new SqlCommand("""
            SELECT CalendarDate, DayType FROM dbo.SYS_FactoryCalendar
            WHERE  CalendarDate BETWEEN @From AND @To AND CalendarDate IS NOT NULL;
            """, conn, tx);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = from.Date;
        cmd.Parameters.Add("@To",   SqlDbType.Date).Value = to.Date;
        using var rdr = cmd.ExecuteReader();
        var list = new List<(DateTime, string?)>();
        while (rdr.Read()) list.Add(((DateTime)rdr["CalendarDate"], rdr["DayType"] as string));
        return list;
    }

    static Dictionary<string, int?> ReadDailyCap(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = new SqlCommand("SELECT LineID, DailyCap FROM dbo.MD_Line;", conn, tx);
        using var rdr = cmd.ExecuteReader();
        var map = new Dictionary<string, int?>();
        while (rdr.Read()) map[(string)rdr["LineID"]] = rdr["DailyCap"] is int dc ? dc : null;
        return map;
    }

    const string InsertWoForOrderSql = """
        INSERT INTO dbo.PP_WorkOrder
               (WoNumber, SoID, ItemNo, OrderQty, OpenQty, DueDate, ProdDeadline, RoutingType, Status, CreatedBy, CreatedTS)
        OUTPUT INSERTED.WoID, INSERTED.OrderQty
        SELECT @Wo, s.SoID, s.ItemNo, q.Qty, q.Qty, s.RequestedDeliveryDate, @Deadline, i.RoutingType,
               'Draft', @Actor, SYSDATETIME()
        FROM   dbo.PP_CustomerOrder s
        JOIN   dbo.MD_Item i ON i.ItemNo = s.ItemNo
        OUTER APPLY (SELECT SUM(f.Qty) AS OnHand FROM dbo.FG_Inventory f
                     WHERE f.ItemNo = s.ItemNo AND f.Status NOT IN ('SHIPPED','SCRAPPED')) fg
        OUTER APPLY (SELECT SUM(ISNULL(w.OrderQty,0)) AS Qty FROM dbo.PP_WorkOrder w
                     WHERE w.SoID = s.SoID AND w.Status <> 'Cancelled') iss
        -- 수량: 직접 입력(@Qty) 우선. 아니면 기준(순수요/수주량)에서 기발행분을 뺀 잔량 — 같은 수주에 WO 를 나눠 낼 수 있다
        CROSS APPLY (SELECT COALESCE(@Qty,
                            CASE WHEN @UseNet = 1
                                 THEN ISNULL(s.OrderQty,0) - ISNULL(fg.OnHand,0) - ISNULL(iss.Qty,0)
                                 ELSE ISNULL(s.OrderQty,0) - ISNULL(iss.Qty,0) END) AS Qty) q
        WHERE  s.SoID = @SoID AND s.Status = 'Confirmed' AND q.Qty > 0
           AND i.RoutingType IS NOT NULL;
        """;

    static SqlCommand BuildInsertWoForOrder(SqlConnection conn, SqlTransaction tx, string actor, bool useNetReq)
    {
        var ins = new SqlCommand(InsertWoForOrderSql, conn, tx);
        ins.Parameters.Add("@Wo",       SqlDbType.VarChar, 20);
        ins.Parameters.Add("@SoID",     SqlDbType.Int);
        ins.Parameters.Add("@Deadline", SqlDbType.Date);
        ins.Parameters.Add("@Actor",    SqlDbType.NVarChar, 450).Value = actor;
        ins.Parameters.Add("@UseNet",   SqlDbType.Bit).Value = useNetReq;
        var qtyP = ins.Parameters.Add("@Qty", SqlDbType.Decimal); qtyP.Precision = 18; qtyP.Scale = 3; qtyP.Value = DBNull.Value;
        return ins;
    }

    /// <summary>1행 삽입되면 (WoID, 수량). 조건 미충족(미확정·라우팅 없음·수량 0 이하)이면 null. qty 는 직접 입력 오버라이드.</summary>
    static (int WoId, decimal Qty)? ExecInsertWoForOrder(SqlCommand ins, string wo, int soId, DateTime? deadline, decimal? qty = null)
    {
        ins.Parameters["@Wo"].Value       = wo;
        ins.Parameters["@SoID"].Value     = soId;
        ins.Parameters["@Deadline"].Value = deadline is { } d ? d.Date : DBNull.Value;
        ins.Parameters["@Qty"].Value      = qty is { } q ? q : DBNull.Value;
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

    // ── PP-005 MRP — run / latest snapshot / shortage PR ────────────────
    public sealed record MrpWoRef(int WoId, string? WoNumber, decimal Qty, DateTime? DueDate);

    /// <summary>실행 스냅샷 한 행. PR 이 연결된 행은 부족이 발주중으로 옮겨져 <see cref="IsShort"/> 가 아니다.</summary>
    public sealed record MrpMaterialRow(int MrpRunId, string ItemNo, string? ItemName, string? Uom,
        decimal Required, decimal Stock, decimal OnOrder, decimal Shortage, int? LeadTimeDays, DateTime? OrderDue,
        int? PrId, string? PrNumber, IReadOnlyList<MrpWoRef> Wos)
    {
        public bool IsShort => Shortage > 0 && PrId is null;
    }

    public sealed record MrpSnapshot(MrpRunRow Run, List<MrpMaterialRow> Materials);
    public sealed record MrpPrCreated(string ItemNo, int PrId, string PrNumber);

    /// <summary>PR 생성 요청. Qty 가 null 이면 결과 행의 부족량을 그대로 쓴다.</summary>
    public sealed record MrpPrRequest(string ItemNo, decimal? Qty = null);

    /// <summary>열린 WO 상태 — Completed/Closed/Stocked/Cancelled 는 자재 수요가 없다.</summary>
    const string MrpOpenWoWhere = "ISNULL(w.Status,'Draft') NOT IN ('Completed','Closed','Stocked','Cancelled') AND w.ItemNo IS NOT NULL";

    /// <summary>
    /// MRP 실행: 열린 WO 잔량 → 유효 APPROVED BOM 분해 → 재고·발주중 대조 → PP_MRPLog + PP_MRPResult(Wo) 스냅샷.
    /// 계산 규칙은 <see cref="MrpCalculator"/>. BOM 순환이면 Failed 로그만 남기고 <see cref="MrpCalculator.MrpCycleException"/>.
    /// </summary>
    public int RunMrp(string runBy)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var today = DbClock.Today;

        var demands = Query($"""
            SELECT w.WoID, w.ItemNo, ISNULL(w.OrderQty,0) - ISNULL(w.CompletedQty,0) AS Qty, w.DueDate
            FROM   dbo.PP_WorkOrder w
            WHERE  {MrpOpenWoWhere};
            """, r => new MrpCalculator.Demand((int)r["WoID"], (string)r["ItemNo"],
                r.GetDecimal(r.GetOrdinal("Qty")), r["DueDate"] as DateTime?));

        // 부모 품번마다 유효(APPROVED·기간 내) 버전 중 EffFrom 최신 하나만 쓴다 — 버전이 겹치면 이중 계상되기 때문
        var bom = Query("""
            WITH eff AS (
                SELECT v.VersionID, v.EffFrom
                FROM   dbo.MD_BomVersion v
                WHERE  v.Status = 'APPROVED'
                  AND (v.EffFrom IS NULL OR v.EffFrom <= @Today)
                  AND (v.EffTo   IS NULL OR v.EffTo   >= @Today)),
            lines AS (
                SELECT b.ParentItemNo, b.CompItemNo, ISNULL(b.QtyPer,0) AS QtyPer, ISNULL(b.ScrapPct,0) AS ScrapPct,
                       DENSE_RANK() OVER (PARTITION BY b.ParentItemNo ORDER BY e.EffFrom DESC, e.VersionID DESC) AS Rk
                FROM   dbo.MD_Bom b
                JOIN   eff e ON e.VersionID = b.VersionID
                WHERE  ISNULL(b.ActiveFlag,1) = 1 AND b.ParentItemNo IS NOT NULL AND b.CompItemNo IS NOT NULL)
            SELECT ParentItemNo, CompItemNo, QtyPer, ScrapPct FROM lines WHERE Rk = 1;
            """, r => new MrpCalculator.BomLine((string)r["ParentItemNo"], (string)r["CompItemNo"],
                r.GetDecimal(r.GetOrdinal("QtyPer")), r.GetDecimal(r.GetOrdinal("ScrapPct"))),
            ("@Today", today));

        var supply = Query("""
            SELECT i.ItemNo, i.LeadTimeDays,
                   (SELECT ISNULL(SUM(ISNULL(x.OnHandQty,0) - ISNULL(x.ReservedQty,0)),0)
                    FROM dbo.WH_Inventory x WHERE x.ItemNo = i.ItemNo) AS Stock,
                   (SELECT ISNULL(SUM(ISNULL(p.OrderQty,0) - ISNULL(p.ReceivedQty,0)),0)
                    FROM dbo.WH_PurchaseOrder p
                    WHERE p.ItemNo = i.ItemNo AND p.Status IN ('Open','Partial')
                      AND ISNULL(p.OrderQty,0) > ISNULL(p.ReceivedQty,0))
                 + (SELECT ISNULL(SUM(ISNULL(q.RequiredQty,0)),0)
                    FROM dbo.PP_PurchaseRequest q
                    WHERE q.ItemNo = i.ItemNo AND q.SapPoNumber IS NULL
                      AND q.Status IN ('Draft','Sent','Approved')) AS OnOrder
            FROM   dbo.MD_Item i
            WHERE  EXISTS (SELECT 1 FROM dbo.MD_Bom b WHERE b.CompItemNo = i.ItemNo);
            """, r => new MrpCalculator.Supply((string)r["ItemNo"],
                r.GetDecimal(r.GetOrdinal("Stock")), r.GetDecimal(r.GetOrdinal("OnOrder")),
                r["LeadTimeDays"] as int?))
            .ToDictionary(s => s.ItemNo, StringComparer.OrdinalIgnoreCase);

        MrpCalculator.Result result;
        try { result = MrpCalculator.Explode(demands, bom, supply, today); }
        catch (MrpCalculator.MrpCycleException)
        {
            InsertMrpLog(null, null, runBy, today, today, 0, 0, (int)sw.ElapsedMilliseconds, "Failed");
            throw;
        }

        var horizonEnd = demands.Where(d => d.Qty > 0).Select(d => d.DueDate).Max() ?? today;
        var shortCount = result.Materials.Count(m => m.IsShort);

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var runId = InsertMrpLog(conn, tx, runBy, today, horizonEnd, result.WosConsidered, shortCount,
                                 (int)sw.ElapsedMilliseconds, "Completed");

        using var ins = new SqlCommand("""
            INSERT INTO dbo.PP_MRPResult
                (MrpRunID, ItemNo, RequiredQty, StockQty, OnOrderQty, ShortageQty, LeadTimeDays, OrderDue, CreatedBy)
            VALUES (@R, @I, @Req, @Stock, @OnOrder, @Short, @Lt, @Due, @By);
            """, conn, tx);
        ins.Parameters.Add("@R",       SqlDbType.Int).Value = runId;
        ins.Parameters.Add("@I",       SqlDbType.VarChar, 20);
        ins.Parameters.Add("@Req",     SqlDbType.Decimal).Precision = 14; ins.Parameters["@Req"].Scale = 3;
        ins.Parameters.Add("@Stock",   SqlDbType.Decimal).Precision = 14; ins.Parameters["@Stock"].Scale = 3;
        ins.Parameters.Add("@OnOrder", SqlDbType.Decimal).Precision = 14; ins.Parameters["@OnOrder"].Scale = 3;
        ins.Parameters.Add("@Short",   SqlDbType.Decimal).Precision = 14; ins.Parameters["@Short"].Scale = 3;
        ins.Parameters.Add("@Lt",      SqlDbType.Int);
        ins.Parameters.Add("@Due",     SqlDbType.Date);
        ins.Parameters.Add("@By",      SqlDbType.VarChar, 50).Value = Trunc(runBy, 50);

        using var insWo = new SqlCommand("""
            INSERT INTO dbo.PP_MRPResultWo (MrpRunID, ItemNo, WoID, RequiredQty) VALUES (@R, @I, @W, @Q);
            """, conn, tx);
        insWo.Parameters.Add("@R", SqlDbType.Int).Value = runId;
        insWo.Parameters.Add("@I", SqlDbType.VarChar, 20);
        insWo.Parameters.Add("@W", SqlDbType.Int);
        insWo.Parameters.Add("@Q", SqlDbType.Decimal).Precision = 14; insWo.Parameters["@Q"].Scale = 3;

        foreach (var m in result.Materials)
        {
            ins.Parameters["@I"].Value       = m.ItemNo;
            ins.Parameters["@Req"].Value     = m.Required;
            ins.Parameters["@Stock"].Value   = m.Stock;
            ins.Parameters["@OnOrder"].Value = m.OnOrder;
            ins.Parameters["@Short"].Value   = m.Shortage;
            ins.Parameters["@Lt"].Value      = (object?)m.LeadTimeDays ?? DBNull.Value;
            ins.Parameters["@Due"].Value     = (object?)m.OrderDue ?? DBNull.Value;
            ins.ExecuteNonQuery();
            foreach (var w in m.Wos)
            {
                insWo.Parameters["@I"].Value = m.ItemNo;
                insWo.Parameters["@W"].Value = w.WoId;
                insWo.Parameters["@Q"].Value = w.Qty;
                insWo.ExecuteNonQuery();
            }
        }
        tx.Commit();
        return runId;
    }

    int InsertMrpLog(SqlConnection? conn, SqlTransaction? tx, string runBy, DateTime start, DateTime end,
        int wos, int shortages, int durationMs, string status)
    {
        const string sql = """
            INSERT INTO dbo.PP_MRPLog
                (RunAt, RunBy, HorizonStart, HorizonEnd, WosConsidered, PrsCreated, ShortageCount, DurationMs, Status, CreatedBy)
            OUTPUT INSERTED.MrpRunID
            VALUES (SYSDATETIME(), @RunBy, @S, @E, @Wos, 0, @Short, @Ms, @St, @By);
            """;
        using var own = conn is null ? _f.OpenConnection() : null;
        using var cmd = new SqlCommand(sql, conn ?? own, tx);
        cmd.Parameters.Add("@RunBy", SqlDbType.NVarChar, 450).Value = runBy;
        cmd.Parameters.Add("@S",     SqlDbType.Date).Value = start;
        cmd.Parameters.Add("@E",     SqlDbType.Date).Value = end;
        cmd.Parameters.Add("@Wos",   SqlDbType.Int).Value = wos;
        cmd.Parameters.Add("@Short", SqlDbType.Int).Value = shortages;
        cmd.Parameters.Add("@Ms",    SqlDbType.Int).Value = durationMs;
        cmd.Parameters.Add("@St",    SqlDbType.VarChar, 20).Value = status;
        cmd.Parameters.Add("@By",    SqlDbType.VarChar, 50).Value = Trunc(runBy, 50);
        return (int)cmd.ExecuteScalar();
    }

    static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>최근 Completed 실행의 스냅샷. 실행 이력이 없으면 null.</summary>
    public MrpSnapshot? GetLatestMrp()
    {
        var run = Query("""
            SELECT TOP (1) MrpRunID, RunAt, HorizonStart, HorizonEnd,
                   ISNULL(WosConsidered,0) AS WosConsidered, ISNULL(PrsCreated,0) AS PrsCreated,
                   ISNULL(ShortageCount,0) AS ShortageCount, ISNULL(DurationMs,0) AS DurationMs, Status
            FROM   dbo.PP_MRPLog
            WHERE  Status = 'Completed'
            ORDER BY MrpRunID DESC;
            """, r => new MrpRunRow((int)r["MrpRunID"], r["RunAt"] as DateTime?,
                r["HorizonStart"] as DateTime?, r["HorizonEnd"] as DateTime?,
                (int)r["WosConsidered"], (int)r["PrsCreated"], (int)r["ShortageCount"],
                (int)r["DurationMs"], r["Status"] as string)).FirstOrDefault();
        if (run is null) return null;

        var wos = Query("""
            SELECT rw.ItemNo, rw.WoID, w.WoNumber, rw.RequiredQty, w.DueDate
            FROM   dbo.PP_MRPResultWo rw
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = rw.WoID
            WHERE  rw.MrpRunID = @R
            ORDER BY ISNULL(w.DueDate,'9999-12-31'), rw.WoID;
            """, r => (ItemNo: (string)r["ItemNo"], Wo: new MrpWoRef((int)r["WoID"], r["WoNumber"] as string,
                r.GetDecimal(r.GetOrdinal("RequiredQty")), r["DueDate"] as DateTime?)),
            ("@R", run.MrpRunId))
            .GroupBy(x => x.ItemNo, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MrpWoRef>)g.Select(x => x.Wo).ToList(), StringComparer.OrdinalIgnoreCase);

        var materials = Query("""
            SELECT r.ItemNo, i.ItemName, i.DefaultUOM, r.RequiredQty, r.StockQty, r.OnOrderQty, r.ShortageQty,
                   r.LeadTimeDays, r.OrderDue, r.PrID, p.PrNumber
            FROM   dbo.PP_MRPResult r
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = r.ItemNo
            LEFT JOIN dbo.PP_PurchaseRequest p ON p.PrID = r.PrID
            WHERE  r.MrpRunID = @R
            ORDER BY CASE WHEN r.ShortageQty > 0 AND r.PrID IS NULL THEN 0 ELSE 1 END, r.ItemNo;
            """, r =>
            {
                var itemNo = (string)r["ItemNo"];
                return new MrpMaterialRow(run.MrpRunId, itemNo, r["ItemName"] as string, r["DefaultUOM"] as string,
                    r.GetDecimal(r.GetOrdinal("RequiredQty")), r.GetDecimal(r.GetOrdinal("StockQty")),
                    r.GetDecimal(r.GetOrdinal("OnOrderQty")), r.GetDecimal(r.GetOrdinal("ShortageQty")),
                    r["LeadTimeDays"] as int?, r["OrderDue"] as DateTime?,
                    r["PrID"] as int?, r["PrNumber"] as string,
                    wos.TryGetValue(itemNo, out var w) ? w : Array.Empty<MrpWoRef>());
            }, ("@R", run.MrpRunId));

        return new MrpSnapshot(run, materials);
    }

    /// <summary>부족량 그대로 PR 을 만든다. <see cref="CreateShortagePrs(int, IReadOnlyCollection{MrpPrRequest}, string)"/> 참조.</summary>
    public List<MrpPrCreated> CreateShortagePrs(int runId, IReadOnlyCollection<string> itemNos, string by)
        => CreateShortagePrs(runId, itemNos.Select(i => new MrpPrRequest(i)).ToList(), by);

    /// <summary>
    /// 요청 행마다 PP_PurchaseRequest(Draft, 수량 = 요청 수량(기본 부족량), 필요일 = 발주 기한 → 최단 WO 납기 → 오늘, WoID = 최단 납기 WO) 를 만들고
    /// 결과 행에 연결한다(요청 수량만큼 부족 → 발주중 이관, 부족량보다 적으면 잔여 부족이 남는다). 이미 PR 이 있거나
    /// 부족이 아닌 품번은 건너뛴다. 수량이 0 이하면 전체 거부. 한 트랜잭션.
    /// </summary>
    public List<MrpPrCreated> CreateShortagePrs(int runId, IReadOnlyCollection<MrpPrRequest> requests, string by)
    {
        var created = new List<MrpPrCreated>();
        if (requests.Count == 0) return created;
        foreach (var r in requests)
            if (r.Qty is <= 0) throw new ArgumentOutOfRangeException(nameof(requests), $"{r.ItemNo}: PR quantity must be positive.");

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        // WO-yyyyMMdd-NNN 과 같은 일별 채번 — 연 단위 3자리는 부족 자재마다 PR 이 나와 금방 소진된다
        var prefix = $"PR-{DbClock.Today:yyyyMMdd}-";
        var seq = NextPrSeq(conn, tx, prefix);

        foreach (var req in requests.DistinctBy(r => r.ItemNo, StringComparer.OrdinalIgnoreCase))
        {
            var itemNo = req.ItemNo;
            using var sel = new SqlCommand("""
                SELECT r.ShortageQty, r.OrderDue,
                       (SELECT TOP (1) rw.WoID FROM dbo.PP_MRPResultWo rw
                        LEFT JOIN dbo.PP_WorkOrder w ON w.WoID = rw.WoID
                        WHERE rw.MrpRunID = r.MrpRunID AND rw.ItemNo = r.ItemNo
                        ORDER BY ISNULL(w.DueDate,'9999-12-31'), rw.WoID) AS WoID,
                       (SELECT MIN(w.DueDate) FROM dbo.PP_MRPResultWo rw
                        JOIN dbo.PP_WorkOrder w ON w.WoID = rw.WoID
                        WHERE rw.MrpRunID = r.MrpRunID AND rw.ItemNo = r.ItemNo) AS WoDue
                FROM   dbo.PP_MRPResult r WITH (UPDLOCK, HOLDLOCK)
                WHERE  r.MrpRunID = @R AND r.ItemNo = @I AND r.PrID IS NULL AND r.ShortageQty > 0;
                """, conn, tx);
            sel.Parameters.Add("@R", SqlDbType.Int).Value = runId;
            sel.Parameters.Add("@I", SqlDbType.VarChar, 20).Value = itemNo;
            decimal shortage; DateTime? orderDue, woDue; int? woId;
            using (var rdr = sel.ExecuteReader())
            {
                if (!rdr.Read()) continue;
                shortage = rdr.GetDecimal(rdr.GetOrdinal("ShortageQty"));
                orderDue = rdr["OrderDue"] as DateTime?;
                woId     = rdr["WoID"] as int?;
                woDue    = rdr["WoDue"] as DateTime?;
            }

            var qty = req.Qty ?? shortage;
            var prNumber = prefix + (++seq).ToString("D4");
            using var ins = new SqlCommand("""
                INSERT INTO dbo.PP_PurchaseRequest (PrNumber, ItemNo, VendorID, RequiredQty, RequiredDate, WoID, Status, CreatedBy)
                OUTPUT INSERTED.PrID
                VALUES (@Pr, @I, NULL, @Qty, @Due, @Wo, 'Draft', @By);
                """, conn, tx);
            ins.Parameters.Add("@Pr",  SqlDbType.VarChar, 20).Value = prNumber;
            ins.Parameters.Add("@I",   SqlDbType.VarChar, 20).Value = itemNo;
            var q = ins.Parameters.Add("@Qty", SqlDbType.Decimal); q.Precision = 14; q.Scale = 3; q.Value = qty;
            ins.Parameters.Add("@Due", SqlDbType.Date).Value = (orderDue ?? woDue ?? DbClock.Today).Date;
            ins.Parameters.Add("@Wo",  SqlDbType.Int).Value = (object?)woId ?? DBNull.Value;
            ins.Parameters.Add("@By",  SqlDbType.VarChar, 50).Value = Trunc(by, 50);
            var prId = (int)ins.ExecuteScalar();

            using var upd = new SqlCommand("""
                UPDATE dbo.PP_MRPResult
                SET    PrID = @P, OnOrderQty = OnOrderQty + @Qty, ShortageQty = ShortageQty - @Qty,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  MrpRunID = @R AND ItemNo = @I;
                UPDATE dbo.PP_MRPLog SET PrsCreated = ISNULL(PrsCreated,0) + 1, ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  MrpRunID = @R;
                """, conn, tx);
            upd.Parameters.Add("@P",  SqlDbType.Int).Value = prId;
            var uq = upd.Parameters.Add("@Qty", SqlDbType.Decimal); uq.Precision = 14; uq.Scale = 3; uq.Value = qty;
            upd.Parameters.Add("@R",  SqlDbType.Int).Value = runId;
            upd.Parameters.Add("@I",  SqlDbType.VarChar, 20).Value = itemNo;
            upd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = by;
            upd.ExecuteNonQuery();

            created.Add(new MrpPrCreated(itemNo, prId, prNumber));
        }
        tx.Commit();
        return created;
    }

    /// <summary>
    /// 자재의 진행 중 구매요청 — PO 미전환(SapPoNumber 없음) Draft/Sent/Approved. RunMrp 가 발주중으로 세는 PR 과 같은 범위.
    /// </summary>
    public List<PrRow> ListOpenPrsForItem(string itemNo)
    {
        var sql = $"""
            SELECT {PrSelect}
            FROM   dbo.PP_PurchaseRequest p
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = p.ItemNo
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = p.WoID
            WHERE  p.ItemNo = @I AND p.SapPoNumber IS NULL AND p.Status IN ('Draft','Sent','Approved')
            ORDER BY ISNULL(p.RequiredDate, '9999-01-01'), p.PrID;
            """;
        return Query(sql, MapPr, ("@I", itemNo));
    }

    static PrRow MapPr(IDataReader r) => new(
        (int)r["PrID"], r["PrNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r["VendorID"] as string, r.GetDecimal(r.GetOrdinal("RequiredQty")),
        r["RequiredDate"] as DateTime?, PrStatusRules.Normalize(r["Status"] as string),
        r["SapPoNumber"] as string,
        r["WoID"] as int?, r["WoNumber"] as string, r["ApprovedBy"] as string, r["ApprovedAt"] as DateTime?,
        r["CreatedBy"] as string, r["CreatedTS"] as DateTime?,
        r["SapDocNum"] as string, r["SentAt"] as DateTime?, Convert.ToInt32(r["RetryCount"]), r["LastError"] as string,
        r["DefaultUOM"] as string);

    // ── PP-006 상태 전이 — 규칙은 PrStatusRules, 전이마다 PP_PRSendLog 1행, 한 트랜잭션 ─────────
    /// <summary>
    /// 선택한 PR 중 전송 가능한(Draft/Failed) 행을 Sent 로 올린다. docNum 은 단건 전송 때 입력한 SAP DocNum(없으면 유지).
    /// 전송 불가 상태는 건너뛰고, 실제로 전이된 PrID 만 돌려준다.
    /// </summary>
    public List<int> SendPrs(IReadOnlyCollection<int> prIds, string? docNum, string by)
    {
        var sent = new List<int>();
        if (prIds.Count == 0) return sent;
        docNum = string.IsNullOrWhiteSpace(docNum) ? null : docNum.Trim();

        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        foreach (var prId in prIds.Distinct())
        {
            var status = LockPrStatus(conn, tx, prId);
            if (status is null || !PrStatusRules.CanSend(status)) continue;

            using var upd = new SqlCommand("""
                UPDATE dbo.PP_PurchaseRequest
                SET    Status = 'Sent', SentAt = SYSDATETIME(), SapDocNum = COALESCE(@Doc, SapDocNum), LastError = NULL,
                       ModifiedBy = @By, ModifiedTS = SYSDATETIME()
                WHERE  PrID = @P;
                """, conn, tx);
            upd.Parameters.Add("@P",   SqlDbType.Int).Value = prId;
            upd.Parameters.Add("@Doc", SqlDbType.VarChar, 20).Value = (object?)docNum ?? DBNull.Value;
            upd.Parameters.Add("@By",  SqlDbType.NVarChar, 450).Value = by;
            upd.ExecuteNonQuery();
            InsertPrLog(conn, tx, prId, "Sent", docNum, by);
            sent.Add(prId);
        }
        tx.Commit();
        return sent;
    }

    /// <summary>전송 실패 기록: Failed 로 두고 RetryCount +1, 사유 저장. Approved 는 거부.</summary>
    public void FailPr(int prId, string reason, string by)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Failure reason is required.", nameof(reason));
        reason = reason.Trim();
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var status = LockPrStatus(conn, tx, prId) ?? throw new InvalidOperationException($"PR {prId} not found.");
        PrStatusRules.Next(status, PrStatusRules.PrAction.Fail);

        using var upd = new SqlCommand("""
            UPDATE dbo.PP_PurchaseRequest
            SET    Status = 'Failed', RetryCount = ISNULL(RetryCount,0) + 1, LastError = @Err,
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  PrID = @P;
            """, conn, tx);
        upd.Parameters.Add("@P",   SqlDbType.Int).Value = prId;
        upd.Parameters.Add("@Err", SqlDbType.NVarChar, 200).Value = reason.Length > 200 ? reason[..200] : reason;
        upd.Parameters.Add("@By",  SqlDbType.NVarChar, 450).Value = by;
        upd.ExecuteNonQuery();
        InsertPrLog(conn, tx, prId, "Failed", reason, by);
        tx.Commit();
    }

    /// <summary>SAP PO 생성 확인: Sent → Approved, PO 번호·승인자·승인시각 기록. PO 번호 필수.</summary>
    public void ApprovePr(int prId, string poNumber, string by)
    {
        if (string.IsNullOrWhiteSpace(poNumber)) throw new ArgumentException("PO number is required.", nameof(poNumber));
        poNumber = poNumber.Trim();
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var status = LockPrStatus(conn, tx, prId) ?? throw new InvalidOperationException($"PR {prId} not found.");
        PrStatusRules.Next(status, PrStatusRules.PrAction.Approve);

        using var upd = new SqlCommand("""
            UPDATE dbo.PP_PurchaseRequest
            SET    Status = 'Approved', SapPoNumber = @Po, ApprovedBy = @By, ApprovedAt = SYSDATETIME(),
                   ModifiedBy = @By, ModifiedTS = SYSDATETIME()
            WHERE  PrID = @P;
            """, conn, tx);
        upd.Parameters.Add("@P",  SqlDbType.Int).Value = prId;
        upd.Parameters.Add("@Po", SqlDbType.VarChar, 20).Value = poNumber;
        upd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = by;
        upd.ExecuteNonQuery();
        InsertPrLog(conn, tx, prId, "Approved", poNumber, by);
        tx.Commit();
    }

    /// <summary>거래처 지정/해제 — 전송 전(Draft/Failed)에만.</summary>
    public void UpdatePrVendor(int prId, string? vendorId, string by)
    {
        using var conn = _f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var status = LockPrStatus(conn, tx, prId) ?? throw new InvalidOperationException($"PR {prId} not found.");
        if (!PrStatusRules.CanSend(status)) throw new InvalidOperationException($"PR status '{status}' does not allow vendor change.");

        using var upd = new SqlCommand("""
            UPDATE dbo.PP_PurchaseRequest SET VendorID = @V, ModifiedBy = @By, ModifiedTS = SYSDATETIME() WHERE PrID = @P;
            """, conn, tx);
        upd.Parameters.Add("@P",  SqlDbType.Int).Value = prId;
        upd.Parameters.Add("@V",  SqlDbType.VarChar, 20).Value = string.IsNullOrWhiteSpace(vendorId) ? DBNull.Value : vendorId.Trim();
        upd.Parameters.Add("@By", SqlDbType.NVarChar, 450).Value = by;
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public List<PrSendLogRow> ListPrSendLog(int prId)
        => Query("""
            SELECT SendLogID, ISNULL(AttemptNo,0) AS AttemptNo, SentAt, Result, ResponsePayload, CreatedBy
            FROM   dbo.PP_PRSendLog
            WHERE  PrID = @P
            ORDER BY SendLogID;
            """, r => new PrSendLogRow((long)r["SendLogID"], Convert.ToInt32(r["AttemptNo"]), r["SentAt"] as DateTime?,
                r["Result"] as string, r["ResponsePayload"] as string, r["CreatedBy"] as string),
            ("@P", prId));

    /// <summary>행 잠금 후 현재 상태(정규화). 없으면 null.</summary>
    static string? LockPrStatus(SqlConnection conn, SqlTransaction tx, int prId)
    {
        using var cmd = new SqlCommand("SELECT Status FROM dbo.PP_PurchaseRequest WITH (UPDLOCK, ROWLOCK) WHERE PrID = @P;", conn, tx);
        cmd.Parameters.Add("@P", SqlDbType.Int).Value = prId;
        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? PrStatusRules.Normalize(rdr["Status"] as string) : null;
    }

    /// <summary>PP_PRSendLog 1행. AttemptNo 는 그 PR 의 이력 수 + 1. Endpoint 는 화면 확정임을 남긴다(연동 후 실제 URL).</summary>
    static void InsertPrLog(SqlConnection conn, SqlTransaction tx, int prId, string result, string? message, string by)
    {
        using var cmd = new SqlCommand("""
            INSERT INTO dbo.PP_PRSendLog (PrID, AttemptNo, SentAt, Endpoint, ResponsePayload, Result, CreatedBy)
            SELECT @P, ISNULL(MAX(AttemptNo),0) + 1, SYSDATETIME(), 'MANUAL', @Msg, @R, @By
            FROM   dbo.PP_PRSendLog WHERE PrID = @P;
            """, conn, tx);
        cmd.Parameters.Add("@P",   SqlDbType.Int).Value = prId;
        cmd.Parameters.Add("@Msg", SqlDbType.NVarChar, -1).Value = (object?)message ?? DBNull.Value;
        cmd.Parameters.Add("@R",   SqlDbType.VarChar, 20).Value = result;
        cmd.Parameters.Add("@By",  SqlDbType.VarChar, 50).Value = Trunc(by, 50);
        cmd.ExecuteNonQuery();
    }

    /// <summary>접두사(PR-yyyyMMdd-) 내 마지막 채번. NextWoSeq 와 같은 범위 잠금.</summary>
    static int NextPrSeq(SqlConnection conn, SqlTransaction tx, string prefix)
    {
        using var cmd = new SqlCommand("""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(PrNumber, LEN(@P) + 1, 10) AS INT)), 0)
            FROM   dbo.PP_PurchaseRequest WITH (UPDLOCK, HOLDLOCK)
            WHERE  PrNumber LIKE @P + '%';
            """, conn, tx);
        cmd.Parameters.Add("@P", SqlDbType.VarChar, 20).Value = prefix;
        return (int)cmd.ExecuteScalar();
    }

    // ── PP-006 Purchase Request ─────────────────────────────────────────
    /// <summary>구매요청 전체(topN 0) 또는 최근 N 건. 필요일 오름차순.</summary>
    public List<PrRow> ListPurchaseRequests(int topN = 0)
    {
        var top = topN > 0 ? $"TOP ({topN})" : "";
        var sql = $"""
            SELECT {top} {PrSelect}
            FROM   dbo.PP_PurchaseRequest p
            LEFT JOIN dbo.MD_Item      i ON i.ItemNo = p.ItemNo
            LEFT JOIN dbo.PP_WorkOrder w ON w.WoID   = p.WoID
            ORDER BY ISNULL(p.RequiredDate, '9999-01-01'), p.PrID DESC;
            """;
        return Query(sql, MapPr);
    }

    const string PrSelect = """
        p.PrID, p.PrNumber, p.ItemNo, i.ItemName, i.DefaultUOM,
               p.VendorID, ISNULL(p.RequiredQty,0) AS RequiredQty,
               p.RequiredDate, p.Status, p.SapPoNumber,
               p.WoID, w.WoNumber, p.ApprovedBy, p.ApprovedAt, p.CreatedBy, p.CreatedTS,
               p.SapDocNum, p.SentAt, ISNULL(p.RetryCount,0) AS RetryCount, p.LastError
        """;

    // ── Releasable WOs (Home KPI · API) — draft/planned first ───────────
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
        return Query(sql, MapOtd, ("@D", daysBack), ("@Today", DbClock.Today));
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
        cmd.Parameters.Add("@Today", SqlDbType.Date).Value = DbClock.Today;
        if (from       != null) cmd.Parameters.Add("@From",     SqlDbType.Date).Value        = from.Value.Date;
        if (to         != null) cmd.Parameters.Add("@To",       SqlDbType.Date).Value        = to.Value.Date.AddDays(1);
        if (customerId != null) cmd.Parameters.Add("@Customer", SqlDbType.VarChar, 30).Value = customerId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<OtdRow>();
        while (rdr.Read()) list.Add(MapOtd(rdr));
        return list;
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
        (int)r["ItemExists"] == 1, Convert.ToInt32(r["WoCount"]));
    private static WoLite MapWoLite(IDataReader r) => new(
        (int)r["WoID"], r["WoNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("CompletedQty")),
        r["RouteLines"] as string, r["DueDate"] as DateTime?,
        r["Status"] as string, r["ReleasedAt"] as DateTime?);
}
