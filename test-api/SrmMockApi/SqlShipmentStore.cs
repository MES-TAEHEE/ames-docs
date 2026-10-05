using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

public sealed class SqlShipmentStore(string connectionString) : IShipmentStore
{
    private static void Text(SqlCommand command, string name, string? value, int size = 100) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;

    private static void Quantity(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 28;
        parameter.Scale = 6;
        parameter.Value = value;
    }

    private static async Task<StoredShipment?> Read(SqlConnection connection, SqlTransaction? transaction, string id, CancellationToken ct)
    {
        await using var command = new SqlCommand("SELECT REQUEST_JSON, RECEIPT_JSON FROM dbo.TEST_SRM_Shipment WHERE REQUEST_ID=@id", connection, transaction);
        Text(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var request = JsonSerializer.Deserialize<ShipmentRequest>(reader.GetString(0), ShipmentApi.Json);
        var receipt = JsonSerializer.Deserialize<ShipmentReceipt>(reader.GetString(1), ShipmentApi.Json);
        return request is null || receipt is null ? throw new InvalidDataException("Invalid shipment record.") : new(request, receipt);
    }

    public async Task<StoredShipment?> Get(string requestId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await Read(connection, null, requestId, ct);
    }

    public async Task<(StoredShipment? Stored, bool Duplicate, bool Conflict)> Save(ShipmentRequest request, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        // Transaction-owned lock serializes retries across API instances, including absent rows.
        await using (var command = new SqlCommand("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@key, @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 50001, 'Shipment request lock unavailable.', 1;
            """, connection, transaction))
        {
            Text(command, "@key", "TEST_SRM_Shipment:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.REQUEST_ID))), 255);
            await command.ExecuteNonQueryAsync(ct);
        }
        var json = JsonSerializer.Serialize(request, ShipmentApi.Json);
        var existing = await Read(connection, transaction, request.REQUEST_ID, ct);
        if (existing is not null)
        {
            var same = JsonSerializer.Serialize(existing.Request, ShipmentApi.Json) == json;
            await transaction.CommitAsync(ct);
            return (existing, same, !same);
        }
        var receipt = new ShipmentReceipt(request.REQUEST_ID, "TEST-" + Guid.NewGuid().ToString("N"),
            "COMPLETED", request.PURC_PO_TYPE == "1K10" ? "NOT_REQUIRED" : "SIMULATED_SUCCESS",
            true, DateTimeOffset.UtcNow, request.ITEMS.Length, request.ITEMS.Sum(i => i.DELI_QTY));
        await using (var command = new SqlCommand("""
            INSERT dbo.TEST_SRM_Shipment
                (REQUEST_ID, CORCD, BIZCD, VENDCD, PURC_ORG, PURC_PO_TYPE, DELI_DATE, ARRIV_DATE,
                 ARRIV_TIME, TRUCK_NO, USER_ID, DELI_NOTE, STATUS, SAP_STATUS, SAP_SIMULATED,
                 RECEIVED_AT_UTC, ITEM_COUNT, TOTAL_DELI_QTY, REQUEST_JSON, RECEIPT_JSON)
            VALUES (@id,@corcd,@bizcd,@vendcd,@org,@type,@delivery,@arrival,@time,@truck,@user,
                    @note,@status,@sap,1,@received,@count,@total,@request,@receipt)
            """, connection, transaction))
        {
            Text(command, "@id", request.REQUEST_ID); Text(command, "@corcd", request.CORCD);
            Text(command, "@bizcd", request.BIZCD); Text(command, "@vendcd", request.VENDCD);
            Text(command, "@org", request.PURC_ORG); Text(command, "@type", request.PURC_PO_TYPE);
            command.Parameters.Add("@delivery", SqlDbType.Date).Value = DateOnly.ParseExact(request.DELI_DATE, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue);
            command.Parameters.Add("@arrival", SqlDbType.Date).Value = DateOnly.ParseExact(request.ARRIV_DATE, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue);
            Text(command, "@time", request.ARRIV_TIME, 4); Text(command, "@truck", request.TRUCK_NO);
            Text(command, "@user", request.USER_ID); Text(command, "@note", receipt.DELI_NOTE);
            Text(command, "@status", receipt.STATUS); Text(command, "@sap", receipt.SAP_STATUS);
            command.Parameters.Add("@received", SqlDbType.DateTimeOffset).Value = receipt.RECEIVED_AT_UTC;
            command.Parameters.Add("@count", SqlDbType.Int).Value = receipt.ITEM_COUNT;
            Quantity(command, "@total", receipt.TOTAL_DELI_QTY);
            Text(command, "@request", json, -1);
            Text(command, "@receipt", JsonSerializer.Serialize(receipt, ShipmentApi.Json), -1);
            await command.ExecuteNonQueryAsync(ct);
        }
        for (var index = 0; index < request.ITEMS.Length; index++)
        {
            var item = request.ITEMS[index];
            await using var command = new SqlCommand("""
                INSERT dbo.TEST_SRM_ShipmentItem
                    (REQUEST_ID, ITEM_NO, PONO, PONO_SEQ, UNIT_PACK_QTY, DELI_QTY, VEND_LOTNO, PRDT_DATE, CHANGE_4M)
                VALUES (@id,@line,@pono,@seq,@pack,@qty,@lot,@date,@change)
                """, connection, transaction);
            Text(command, "@id", request.REQUEST_ID);
            command.Parameters.Add("@line", SqlDbType.Int).Value = index + 1;
            Text(command, "@pono", item.PONO); Text(command, "@seq", item.PONO_SEQ);
            Quantity(command, "@pack", item.UNIT_PACK_QTY); Quantity(command, "@qty", item.DELI_QTY);
            Text(command, "@lot", item.VEND_LOTNO); Text(command, "@change", item.CHANGE_4M);
            command.Parameters.Add("@date", SqlDbType.Date).Value = DateOnly.ParseExact(item.PRDT_DATE, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue);
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (new(request, receipt), false, false);
    }

    public void Dispose() { }
}
