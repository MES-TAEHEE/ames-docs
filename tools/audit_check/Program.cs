using System.Text;
using Microsoft.Data.SqlClient;

Console.OutputEncoding = Encoding.UTF8;

const string cs =
    "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
    "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

using var conn = new SqlConnection(cs);
conn.Open();

void Dump(string table, int max = 20)
{
    Console.WriteLine();
    Console.WriteLine($"=== {table} ===");
    using var cmd = new SqlCommand("""
        SELECT TOP (@N) c.name AS C, CAST(ep.value AS NVARCHAR(MAX)) AS V
        FROM sys.columns c
        JOIN sys.extended_properties ep ON ep.class=1 AND ep.major_id=c.object_id
            AND ep.minor_id=c.column_id AND ep.name='MS_Description'
        WHERE c.object_id = OBJECT_ID(@T)
        ORDER BY c.column_id;
        """, conn);
    cmd.Parameters.AddWithValue("@T", $"dbo.{table}");
    cmd.Parameters.AddWithValue("@N", max);
    using var rdr = cmd.ExecuteReader();
    while (rdr.Read()) Console.WriteLine($"  {rdr["C"],-22} {rdr["V"]}");
}

Dump("MD_Item");
Dump("MD_Equipment");
Dump("PR_AndonCall");
Dump("SYS_AuditLog");
Dump("FG_ShipmentOrder");
Dump("MNT_FailureRegister");

Console.WriteLine();
Console.WriteLine("[done]");
