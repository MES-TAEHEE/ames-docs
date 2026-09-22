using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Data.Connection;
using AMES.Data.Services.PoSync;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Services;

public sealed class ShipmentDispatchService(
    AmesConnectionFactory factory,
    IHttpClientFactory clients)
{
    public sealed record DispatchResult(string RequestId, string DeliveryNote, bool Duplicate);
    internal sealed record ApiConfig(
        string BaseUrl, string ApiKey, string CompanyCode, string BusinessCode,
        string VendorCode, string PurchaseOrganization, string PurchaseOrderType,
        int ArrivalLeadDays, string ArrivalTime);

    public DispatchResult Send(int loadingId, string employeeNo)
    {
        using var connection = factory.OpenConnection();
        var config = LoadConfig(connection);
        using var command = new SqlCommand("""
            SELECT O.ShipOrderNumber,O.CustomerPO,O.ShipDate,LC.LicensePlate,
                   L.LineSeq,D.Qty,LOT.LotCode,LOT.ProducedAt,
                   COALESCE(NULLIF(PACK.QtyPerInner,0),1) AS UnitPackQty
            FROM dbo.FG_LoadingConfirm LC
            JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=LC.ShipmentOrderID
            JOIN dbo.FG_PickingDetail D ON D.PickID=LC.PickID
            JOIN dbo.FG_ShipmentOrderLine L ON L.ShipmentOrderLineID=D.ShipmentOrderLineID
            LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID=D.LotID
            OUTER APPLY
            (
                SELECT TOP(1) P.QtyPerInner
                FROM dbo.MD_PackagingSpec P
                WHERE P.ItemID=D.ItemNo AND COALESCE(P.ActiveFlag,1)=1
                ORDER BY P.PackSpecID
            ) PACK
            WHERE LC.LoadingID=@LoadingID
            ORDER BY L.LineSeq,D.PickSeq,D.PickDetailID;
            """, connection);
        command.Parameters.Add("@LoadingID", SqlDbType.Int).Value = loadingId;

        string? shipOrderNumber = null;
        string? customerPo = null;
        string? truckNo = null;
        DateTime? deliveryDate = null;
        var items = new List<Dictionary<string, object?>>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                shipOrderNumber ??= Text(reader, "ShipOrderNumber");
                customerPo ??= Text(reader, "CustomerPO");
                truckNo ??= Text(reader, "LicensePlate");
                deliveryDate ??= Date(reader, "ShipDate");
                var producedAt = Date(reader, "ProducedAt")
                    ?? throw new InvalidOperationException("A loaded LOT has no production date.");
                items.Add(new Dictionary<string, object?>
                {
                    ["PONO"] = customerPo ?? shipOrderNumber,
                    ["PONO_SEQ"] = Int(reader, "LineSeq").ToString("D5"),
                    ["UNIT_PACK_QTY"] = Decimal(reader, "UnitPackQty"),
                    ["DELI_QTY"] = Decimal(reader, "Qty"),
                    ["VEND_LOTNO"] = Text(reader, "LotCode"),
                    ["PRDT_DATE"] = producedAt.ToString("yyyy-MM-dd"),
                    ["CHANGE_4M"] = ""
                });
            }
        }

        if (items.Count == 0 || string.IsNullOrWhiteSpace(shipOrderNumber))
            throw new InvalidOperationException("No loaded shipment items were found.");
        var shipDate = deliveryDate ?? DateTime.Today;
        var requestId = $"AMES-SHIP-{DateTime.Today:yyyyMMdd}-{loadingId:D4}";
        var payload = new Dictionary<string, object?>
        {
            ["REQUEST_ID"] = requestId,
            ["CORCD"] = config.CompanyCode,
            ["BIZCD"] = config.BusinessCode,
            ["VENDCD"] = config.VendorCode,
            ["PURC_ORG"] = config.PurchaseOrganization,
            ["PURC_PO_TYPE"] = config.PurchaseOrderType,
            ["DELI_DATE"] = shipDate.ToString("yyyy-MM-dd"),
            ["ARRIV_DATE"] = shipDate.AddDays(config.ArrivalLeadDays).ToString("yyyy-MM-dd"),
            ["ARRIV_TIME"] = config.ArrivalTime,
            ["TRUCK_NO"] = truckNo,
            ["USER_ID"] = employeeNo,
            ["ITEMS"] = items
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl}/api/shipments");
        request.Headers.Add("X-API-KEY", config.ApiKey);
        request.Content = JsonContent.Create(payload);
        using var response = clients.CreateClient().Send(request);
        var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Shipment API HTTP {(int)response.StatusCode}: {Short(responseBody)}");

        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;
        if (!root.TryGetProperty("success", out var success) || !success.GetBoolean())
            throw new InvalidOperationException("Shipment API did not confirm success.");
        var data = root.GetProperty("data");
        var deliveryNote = data.GetProperty("DELI_NOTE").GetString() ?? requestId;
        var duplicate = root.TryGetProperty("duplicate", out var duplicateValue) && duplicateValue.GetBoolean();
        SaveSuccess(connection, loadingId, requestId, employeeNo, deliveryNote, JsonSerializer.Serialize(items));
        return new DispatchResult(requestId, deliveryNote, duplicate);
    }

    private static ApiConfig LoadConfig(SqlConnection connection)
    {
        using var command = new SqlCommand("""
            SELECT S.Description AS Parameters, U.Description AS BaseUrl,
                   A.Attribute1 AS AuthScheme, A.Description AS ApiKey
            FROM dbo.MD_CodeItem S
            JOIN dbo.MD_CodeItem U ON U.GroupCode='FG_SHIPMENT_URL' AND U.CodeValue=S.CodeValue AND ISNULL(U.UseFlag,1)=1
            JOIN dbo.MD_CodeItem A ON A.GroupCode='FG_SHIPMENT_AUTH' AND A.CodeValue=S.CodeValue AND ISNULL(A.UseFlag,1)=1
            WHERE S.GroupCode='FG_SHIPMENT_SOURCE' AND S.CodeValue='SEMS' AND ISNULL(S.UseFlag,1)=1;
            """, connection);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Savannah shipment API configuration was not found in common codes.");
        return ParseConfig(
            Convert.ToString(reader["BaseUrl"]), Convert.ToString(reader["AuthScheme"]),
            Convert.ToString(reader["ApiKey"]), Convert.ToString(reader["Parameters"]));
    }

    internal static ApiConfig ParseConfig(string? baseUrl, string? authScheme, string? apiKey, string? parameters)
    {
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/'), UriKind.Absolute, out var uri))
            throw new InvalidOperationException("FG_SHIPMENT_URL SEMS Description must contain an absolute URL.");
        if (!string.Equals(authScheme?.Trim(), "Header:X-API-KEY", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("FG_SHIPMENT_AUTH SEMS must contain Header:X-API-KEY and its key value.");

        var p = PoSyncConfig.ParseParams(parameters);
        string Required(string key) => p.TryGetValue(key, out var value) && value.Length > 0
            ? value : throw new InvalidOperationException($"FG_SHIPMENT_SOURCE SEMS is missing {key}.");
        var leadDays = int.TryParse(p.GetValueOrDefault("ARRIVAL_LEAD_DAYS"), out var days) && days >= 0 ? days : 1;
        return new ApiConfig(uri.ToString().TrimEnd('/'), apiKey.Trim(), Required("CORCD"), Required("BIZCD"),
            Required("VENDCD"), Required("PURC_ORG"), Required("PURC_PO_TYPE"), leadDays,
            p.GetValueOrDefault("ARRIVAL_TIME") is { Length: > 0 } time ? time : "0930");
    }

    private static void SaveSuccess(SqlConnection connection, int loadingId, string requestId,
        string employeeNo, string deliveryNote, string linesJson)
    {
        using var command = new SqlCommand("""
            DECLARE @ID int=(SELECT TOP(1) DeliveryNoteID FROM dbo.FG_DeliveryNote WHERE LoadingID=@LoadingID ORDER BY DeliveryNoteID DESC);
            IF @ID IS NULL
                INSERT dbo.FG_DeliveryNote
                    (DnNumber,ShipmentOrderID,LoadingID,CustomerCode,FormatTemplate,Revision,
                     IssuedAt,IssuedBy,EdiMsgID,EdiStatus,CustomerAckTS,LinesJSON,RevisionReason,CreatedBy,CreatedTS)
                SELECT @DeliveryNote,LC.ShipmentOrderID,LC.LoadingID,O.CustomerCode,'SRM_SHIPMENT',1,
                       SYSDATETIME(),@UserID,@RequestID,'Sent',SYSUTCDATETIME(),@LinesJSON,
                       NULL,'api',SYSDATETIME()
                FROM dbo.FG_LoadingConfirm LC JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=LC.ShipmentOrderID
                WHERE LC.LoadingID=@LoadingID;
            ELSE
                UPDATE dbo.FG_DeliveryNote
                   SET DnNumber=@DeliveryNote,EdiMsgID=@RequestID,EdiStatus='Sent',
                       CustomerAckTS=SYSUTCDATETIME(),LinesJSON=@LinesJSON,RevisionReason=NULL
                 WHERE DeliveryNoteID=@ID;
            """, connection);
        command.Parameters.Add("@LoadingID", SqlDbType.Int).Value = loadingId;
        command.Parameters.Add("@RequestID", SqlDbType.VarChar, 40).Value = requestId;
        command.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = employeeNo;
        command.Parameters.Add("@DeliveryNote", SqlDbType.VarChar, 60).Value = deliveryNote;
        command.Parameters.Add("@LinesJSON", SqlDbType.NVarChar, -1).Value = linesJson;
        command.ExecuteNonQuery();
    }

    private static string? Text(SqlDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToString(reader[name])?.Trim();
    private static int Int(SqlDataReader reader, string name) => reader[name] is DBNull ? 0 : Convert.ToInt32(reader[name]);
    private static decimal Decimal(SqlDataReader reader, string name) => reader[name] is DBNull ? 0 : Convert.ToDecimal(reader[name]);
    private static DateTime? Date(SqlDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToDateTime(reader[name]);
    private static string Short(string value) => value.Length <= 200 ? value : value[..200];
}
