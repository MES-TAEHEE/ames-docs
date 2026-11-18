using System.Data;
using AMES.Contracts.Dto;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public static decimal? EstimateShippingPallets(decimal quantity,int? perPallet,bool tote=false) =>
        quantity <= 0 ? 0 : !tote && perPallet is > 0 ? decimal.Ceiling(quantity/perPallet.Value) : null;

    /// <summary>출하 오더 한 건(번호로) — 작업지시 ↔ 출하 오더 연결 팝업용.</summary>
    public ShippingOrderDto? GetShippingOrder(string orderNo) => ListShippingOrders(orderNo:orderNo).FirstOrDefault();

    public List<ShippingOrderDto> ListShippingOrders(DateTime? from=null,DateTime? to=null,bool onlyOpen=false,bool byDeliveryDate=false,string? orderNo=null)
    {
        using var conn=_factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT O.OrderID,O.OrderNo,O.CustomerID,P.ShipDate,O.Status,O.DeliveryID,D.DeliveryNo,
                L.OrderLineID,L.SoID,S.SoNumber,L.PartNo,I.ItemName,L.Qty,I.DefaultUOM,I.PalletQty,COALESCE(I.ToteFlag,0) ToteFlag,S.RequestedDeliveryDate
            FROM dbo.FG_ShippingOrder O JOIN dbo.FG_ShippingOrderLine L ON L.OrderID=O.OrderID
            JOIN dbo.PP_CustomerOrder S ON S.SoID=L.SoID LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.PartNo
            LEFT JOIN dbo.FG_Delivery D ON D.DeliveryID=O.DeliveryID
            CROSS APPLY(VALUES(CASE WHEN @ByDeliveryDate=1 THEN COALESCE(D.ShipDate,O.ShipDate) ELSE O.ShipDate END)) P(ShipDate)
            WHERE (@From IS NULL OR P.ShipDate>=@From) AND (@To IS NULL OR P.ShipDate<=@To)
                AND (@OnlyOpen=0 OR O.Status IN('Open','Loading'))
                AND (@OrderNo IS NULL OR O.OrderNo=@OrderNo)
            ORDER BY P.ShipDate,O.OrderID,L.OrderLineID;
            """,conn);
        cmd.Parameters.Add("@From",SqlDbType.Date).Value=(object?)from?.Date??DBNull.Value;
        cmd.Parameters.Add("@To",SqlDbType.Date).Value=(object?)to?.Date??DBNull.Value;
        cmd.Parameters.Add("@OnlyOpen",SqlDbType.Bit).Value=onlyOpen;
        cmd.Parameters.Add("@ByDeliveryDate",SqlDbType.Bit).Value=byDeliveryDate;
        cmd.Parameters.Add("@OrderNo",SqlDbType.VarChar,30).Value=(object?)orderNo??DBNull.Value;
        using var r=cmd.ExecuteReader();var rows=new List<ShippingOrderDto>();
        while(r.Read())
        {
            var id=GetInt(r,"OrderID"); var order=rows.LastOrDefault();
            if(order?.OrderId!=id) { order=new(id,GetString(r,"OrderNo")!,GetString(r,"CustomerID")!,r.GetDateTime(r.GetOrdinal("ShipDate")),GetString(r,"Status")!,
                r["DeliveryID"] is int d?d:null,GetString(r,"DeliveryNo"),[]);rows.Add(order); }
            order.Lines.Add(new(GetInt(r,"OrderLineID"),GetInt(r,"SoID"),GetString(r,"SoNumber")??"",GetString(r,"PartNo")!,GetString(r,"ItemName"),
                GetDecimal(r,"Qty"),GetString(r,"DefaultUOM"),r["PalletQty"] is int p?p:null,Convert.ToBoolean(r["ToteFlag"]),GetDate(r,"RequestedDeliveryDate")));
        }
        return rows;
    }

    public List<ShippingDeliveryListDto> ListOpenShippingDeliveries()
    {
        using var conn=_factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT D.DeliveryID,D.DeliveryNo,D.CustomerID,D.ShipDate,D.TrailerNo,D.Status,
                (SELECT COUNT(*) FROM dbo.FG_ShippingOrder O WHERE O.DeliveryID=D.DeliveryID) AS OrderCount,
                (SELECT COUNT(DISTINCT L.PartNo) FROM dbo.FG_ShippingOrder O
                    JOIN dbo.FG_ShippingOrderLine L ON L.OrderID=O.OrderID
                    WHERE O.DeliveryID=D.DeliveryID) AS PartCount
            FROM dbo.FG_Delivery D
            WHERE D.Status IN('Open','Loading')
                AND EXISTS(SELECT 1 FROM dbo.FG_ShippingOrder O
                    JOIN dbo.FG_ShippingOrderLine L ON L.OrderID=O.OrderID WHERE O.DeliveryID=D.DeliveryID)
            ORDER BY D.ShipDate,D.DeliveryID;
            """,conn);
        using var r=cmd.ExecuteReader();
        var rows=new List<ShippingDeliveryListDto>();
        while(r.Read())
            rows.Add(new(GetInt(r,"DeliveryID"),GetString(r,"DeliveryNo")!,GetString(r,"CustomerID")!,
                r.GetDateTime(r.GetOrdinal("ShipDate")),GetString(r,"TrailerNo"),GetString(r,"Status")!,
                GetInt(r,"OrderCount"),GetInt(r,"PartCount")));
        return rows;
    }

    /// <summary>재고 부족을 승인하고 오더를 만들 때의 예외 승인 — 사유 필수(승인 권한 확인은 호출한 화면 몫, 기록은 감사 로그).</summary>
    /// <summary>PDA 적재(OutboundScan 52006)가 받는 위치 — 등록된 활성 FG 상세 위치(레벨 2). <paramref name="a"/> 는 WH_Inventory 별칭.</summary>
    internal static string LoadableLocation(string a)=>
        $"EXISTS(SELECT 1 FROM dbo.MD_Location LL WHERE LL.LocationID=NULLIF(LTRIM(RTRIM({a}.LocationNo)),'') COLLATE DATABASE_DEFAULT AND LL.LocationLevel=2 AND COALESCE(LL.ActiveFlag,1)=1 AND UPPER(ISNULL(LL.LocationType,''))='FG')";

    /// <summary>품번별 출하 오더 재고 — Loadable=적재 가능(FG 위치 지정), Unlocated=재고는 있으나 Set Location 전. 오더 생성 팝업의 표시용.</summary>
    public Dictionary<string,(decimal Loadable,int Pallets,decimal Unlocated)> ListShippableStock()
    {
        var sql=$$"""
            SELECT i.PartNo,
                   SUM(CASE WHEN {{LoadableLocation("i")}} THEN i.Qty ELSE 0 END) Loadable,
                   COUNT(DISTINCT CASE WHEN {{LoadableLocation("i")}} THEN i.PalletNo END) Pallets,
                   SUM(CASE WHEN {{LoadableLocation("i")}} THEN 0 ELSE i.Qty END) Unlocated
            FROM dbo.WH_Inventory i
            WHERE i.Qty>0 AND i.PartNo IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.LotNo=i.LotNo AND R.Status IN('Open','Inspecting'))
            GROUP BY i.PartNo;
            """;
        using var conn=_factory.OpenConnection();using var cmd=new SqlCommand(sql,conn){CommandTimeout=60};
        using var r=cmd.ExecuteReader();var d=new Dictionary<string,(decimal,int,decimal)>(StringComparer.OrdinalIgnoreCase);
        while(r.Read())d[r.GetString(0)]=(r.GetDecimal(1),r.GetInt32(2),r.GetDecimal(3));
        return d;
    }

    public sealed record StockOverride(string Reason);
    /// <summary>요청 수량이 출하 가능 재고를 넘는 품번 목록을 담아 오더 생성을 거부한다.</summary>
    public sealed class InsufficientStockException(List<(string Part,decimal Requested,decimal Available)> items)
        : InvalidOperationException(string.Join("; ",items.Select(x=>$"{x.Part}: insufficient finished-goods stock (available {x.Available:N0}, requested {x.Requested:N0})")))
    { public List<(string Part,decimal Requested,decimal Available)> Items{get;}=items; }

    /// <summary>재고 부족 승인으로 만든 출하 오더 번호 → 승인 사유(감사 로그 FG-05/CREATE_OVERRIDE). 목록 배지용.</summary>
    public Dictionary<string,string> ListStockOverrideReasons()
    {
        using var conn=_factory.OpenConnection();
        using var cmd=new SqlCommand("SELECT TargetID,Note FROM dbo.SYS_AuditLog WHERE ScreenCode='FG-05' AND ActionType='CREATE_OVERRIDE' AND Result='OK' ORDER BY EventTS",conn);
        using var r=cmd.ExecuteReader();var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        while(r.Read())if(r["TargetID"] is string no)d[no]=r["Note"] as string??"";
        return d;
    }

    public List<string> CreateShippingOrders(DateTime shipDate,IReadOnlyList<ShippingOrderInput> input,string actor,StockOverride? stockOverride=null)
    {
        CheckActor(actor);
        if(input.Count is <1 or >500 || input.Select(x=>x.SoId).Distinct().Count()!=input.Count || input.Any(x=>x.Qty<=0 || x.Qty>99999999999m || x.Qty!=decimal.Truncate(x.Qty)))
            throw new ArgumentException("Select SO lines and enter positive whole shipment quantities (maximum 500 lines).");
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable); Gate(conn,tx);
        var selected=new List<(int So,string Customer,string Part,decimal Qty)>();
        foreach(var line in input.OrderBy(x=>x.SoId))
        {
            using var cmd=new SqlCommand("""
                SELECT S.CustomerID,S.ItemNo,COALESCE(S.OrderQty,0)-COALESCE(S.ShippedQty,0)-COALESCE(R.Qty,0) Remaining
                FROM dbo.PP_CustomerOrder S WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.MD_Customer C ON C.CustomerID=S.CustomerID
                JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo AND COALESCE(I.ActiveFlag,1)=1
                OUTER APPLY(SELECT SUM(L.Qty) Qty FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
                    WHERE L.SoID=S.SoID AND O.Status IN('Open','Loading')) R
                WHERE S.SoID=@So AND UPPER(COALESCE(S.Status,'')) NOT IN('SHIPPED','CLOSED','CANCELLED','CANCELED');
                """,conn,tx);
            cmd.Parameters.Add("@So",SqlDbType.Int).Value=line.SoId;
            using var r=cmd.ExecuteReader();
            if(!r.Read() || r.IsDBNull(0) || line.Qty>GetDecimal(r,"Remaining")) throw new InvalidOperationException($"SO #{line.SoId}: quantity exceeds the unallocated SO balance, or the order is unavailable. Refresh and retry.");
            selected.Add((line.SoId,r.GetString(0),r.GetString(1),line.Qty));
        }
        // 재고 검사 — 품번별로 (완제품 재고 − 이미 열린 출하 오더가 잡은 수량)보다 많이 요청하면 오더를 만들지 않는다.
        // 재고 = 실을 수 있는 재고 — WH_Inventory 양수 수량 중 등록된 FG 상세 위치(레벨 2)에 지정된 것(반품 검사 대기 제외).
        // 위치 미지정 재고는 PDA 적재(52006)가 막으므로 세지 않는다(PP-003·APS 의 재고 요약과는 다른 기준). 승인(StockOverride, 사유 필수)이 있으면 통과시킨다.
        var lacking=new List<(string Part,decimal Requested,decimal Available)>();
        foreach(var part in selected.GroupBy(x=>x.Part,StringComparer.OrdinalIgnoreCase))
        {
            using var cmd=new SqlCommand($$"""
                SELECT (SELECT COALESCE(SUM(i.Qty),0) FROM dbo.WH_Inventory i
                        WHERE i.PartNo=@Part AND i.Qty>0 AND {{LoadableLocation("i")}}
                          AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.LotNo=i.LotNo AND R.Status IN('Open','Inspecting')))
                     - (SELECT COALESCE(SUM(L.Qty),0) FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
                        WHERE L.PartNo=@Part AND O.Status IN('Open','Loading')) AS Available;
                """,conn,tx);
            Add(cmd,"@Part",SqlDbType.VarChar,20,part.Key);
            var available=Math.Max(0,Convert.ToDecimal(cmd.ExecuteScalar()));
            var requested=part.Sum(x=>x.Qty);
            if(requested>available)lacking.Add((part.Key,requested,available));
        }
        if(lacking.Count>0)
        {
            if(stockOverride is null)throw new InsufficientStockException(lacking);
            if(string.IsNullOrWhiteSpace(stockOverride.Reason)||stockOverride.Reason.Trim().Length>200)throw new ArgumentException("A stock shortage approval needs a reason (1-200 characters).");
        }
        var numbers=new List<string>();
        foreach(var customer in selected.GroupBy(x=>x.Customer))
        {
            var number=NextNo(conn,tx,"ORD");int id;
            using(var cmd=new SqlCommand("INSERT dbo.FG_ShippingOrder(OrderNo,CustomerID,ShipDate,CreatedBy) OUTPUT inserted.OrderID VALUES(@No,@Customer,@Date,@Actor)",conn,tx))
            { Add(cmd,"@No",SqlDbType.VarChar,30,number);Add(cmd,"@Customer",SqlDbType.VarChar,20,customer.Key);Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);cmd.Parameters.Add("@Date",SqlDbType.Date).Value=shipDate.Date;id=(int)cmd.ExecuteScalar()!; }
            foreach(var row in customer)
            {
                using var cmd=new SqlCommand("INSERT dbo.FG_ShippingOrderLine(OrderID,SoID,PartNo,Qty) VALUES(@Order,@So,@Part,@Qty)",conn,tx);
                cmd.Parameters.Add("@Order",SqlDbType.Int).Value=id;cmd.Parameters.Add("@So",SqlDbType.Int).Value=row.So;
                Add(cmd,"@Part",SqlDbType.VarChar,20,row.Part);Qty(cmd,"@Qty",row.Qty);cmd.ExecuteNonQuery();
            }
            numbers.Add(number);
        }
        tx.Commit();return numbers;
    }

    /// <summary>
    /// 출하 오더 취소 — DCR 에 배정되지 않은 Open 오더만. 상태를 Cancelled 로 바꿔 수주 잔량·재고 예약(Open·Loading 만 센다)이 풀린다.
    /// 행을 지우지 않아 번호·라인 이력이 남는다. 사유·행위자는 호출한 화면이 감사 로그에 남긴다.
    /// </summary>
    public void CancelShippingOrder(int orderId,string actor)
    {
        CheckActor(actor);
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        using var cmd=new SqlCommand("UPDATE dbo.FG_ShippingOrder SET Status='Cancelled',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE OrderID=@Id AND Status='Open' AND DeliveryID IS NULL",conn,tx);
        cmd.Parameters.Add("@Id",SqlDbType.Int).Value=orderId;Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Only an open order that is not assigned to a DCR can be cancelled. Refresh and retry.");
        tx.Commit();
    }

    /// <summary>
    /// 출하 오더 수정 — DCR 에 배정되지 않은 Open 오더의 출하일과 라인별 수량. 수량 0 은 그 라인을 뺀다(전부 빼려면 취소).
    /// 수량을 올리는 라인은 생성 때와 같이 수주 잔량(다른 오더가 잡은 몫 제외)과 품번별 출하 가능 재고를 넘지 못한다.
    /// </summary>
    public void UpdateShippingOrder(int orderId,DateTime shipDate,IReadOnlyDictionary<int,decimal> lineQty,string actor)
    {
        CheckActor(actor);
        if(lineQty.Count is <1 or >500 || lineQty.Values.Any(q=>q<0 || q>99999999999m || q!=decimal.Truncate(q)))
            throw new ArgumentException("Enter whole, non-negative shipment quantities.");
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        using(var lockCmd=new SqlCommand("SELECT OrderID FROM dbo.FG_ShippingOrder WITH(UPDLOCK,HOLDLOCK) WHERE OrderID=@Id AND Status='Open' AND DeliveryID IS NULL",conn,tx))
        {
            lockCmd.Parameters.Add("@Id",SqlDbType.Int).Value=orderId;
            if(lockCmd.ExecuteScalar() is null)throw new InvalidOperationException("Only an open order that is not assigned to a DCR can be edited. Refresh and retry.");
        }
        var lines=new List<(int LineId,int SoId,string Part,decimal Old)>();
        using(var rd=new SqlCommand("SELECT OrderLineID,SoID,PartNo,Qty FROM dbo.FG_ShippingOrderLine WITH(UPDLOCK,HOLDLOCK) WHERE OrderID=@Id",conn,tx))
        {
            rd.Parameters.Add("@Id",SqlDbType.Int).Value=orderId;using var r=rd.ExecuteReader();
            while(r.Read())lines.Add((r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetDecimal(3)));
        }
        if(lineQty.Keys.Any(id=>lines.All(l=>l.LineId!=id)))throw new InvalidOperationException("The order lines changed. Refresh and retry.");
        var kept=lines.Where(l=>lineQty.GetValueOrDefault(l.LineId,l.Old)>0).ToList();
        if(kept.Count==0)throw new InvalidOperationException("An order needs at least one line with a quantity; cancel the order instead.");
        foreach(var l in kept)
        {
            var qty=lineQty.GetValueOrDefault(l.LineId,l.Old);
            if(qty<=l.Old)continue;   // 줄이는 것은 항상 가능
            using var cmd=new SqlCommand("""
                SELECT COALESCE(S.OrderQty,0)-COALESCE(S.ShippedQty,0)-COALESCE(R.Qty,0)
                FROM dbo.PP_CustomerOrder S WITH(UPDLOCK,HOLDLOCK)
                OUTER APPLY(SELECT SUM(X.Qty) Qty FROM dbo.FG_ShippingOrderLine X JOIN dbo.FG_ShippingOrder O ON O.OrderID=X.OrderID
                    WHERE X.SoID=S.SoID AND O.Status IN('Open','Loading') AND X.OrderLineID<>@Line) R
                WHERE S.SoID=@So;
                """,conn,tx);
            cmd.Parameters.Add("@So",SqlDbType.Int).Value=l.SoId;cmd.Parameters.Add("@Line",SqlDbType.Int).Value=l.LineId;
            if(cmd.ExecuteScalar() is not decimal remaining || qty>remaining)throw new InvalidOperationException($"{l.Part}: quantity exceeds the unallocated SO balance. Refresh and retry.");
        }
        var lacking=new List<(string Part,decimal Requested,decimal Available)>();
        foreach(var part in kept.GroupBy(x=>x.Part,StringComparer.OrdinalIgnoreCase))
        {
            if(part.All(l=>lineQty.GetValueOrDefault(l.LineId,l.Old)<=l.Old))continue;
            using var cmd=new SqlCommand($$"""
                SELECT (SELECT COALESCE(SUM(i.Qty),0) FROM dbo.WH_Inventory i
                        WHERE i.PartNo=@Part AND i.Qty>0 AND {{LoadableLocation("i")}}
                          AND NOT EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.LotNo=i.LotNo AND R.Status IN('Open','Inspecting')))
                     - (SELECT COALESCE(SUM(L.Qty),0) FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
                        WHERE L.PartNo=@Part AND O.Status IN('Open','Loading') AND L.OrderID<>@Order);
                """,conn,tx);
            Add(cmd,"@Part",SqlDbType.VarChar,20,part.Key);cmd.Parameters.Add("@Order",SqlDbType.Int).Value=orderId;
            var available=Math.Max(0,Convert.ToDecimal(cmd.ExecuteScalar()));
            var requested=part.Sum(l=>lineQty.GetValueOrDefault(l.LineId,l.Old));
            if(requested>available)lacking.Add((part.Key,requested,available));
        }
        if(lacking.Count>0)throw new InsufficientStockException(lacking);
        foreach(var l in lines)
        {
            var qty=lineQty.GetValueOrDefault(l.LineId,l.Old);
            if(qty<=0){using var del=new SqlCommand("DELETE dbo.FG_ShippingOrderLine WHERE OrderLineID=@L",conn,tx);del.Parameters.Add("@L",SqlDbType.Int).Value=l.LineId;del.ExecuteNonQuery();}
            else if(qty!=l.Old){using var up=new SqlCommand("UPDATE dbo.FG_ShippingOrderLine SET Qty=@Q WHERE OrderLineID=@L",conn,tx);up.Parameters.Add("@L",SqlDbType.Int).Value=l.LineId;Qty(up,"@Q",qty);up.ExecuteNonQuery();}
        }
        using(var hd=new SqlCommand("UPDATE dbo.FG_ShippingOrder SET ShipDate=@D,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE OrderID=@Id",conn,tx))
        {hd.Parameters.Add("@D",SqlDbType.Date).Value=shipDate.Date;hd.Parameters.Add("@Id",SqlDbType.Int).Value=orderId;Add(hd,"@Actor",SqlDbType.VarChar,20,actor);hd.ExecuteNonQuery();}
        tx.Commit();
    }

    internal static (string CustomerId, DateTime ShipDate) ShippingDeliveryHeader(IReadOnlyList<(string CustomerId, DateTime ShipDate)> orders)
    {
        if(orders.Count==0)throw new ArgumentException("Select at least one shipment order.");
        var customer=orders[0].CustomerId.Trim();
        if(orders.Any(o=>!string.Equals(customer,o.CustomerId.Trim(),StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Orders for different customers cannot be combined in one DCR.");
        return (customer,orders.Min(o=>o.ShipDate).Date);
    }

    public string CreateShippingDelivery(IReadOnlyList<int> orderIds,string? trailer,string actor)
    {
        CheckActor(actor);
        if(orderIds.Count is <1 or >100 || orderIds.Distinct().Count()!=orderIds.Count || trailer?.Length>50)throw new ArgumentException("Select 1–100 shipment orders and a trailer name within 50 characters.");
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        var selected=new List<(string CustomerId,DateTime ShipDate)>();
        foreach(var id in orderIds.Order())
        {
            using var read=new SqlCommand("SELECT CustomerID,ShipDate FROM dbo.FG_ShippingOrder WITH(UPDLOCK,HOLDLOCK) WHERE OrderID=@Id AND DeliveryID IS NULL AND Status='Open'",conn,tx);
            read.Parameters.Add("@Id",SqlDbType.Int).Value=id;using var r=read.ExecuteReader();
            if(!r.Read())throw new InvalidOperationException("An order is already assigned or no longer open. Refresh and retry.");
            selected.Add((r.GetString(0),r.GetDateTime(1)));
        }
        var (customer,date)=ShippingDeliveryHeader(selected);
        var number=NextNo(conn,tx,"DCR");int deliveryId;
        using(var cmd=new SqlCommand("INSERT dbo.FG_Delivery(DeliveryNo,CustomerID,ShipDate,TrailerNo,CreatedBy) OUTPUT inserted.DeliveryID VALUES(@No,@Customer,@Date,@Trailer,@Actor)",conn,tx))
        { Add(cmd,"@No",SqlDbType.VarChar,30,number);Add(cmd,"@Customer",SqlDbType.VarChar,20,customer);Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);Add(cmd,"@Trailer",SqlDbType.NVarChar,50,trailer);cmd.Parameters.Add("@Date",SqlDbType.Date).Value=date;deliveryId=(int)cmd.ExecuteScalar()!; }
        foreach(var id in orderIds)
        {
            using var cmd=new SqlCommand("UPDATE dbo.FG_ShippingOrder SET DeliveryID=@D,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE OrderID=@Id",conn,tx);
            cmd.Parameters.Add("@D",SqlDbType.Int).Value=deliveryId;cmd.Parameters.Add("@Id",SqlDbType.Int).Value=id;Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);cmd.ExecuteNonQuery();
        }
        tx.Commit();return number;
    }

    /// <summary>
    /// DCR 해제 — 아직 하나도 싣지 않은(Open) DCR 에서 오더를 뺀다. <paramref name="orderId"/> 가 있으면 그 오더만, 없으면 DCR 의 오더 전부.
    /// 풀린 오더는 DCR 미배정 Open 으로 돌아가 다시 묶거나 취소·수정할 수 있다. 오더가 하나도 남지 않으면 DCR 은 Cancelled 로 닫는다.
    /// 이미 실은 팔레트가 있으면(Loading) 막는다 — PDA 에서 내린(Unload) 뒤에 해제한다. 출하 완료는 되돌리지 않는다.
    /// </summary>
    public void ReleaseShippingDelivery(int deliveryId,string actor,int? orderId=null)
    {
        CheckActor(actor);
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        string? status;
        using(var st=new SqlCommand("SELECT Status FROM dbo.FG_Delivery WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryID=@D",conn,tx))
        {st.Parameters.Add("@D",SqlDbType.Int).Value=deliveryId;status=st.ExecuteScalar() as string;}
        if(status is null)throw new InvalidOperationException("Delivery not found. Refresh and retry.");
        if(status!="Open")throw new InvalidOperationException(status=="Loading"
            ?"Pallets are already loaded on this DCR. Unload them on the PDA first, then release the DCR."
            :$"A {status.ToLowerInvariant()} DCR cannot be released.");
        using(var ld=new SqlCommand("SELECT COUNT(*) FROM dbo.FG_DeliveryLoad WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryID=@D",conn,tx))
        {ld.Parameters.Add("@D",SqlDbType.Int).Value=deliveryId;if((int)ld.ExecuteScalar()!>0)throw new InvalidOperationException("Pallets are already loaded on this DCR. Unload them on the PDA first, then release the DCR.");}
        using(var rel=new SqlCommand("UPDATE dbo.FG_ShippingOrder SET DeliveryID=NULL,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE DeliveryID=@D AND Status='Open' AND (@O IS NULL OR OrderID=@O)",conn,tx))
        {
            rel.Parameters.Add("@D",SqlDbType.Int).Value=deliveryId;rel.Parameters.Add("@O",SqlDbType.Int).Value=(object?)orderId??DBNull.Value;Add(rel,"@Actor",SqlDbType.VarChar,20,actor);
            if(rel.ExecuteNonQuery()<1)throw new InvalidOperationException("The order is not on this open DCR. Refresh and retry.");
        }
        using(var close=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.FG_ShippingOrder WHERE DeliveryID=@D) UPDATE dbo.FG_Delivery SET Status='Cancelled' WHERE DeliveryID=@D",conn,tx))
        {close.Parameters.Add("@D",SqlDbType.Int).Value=deliveryId;close.ExecuteNonQuery();}
        tx.Commit();
    }

    public ShippingDeliveryDto GetShippingDelivery(string barcode)
    {
        using var conn=_factory.OpenConnection();return ReadDelivery(conn,null,barcode);
    }
    private static ShippingDeliveryDto ReadDelivery(SqlConnection conn,SqlTransaction? tx,string barcode)
    {
        if(string.IsNullOrWhiteSpace(barcode) || barcode.Length>30)throw new ArgumentException("Scan a DCR or shipment order barcode.");
        using var cmd=new SqlCommand("""
            SELECT D.* FROM dbo.FG_Delivery D
            WHERE D.DeliveryNo=@No OR D.DeliveryID=(SELECT O.DeliveryID FROM dbo.FG_ShippingOrder O WHERE O.OrderNo=@No);
            """,conn,tx);Add(cmd,"@No",SqlDbType.VarChar,30,barcode.Trim());
        ShippingDeliveryDto result;
        using(var r=cmd.ExecuteReader())
        {
            if(!r.Read()) throw new InvalidOperationException("Delivery not found. Assign the shipment order to a DCR trailer delivery on the web first.");
            result=new(GetInt(r,"DeliveryID"),GetString(r,"DeliveryNo")!,GetString(r,"CustomerID")!,r.GetDateTime(r.GetOrdinal("ShipDate")),GetString(r,"TrailerNo"),GetString(r,"Status")!,[],[],[]);
        }
        using var items=new SqlCommand("""
            SELECT OrderNo FROM dbo.FG_ShippingOrder WHERE DeliveryID=@Id ORDER BY OrderID;
            SELECT L.PartNo,I.ItemName,SUM(L.Qty) RequiredQty,
                (SELECT COALESCE(SUM(A.Qty),0) FROM dbo.FG_DeliveryLoad A WHERE A.DeliveryID=@Id AND A.PartNo=L.PartNo) LoadedQty
            FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.PartNo WHERE O.DeliveryID=@Id GROUP BY L.PartNo,I.ItemName;
            SELECT RootLotNo,LotNo,PartNo,Qty FROM dbo.FG_DeliveryLoad WHERE DeliveryID=@Id ORDER BY LoadID;
            """,conn,tx);items.Parameters.Add("@Id",SqlDbType.Int).Value=result.DeliveryId;
        using var reader=items.ExecuteReader();
        while(reader.Read())result.OrderNos.Add(reader.GetString(0));
        reader.NextResult();while(reader.Read())result.Parts.Add(new(reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.GetDecimal(2),reader.GetDecimal(3)));
        reader.NextResult();while(reader.Read())result.Loads.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3)));
        return result;
    }

    private sealed record LoadUnit(string Type,string? Customer,List<DeliveryLoadDto> Items);
    private static LoadUnit ReadLoadUnit(SqlConnection conn,SqlTransaction tx,string barcode)
    {
        if(string.IsNullOrWhiteSpace(barcode) || barcode.Length>50)throw new ArgumentException("Scan a pallet LOT barcode within 50 characters.");
        using(var pallet=new SqlCommand("DECLARE @P varchar(50); SELECT @P=PalletNo FROM dbo.MD_Pallet WITH(UPDLOCK,HOLDLOCK) WHERE PalletNo=@Barcode;",conn,tx))
        {
            Add(pallet,"@Barcode",SqlDbType.VarChar,50,barcode);
            pallet.ExecuteNonQuery();
        }
        using var cmd=new SqlCommand(PdaFinishedGoodsSql.OutboundScan,conn,tx);Add(cmd,"@Barcode",SqlDbType.NVarChar,50,barcode);
        using var r=cmd.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Pallet not found.");
        var type=GetString(r,"UnitType")!;var customer=GetString(r,"KnownCustomerID");var rows=new List<DeliveryLoadDto>();
        r.NextResult();while(r.Read())rows.Add(new(barcode,GetString(r,"LotNo")!,GetString(r,"PartNo")!,GetDecimal(r,"Qty")));
        return new(type,customer,rows);
    }

    public ShippingDeliveryDto LoadShippingDelivery(string deliveryBarcode,string palletBarcode,string actor,bool allowNonFifo=false)
    {
        CheckActor(actor);if(string.IsNullOrWhiteSpace(palletBarcode))throw new ArgumentException("Scan a pallet barcode.");palletBarcode=palletBarcode.Trim();
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        var delivery=ReadDelivery(conn,tx,deliveryBarcode);
        if(delivery.Status=="Shipped")throw new InvalidOperationException("This delivery has already shipped.");
        if(delivery.Status=="Cancelled")throw new InvalidOperationException("This DCR was released. Assign the shipment orders to a DCR on the web first.");
        var unit=ReadLoadUnit(conn,tx,palletBarcode);
        if(unit.Type is not ("PALLET" or "CASE"))throw new InvalidOperationException("Scan the parent pallet barcode, not an individual part or box.");
        if(unit.Customer is not null && unit.Customer!=delivery.CustomerId)throw new InvalidOperationException("The pallet customer does not match this delivery.");
        var error=ValidateDeliveryLoad(delivery.Parts,unit.Items);
        if(error is not null)throw new InvalidOperationException(error);
        if(!allowNonFifo && FinishedGoodsFifo.Check(conn,tx,palletBarcode,delivery.CustomerId) is {Count:>0} warnings)
            throw new FifoConfirmationRequiredException(warnings);
        foreach(var item in unit.Items)
        {
            using var cmd=new SqlCommand("""
                IF EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Lot AND ShipmentID IS NULL)
                    THROW 52020,'This pallet or one of its LOTs is already loaded on a delivery.',1;
                INSERT dbo.FG_DeliveryLoad(DeliveryID,RootLotNo,LotNo,PartNo,Qty,LoadedBy) VALUES(@Id,@Root,@Lot,@Part,@Qty,@Actor);
                """,conn,tx);
            cmd.Parameters.Add("@Id",SqlDbType.Int).Value=delivery.DeliveryId;Add(cmd,"@Root",SqlDbType.NVarChar,50,palletBarcode);
            Add(cmd,"@Lot",SqlDbType.NVarChar,50,item.LotNo);Add(cmd,"@Part",SqlDbType.VarChar,20,item.PartNo);Qty(cmd,"@Qty",item.Qty);Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);cmd.ExecuteNonQuery();
        }
        using(var status=new SqlCommand("UPDATE dbo.FG_Delivery SET Status='Loading' WHERE DeliveryID=@Id; UPDATE dbo.FG_ShippingOrder SET Status='Loading',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE DeliveryID=@Id;",conn,tx))
        {status.Parameters.Add("@Id",SqlDbType.Int).Value=delivery.DeliveryId;Add(status,"@Actor",SqlDbType.VarChar,20,actor);status.ExecuteNonQuery();}
        var result=ReadDelivery(conn,tx,delivery.DeliveryNo);tx.Commit();return result;
    }

    public static string? ValidateDeliveryLoad(IReadOnlyList<DeliveryRequirementDto> requirements,IReadOnlyList<DeliveryLoadDto> items)
    {
        if(items.Count==0)return "The pallet contains no available inventory.";
        foreach(var group in items.GroupBy(x=>x.PartNo,StringComparer.OrdinalIgnoreCase))
        {
            var part=requirements.FirstOrDefault(x=>string.Equals(x.PartNo,group.Key,StringComparison.OrdinalIgnoreCase));
            if(part is null)return $"{group.Key}: this part is not required by the shipment orders.";
            if(group.Sum(x=>x.Qty)>part.RequiredQty-part.LoadedQty)return $"{group.Key}: pallet quantity exceeds the remaining requirement ({part.RequiredQty-part.LoadedQty:N0}).";
        }
        return null;
    }

    public ShippingDeliveryDto UnloadShippingDelivery(string barcode,string palletBarcode,string actor)
    {
        CheckActor(actor);
        if(string.IsNullOrWhiteSpace(palletBarcode)||palletBarcode.Length>50)throw new ArgumentException("Select a loaded pallet.");
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        var delivery=ReadDelivery(conn,tx,barcode);
        if(delivery.Status=="Shipped")throw new InvalidOperationException("A shipped delivery cannot be changed.");
        using(var cmd=new SqlCommand("DELETE dbo.FG_DeliveryLoad WHERE DeliveryID=@Id AND RootLotNo=@Root AND ShipmentID IS NULL",conn,tx))
        {cmd.Parameters.Add("@Id",SqlDbType.Int).Value=delivery.DeliveryId;Add(cmd,"@Root",SqlDbType.NVarChar,50,palletBarcode);cmd.ExecuteNonQuery();}
        using(var status=new SqlCommand("IF NOT EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad WHERE DeliveryID=@Id) BEGIN UPDATE dbo.FG_Delivery SET Status='Open' WHERE DeliveryID=@Id; UPDATE dbo.FG_ShippingOrder SET Status='Open',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE DeliveryID=@Id; END;",conn,tx))
        {status.Parameters.Add("@Id",SqlDbType.Int).Value=delivery.DeliveryId;Add(status,"@Actor",SqlDbType.VarChar,20,actor);status.ExecuteNonQuery();}
        var result=ReadDelivery(conn,tx,delivery.DeliveryNo);tx.Commit();return result;
    }

    public ShippingDeliveryDto CompleteShippingDelivery(string barcode,string actor,bool allowNonFifo=false)
    {
        CheckActor(actor);
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        var delivery=ReadDelivery(conn,tx,barcode);
        if(delivery.Status=="Shipped") {tx.Commit();return delivery;}
        if(delivery.Parts.Count==0 || delivery.Parts.Any(x=>x.RequiredQty!=x.LoadedQty))throw new InvalidOperationException("Load the exact required quantity for every part before confirming shipment.");
        if(!allowNonFifo)
        {
            var warnings=FinishedGoodsFifo.Check(conn,tx,"",delivery.CustomerId,delivery.DeliveryId);
            if(warnings.Count>0)throw new FifoConfirmationRequiredException(warnings);
        }
        foreach(var root in delivery.Loads.GroupBy(x=>x.RootLotNo))
        {
            var current=ReadLoadUnit(conn,tx,root.Key);
            if(!current.Items.OrderBy(x=>x.LotNo).SequenceEqual(root.OrderBy(x=>x.LotNo)))throw new InvalidOperationException($"{root.Key}: contents or quantity changed after loading. Review the pallet before shipment.");
            using var cmd=new SqlCommand(PdaFinishedGoodsSql.OutboundComplete,conn,tx);
            Add(cmd,"@Barcode",SqlDbType.NVarChar,50,root.Key);Add(cmd,"@CustomerID",SqlDbType.VarChar,20,delivery.CustomerId);Add(cmd,"@OperatorID",SqlDbType.VarChar,20,actor);
            cmd.Parameters.Add("@ShippingDeliveryID",SqlDbType.Int).Value=delivery.DeliveryId;
            int shipment;
            using(var r=cmd.ExecuteReader()) {if(!r.Read())throw new InvalidOperationException("Outbound returned no shipment.");shipment=GetInt(r,"ShipmentID");while(r.NextResult()){} }
            using var update=new SqlCommand("UPDATE dbo.FG_DeliveryLoad SET ShipmentID=@Shipment WHERE DeliveryID=@Delivery AND RootLotNo=@Root",conn,tx);
            update.Parameters.Add("@Shipment",SqlDbType.Int).Value=shipment;update.Parameters.Add("@Delivery",SqlDbType.Int).Value=delivery.DeliveryId;Add(update,"@Root",SqlDbType.NVarChar,50,root.Key);update.ExecuteNonQuery();
        }
        using(var cmd=new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
                LEFT JOIN dbo.PP_CustomerOrder S WITH(UPDLOCK,HOLDLOCK) ON S.SoID=L.SoID WHERE O.DeliveryID=@Id
                AND (S.SoID IS NULL OR UPPER(COALESCE(S.Status,'')) IN('CANCELLED','CANCELED','CLOSED') OR COALESCE(S.ItemNo,'')<>L.PartNo OR COALESCE(S.CustomerID,'')<>@Customer))
                THROW 52020,'An SO was cancelled, closed or changed. Shipment was not saved.',1;
            IF EXISTS(SELECT 1 FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID
                JOIN dbo.PP_CustomerOrder S WITH(UPDLOCK,HOLDLOCK) ON S.SoID=L.SoID WHERE O.DeliveryID=@Id
                GROUP BY S.SoID,S.OrderQty,S.ShippedQty HAVING SUM(L.Qty)>COALESCE(S.OrderQty,0)-COALESCE(S.ShippedQty,0))
                THROW 52020,'The SO balance changed. Shipment was not saved.',1;
            UPDATE S SET ShippedQty=COALESCE(S.ShippedQty,0)+Q.Qty,
                Status=CASE WHEN COALESCE(S.ShippedQty,0)+Q.Qty>=S.OrderQty THEN 'Shipped' ELSE S.Status END,
                ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            FROM dbo.PP_CustomerOrder S JOIN(SELECT L.SoID,SUM(L.Qty) Qty FROM dbo.FG_ShippingOrderLine L
                JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID WHERE O.DeliveryID=@Id GROUP BY L.SoID) Q ON Q.SoID=S.SoID;
            UPDATE dbo.FG_ShippingOrder SET Status='Shipped',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE DeliveryID=@Id;
            UPDATE dbo.FG_Delivery SET Status='Shipped',ShippedBy=@Actor,ShippedAt=SYSDATETIME() WHERE DeliveryID=@Id;
            """,conn,tx)) {cmd.Parameters.Add("@Id",SqlDbType.Int).Value=delivery.DeliveryId;Add(cmd,"@Customer",SqlDbType.VarChar,20,delivery.CustomerId);Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);cmd.ExecuteNonQuery();}
        var result=ReadDelivery(conn,tx,delivery.DeliveryNo);tx.Commit();return result;
    }

    private static void CheckActor(string actor) {if(string.IsNullOrWhiteSpace(actor)||actor.Length>20)throw new ArgumentException("A signed-in operator ID within 20 characters is required.");}
    internal static void Gate(SqlConnection conn,SqlTransaction tx)
    {
        using var cmd=new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF; DECLARE @R int; EXEC @R=sys.sp_getapplock @Resource='FG_SHIPPING_ORDERS',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @R<0 THROW 52020,'Shipping is busy. Retry shortly.',1;",conn,tx);cmd.ExecuteNonQuery();
    }
    private static string NextNo(SqlConnection conn,SqlTransaction tx,string kind)
    {
        var sql=kind=="ORD"?"SELECT OrderNo FROM dbo.FG_ShippingOrder":"SELECT DeliveryNo FROM dbo.FG_Delivery";
        using var cmd=new SqlCommand($"SELECT CONCAT(@Kind,'-',CONVERT(char(6),SYSDATETIME(),12),'-',FORMAT(COALESCE(MAX(TRY_CONVERT(int,SUBSTRING(N.No,12,19))),0)+1,'000000')) FROM({sql}) N(No) WHERE N.No LIKE CONCAT(@Kind,'-',CONVERT(char(6),SYSDATETIME(),12),'-%');",conn,tx);
        Add(cmd,"@Kind",SqlDbType.VarChar,3,kind);return (string)cmd.ExecuteScalar()!;
    }
    private static void Add(SqlCommand cmd,string name,SqlDbType type,int size,object? value)=>cmd.Parameters.Add(name,type,size).Value=value??DBNull.Value;
    private static void Qty(SqlCommand cmd,string name,decimal value) {var p=cmd.Parameters.Add(name,SqlDbType.Decimal);p.Precision=18;p.Scale=3;p.Value=value;}
}
