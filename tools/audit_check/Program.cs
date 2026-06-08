using System.Text;
using Microsoft.Data.SqlClient;

Console.OutputEncoding = Encoding.UTF8;

const string cs =
    "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;" +
    "TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;";

using var conn = new SqlConnection(cs);
conn.Open();

// 1) Table-level
Console.WriteLine("=== Table descriptions (sample 5) ===");
using (var cmd = new SqlCommand("""
    SELECT TOP 5 t.name AS T, CAST(ep.value AS NVARCHAR(MAX)) AS V
    FROM sys.tables t
    JOIN sys.extended_properties ep ON ep.major_id=t.object_id AND ep.minor_id=0 AND ep.name='MS_Description'
    WHERE t.is_ms_shipped=0
    ORDER BY t.name;
    """, conn))
using (var rdr = cmd.ExecuteReader())
    while (rdr.Read()) Console.WriteLine($"  {rdr["T"],-25} {rdr["V"]}");

// 2) Column-level samples
Console.WriteLine();
Console.WriteLine("=== MD_Item columns ===");
using (var cmd = new SqlCommand("""
    SELECT c.name AS C, CAST(ep.value AS NVARCHAR(MAX)) AS V
    FROM sys.columns c
    JOIN sys.extended_properties ep ON ep.class=1 AND ep.major_id=c.object_id
        AND ep.minor_id=c.column_id AND ep.name='MS_Description'
    WHERE c.object_id = OBJECT_ID('dbo.MD_Item')
    ORDER BY c.column_id;
    """, conn))
using (var rdr = cmd.ExecuteReader())
    while (rdr.Read()) Console.WriteLine($"  {rdr["C"],-22} {rdr["V"]}");

Console.WriteLine();
Console.WriteLine("=== MD_Equipment audit columns ===");
using (var cmd = new SqlCommand("""
    SELECT c.name AS C, CAST(ep.value AS NVARCHAR(MAX)) AS V
    FROM sys.columns c
    JOIN sys.extended_properties ep ON ep.class=1 AND ep.major_id=c.object_id
        AND ep.minor_id=c.column_id AND ep.name='MS_Description'
    WHERE c.object_id = OBJECT_ID('dbo.MD_Equipment')
      AND c.name IN ('CreatedBy','CreatedTS','ModifiedBy','ModifiedTS','ActiveFlag')
    ORDER BY c.column_id;
    """, conn))
using (var rdr = cmd.ExecuteReader())
    while (rdr.Read()) Console.WriteLine($"  {rdr["C"],-22} {rdr["V"]}");

Console.WriteLine();
Console.WriteLine("[done]");
