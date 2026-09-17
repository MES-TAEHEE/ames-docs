using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Services;

public sealed class ShipmentDispatchService(
    AmesConnectionFactory factory,
    IHttpClientFactory clients,
    IConfiguration configuration)
{
    public sealed record DispatchResult(string RequestId, string DeliveryNote, bool Duplicate);

    public DispatchResult Send(int loadingId, string employeeNo)
    {
        var section = configuration.GetSection("ExternalApis:Shipment");
        var baseUrl = Required(section, "BaseUrl").TrimEnd('/');
        var apiKey = Required(section, "ApiKey");
        var arrivalLeadDays = section.GetValue<int?>("ArrivalLeadDays") ?? 1;
        var arrivalTime = section["ArrivalTime"] ?? "0930";

        using var connection = factory.OpenConnection();
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
            ["CORCD"] = Required(section, "CompanyCode"),
            ["BIZCD"] = Required(section, "BusinessCode"),
            ["VENDCD"] = Required(section, "VendorCode"),
            ["PURC_ORG"] = Required(section, "PurchaseOrganization"),
            ["PURC_PO_TYPE"] = Required(section, "PurchaseOrderType"),
            ["DELI_DATE"] = shipDate.ToString("yyyy-MM-dd"),
            ["ARRIV_DATE"] = shipDate.AddDays(arrivalLeadDays).ToString("yyyy-MM-dd"),
            ["ARRIV_TIME"] = arrivalTime,
            ["TRUCK_NO"] = truckNo,
            ["USER_ID"] = employeeNo,
            ["ITEMS"] = items
        };

        SaveStatus(connection, loadingId, requestId, employeeNo, "Pending", null, JsonSerializer.Serialize(items));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/shipments");
            request.Headers.Add("X-API-KEY", apiKey);
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
            SaveStatus(connection, loadingId, requestId, employeeNo, "Sent", deliveryNote, JsonSerializer.Serialize(items));
            return new DispatchResult(requestId, deliveryNote, duplicate);
        }
        catch (Exception ex)
        {
            SaveStatus(connection, loadingId, requestId, employeeNo, "Failed", Short(ex.Message), JsonSerializer.Serialize(items));
            throw;
        }
    }

    private static void SaveStatus(SqlConnection connection, int loadingId, string requestId,
        string employeeNo, string status, string? result, string linesJson)
    {
        using var command = new SqlCommand("""
            DECLARE @ID int=(SELECT TOP(1) DeliveryNoteID FROM dbo.FG_DeliveryNote WHERE LoadingID=@LoadingID ORDER BY DeliveryNoteID DESC);
            IF @ID IS NULL
                INSERT dbo.FG_DeliveryNote
                    (DnNumber,ShipmentOrderID,LoadingID,CustomerCode,FormatTemplate,Revision,
                     IssuedAt,IssuedBy,EdiMsgID,EdiStatus,CustomerAckTS,LinesJSON,RevisionReason,CreatedBy,CreatedTS)
                SELECT COALESCE(@Result,@RequestID),LC.ShipmentOrderID,LC.LoadingID,O.CustomerCode,'SRM_SHIPMENT',1,
                       SYSDATETIME(),@UserID,@RequestID,@Status,
                       CASE WHEN @Status='Sent' THEN SYSUTCDATETIME() END,@LinesJSON,
                       CASE WHEN @Status='Failed' THEN @Result END,'api',SYSDATETIME()
                FROM dbo.FG_LoadingConfirm LC JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID=LC.ShipmentOrderID
                WHERE LC.LoadingID=@LoadingID;
            ELSE
                UPDATE dbo.FG_DeliveryNote
                   SET DnNumber=COALESCE(@Result,DnNumber),EdiMsgID=@RequestID,EdiStatus=@Status,
                       CustomerAckTS=CASE WHEN @Status='Sent' THEN SYSUTCDATETIME() ELSE CustomerAckTS END,
                       LinesJSON=@LinesJSON,RevisionReason=CASE WHEN @Status='Failed' THEN @Result ELSE NULL END
                 WHERE DeliveryNoteID=@ID;
            """, connection);
        command.Parameters.Add("@LoadingID", SqlDbType.Int).Value = loadingId;
        command.Parameters.Add("@RequestID", SqlDbType.VarChar, 40).Value = requestId;
        command.Parameters.Add("@UserID", SqlDbType.NVarChar, 450).Value = employeeNo;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 15).Value = status;
        command.Parameters.Add("@Result", SqlDbType.NVarChar, 200).Value = (object?)result ?? DBNull.Value;
        command.Parameters.Add("@LinesJSON", SqlDbType.NVarChar, -1).Value = linesJson;
        command.ExecuteNonQuery();
    }

    private static string Required(IConfiguration section, string key) =>
        section[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"ExternalApis:Shipment:{key} is missing.");
    private static string? Text(SqlDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToString(reader[name])?.Trim();
    private static int Int(SqlDataReader reader, string name) => reader[name] is DBNull ? 0 : Convert.ToInt32(reader[name]);
    private static decimal Decimal(SqlDataReader reader, string name) => reader[name] is DBNull ? 0 : Convert.ToDecimal(reader[name]);
    private static DateTime? Date(SqlDataReader reader, string name) => reader[name] is DBNull ? null : Convert.ToDateTime(reader[name]);
    private static string Short(string value) => value.Length <= 200 ? value : value[..200];
}
