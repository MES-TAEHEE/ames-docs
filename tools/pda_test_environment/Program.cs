using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

if (args.Length < 3 || args[0] is not ("plan" or "clone" or "resume" or "sync-unfinished" or "finalize" or "verify"))
    throw new ArgumentException("Usage: plan|clone|resume|sync-unfinished|finalize|verify AMES_TEST_PDA_name output-directory; source connection comes from AMES_CLONE_SOURCE_CONN.");
var (mode, target, output) = (args[0], args[1], Path.GetFullPath(args[2]));
if (!Regex.IsMatch(target, "^AMES_TEST_PDA_[A-Za-z0-9_]+$")) throw new ArgumentException("Only new AMES_TEST_PDA_ databases are permitted.");
var sourceCs = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AMES_CLONE_SOURCE_CONN"));
if (sourceCs.InitialCatalog != "AMES_DEV" || sourceCs.DataSource is not ("192.168.0.132" or "192.168.0.132,1433"))
    throw new ArgumentException("Source must explicitly be 192.168.0.132/AMES_DEV.");
sourceCs.ApplicationName = "AMES read-only clone";
var localCs = new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true };
using var source = new SqlConnection(sourceCs.ConnectionString); source.Open();
using var local = new SqlConnection(localCs.ConnectionString); local.Open();
Directory.CreateDirectory(output);
string Q(string name) => "[" + name.Replace("]", "]]") + "]";
void Exec(SqlConnection conn, string sql) { using var c = new SqlCommand(sql, conn) { CommandTimeout = 120 }; c.ExecuteNonQuery(); }
object? Scalar(SqlConnection conn, string sql) { using var c = new SqlCommand(sql, conn) { CommandTimeout = 60 }; return c.ExecuteScalar(); }
var collation = Convert.ToString(Scalar(source, "SELECT CONVERT(sysname,DATABASEPROPERTYEX(DB_NAME(),'Collation'))"))!;
if (!Regex.IsMatch(collation, "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Unexpected source collation.");
var smo = new Server(new ServerConnection(source));
smo.SetDefaultInitFields(typeof(Table), "IsSystemObject");
smo.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject");
smo.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject");
var database = smo.Databases["AMES_DEV"];
var userObjects = new HashSet<string>(StringComparer.Ordinal);
using (var command = new SqlCommand("SELECT s.name+'.'+o.name FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.is_ms_shipped=0", source))
using (var reader = command.ExecuteReader()) while (reader.Read()) userObjects.Add(reader.GetString(0));
var tables = database.Tables.Cast<Table>().Where(t => userObjects.Contains(t.Schema + "." + t.Name)).OrderBy(t => t.Schema).ThenBy(t => t.Name).ToList();
var procedures = database.StoredProcedures.Cast<StoredProcedure>().Where(p => userObjects.Contains(p.Schema + "." + p.Name)).ToList();
var functions = database.UserDefinedFunctions.Cast<UserDefinedFunction>().Where(f => userObjects.Contains(f.Schema + "." + f.Name)).ToList();
var inventory = new { Source = "192.168.0.132/AMES_DEV", Target = "localhost/" + target, Collation = collation,
    Tables = tables.Count, Procedures = procedures.Count, Functions = functions.Count, Sequences = database.Sequences.Count };
Console.WriteLine(JsonSerializer.Serialize(inventory));
if (mode is "plan" or "clone")
{
    var transfer = new Transfer(database)
    {
        CopyAllObjects = true, CopyAllLogins = false, CopyData = false, CopySchema = true,
        CreateTargetDatabase = false, DropDestinationObjectsFirst = false,
        DestinationServer = "localhost", DestinationDatabase = target, DestinationLoginSecure = true
    };
    transfer.Options.IncludeDatabaseContext = false;
    transfer.Options.AllowSystemObjects = true;
    transfer.Options.IncludeHeaders = false;
    transfer.Options.ScriptDrops = false;
    transfer.Options.DriAll = true;
    transfer.Options.DriIncludeSystemNames = true;
    transfer.Options.Indexes = true;
    transfer.Options.Triggers = true;
    transfer.Options.ExtendedProperties = true;
    transfer.Options.NoCollation = false;
    transfer.Options.NoFileGroup = true;
    transfer.Options.TargetServerVersion = SqlServerVersion.Version160;
    foreach (var table in tables.Where(t => t.IsSystemObject)) transfer.ObjectList.Add(table);
    foreach (var procedure in procedures.Where(p => p.IsSystemObject)) transfer.ObjectList.Add(procedure);
    foreach (var function in functions.Where(f => f.IsSystemObject)) transfer.ObjectList.Add(function);
    var batches = transfer.EnumScriptTransfer().Cast<string>().ToList();
    File.WriteAllText(Path.Combine(output, "schema.sql"), string.Join("\nGO\n", batches), new UTF8Encoding(false));
    Console.WriteLine($"Schema batches: {batches.Count}");
    if (mode == "plan") return;
    // Never reuse, clear, rebuild, drop or overwrite an existing database.
    if (Convert.ToInt32(Scalar(local, $"SELECT COUNT(*) FROM sys.databases WHERE name=N'{target}'")) != 0)
        throw new InvalidOperationException("Target already exists. Choose a new name; existing databases are never overwritten.");
    Exec(local, $"CREATE DATABASE {Q(target)} COLLATE {collation};");
    var compatibility = Convert.ToInt32(Scalar(source, "SELECT compatibility_level FROM sys.databases WHERE name=DB_NAME()"));
    Exec(local, $"ALTER DATABASE {Q(target)} SET COMPATIBILITY_LEVEL={compatibility}; ALTER DATABASE {Q(target)} SET AUTO_CLOSE OFF;");
    local.ChangeDatabase(target);
    Exec(local, "EXEC sys.sp_addextendedproperty @name=N'AMES.CloneSource',@value=N'192.168.0.132/AMES_DEV'; EXEC sys.sp_addextendedproperty @name=N'AMES.CloneReady',@value=0;");
    foreach (var batch in batches)
    {
        foreach (var command in Regex.Split(batch, @"(?im)^\s*GO\s*;?\s*$"))
        {
            if (string.IsNullOrWhiteSpace(command)) continue;
            var sql = command;
            if (Regex.IsMatch(sql, @"^\s*USE\s+", RegexOptions.IgnoreCase))
            {
                if (!Regex.IsMatch(sql, @"^\s*USE\s+\[?AMES_DEV\]?\s*;?\s*$", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Unexpected database switch in schema script.");
                continue;
            }
            Exec(local, sql);
        }
    }
    Console.WriteLine("Schema created in new local database.");
}
else local.ChangeDatabase(target);
if (mode is "clone" or "resume" or "sync-unfinished" or "finalize")
{
    if (Convert.ToString(Scalar(local, "SELECT value FROM sys.extended_properties WHERE class=0 AND name='AMES.CloneSource'")) != "192.168.0.132/AMES_DEV"
        || Convert.ToInt32(Scalar(local, "SELECT value FROM sys.extended_properties WHERE class=0 AND name='AMES.CloneReady'")) != 0)
        throw new InvalidOperationException("Only this tool's unfinished clone can be resumed.");
    Exec(local, "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;");
    foreach (var table in tables) Exec(local, $"ALTER TABLE {Q(table.Schema)}.{Q(table.Name)} NOCHECK CONSTRAINT ALL; DISABLE TRIGGER ALL ON {Q(table.Schema)}.{Q(table.Name)};");
    foreach (var table in mode == "finalize" ? new List<Table>() : tables)
    {
        var name = Q(table.Schema) + "." + Q(table.Name);
        var columns = table.Columns.Cast<Column>().Where(c => !c.Computed && c.DataType.SqlDataType != SqlDataType.Timestamp).Select(c => c.Name).ToList();
        if (columns.Count == 0) throw new InvalidOperationException("Table has no copyable columns: " + name);
        var refresh = false;
        if (Convert.ToInt64(Scalar(local, $"SELECT COUNT_BIG(*) FROM {name}")) > 0)
        {
            if (Fingerprint(source, name, columns) == Fingerprint(local, name, columns))
            { Console.WriteLine("Verified existing " + name); continue; }
            if (mode != "sync-unfinished")
                throw new InvalidOperationException("Existing clone rows differ: " + name + ". Nothing was overwritten.");
            refresh = true;
        }
        // User explicitly chose no source write freeze; consistency is verified afterwards.
        using var read = new SqlCommand($"SELECT {string.Join(",", columns.Select(Q))} FROM {name} WITH(NOLOCK)", source) { CommandTimeout = 60 };
        using var rows = read.ExecuteReader(CommandBehavior.SequentialAccess);
        using var transaction = local.BeginTransaction();
        if (refresh)
        {
            using var clearClone = new SqlCommand($"DELETE FROM {name}", local, transaction) { CommandTimeout = 120 };
            clearClone.ExecuteNonQuery(); // Only this tool's unfinished local clone; source is never written.
        }
        using var bulk = new SqlBulkCopy(local, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, transaction)
        { DestinationTableName = name, BulkCopyTimeout = 120, BatchSize = 5000, EnableStreaming = true };
        foreach (var column in columns) bulk.ColumnMappings.Add(column, column);
        bulk.WriteToServer(rows);
        transaction.Commit();
        Console.WriteLine((refresh ? "Refreshed unfinished local clone " : "Copied ") + name);
    }
    foreach (var table in tables)
    {
        var name = Q(table.Schema) + "." + Q(table.Name);
        var lastValue = Scalar(source, $"SELECT CONVERT(nvarchar(40),last_value) FROM sys.identity_columns WHERE object_id=OBJECT_ID(N'{name.Replace("'", "''")}')");
        if (lastValue is not null and not DBNull && decimal.TryParse(Convert.ToString(lastValue, CultureInfo.InvariantCulture), out var last))
            Exec(local, $"DBCC CHECKIDENT (N'{name.Replace("'", "''")}', RESEED, {last.ToString(CultureInfo.InvariantCulture)}) WITH NO_INFOMSGS;");
        foreach (Trigger trigger in table.Triggers)
            if (trigger.IsEnabled) Exec(local, $"ENABLE TRIGGER {Q(trigger.Name)} ON {name};");
    }
    // SMO may replace system-generated constraint names. Match their definitions,
    // restore names locally, and preserve the original enabled/trusted state.
    var sourceConstraints = Constraints(source);
    var localConstraints = Constraints(local);
    foreach (var constraint in sourceConstraints)
    {
        var candidates = localConstraints.Where(c => c.Schema == constraint.Schema && c.Table == constraint.Table && c.Type == constraint.Type).ToList();
        var exact = candidates.Where(c => c.Name == constraint.Name).ToList();
        var equivalent = candidates.Where(c => c.Column == constraint.Column && c.Definition == constraint.Definition).ToList();
        var match = exact.Count == 1 ? exact[0] : equivalent.Count == 1 ? equivalent[0] : null;
        if (match is null) throw new InvalidOperationException("Cannot match local constraint: " + constraint.Name);
        if (match.Name != constraint.Name)
        {
            using var rename = new SqlCommand("EXEC sys.sp_rename @objname,@newname,N'OBJECT'", local);
            rename.Parameters.AddWithValue("@objname", Q(match.Schema) + "." + Q(match.Name));
            rename.Parameters.AddWithValue("@newname", constraint.Name);
            rename.ExecuteNonQuery();
        }
        if (constraint.Type is "C" or "F")
        {
            var state = constraint.Disabled ? "NOCHECK" : constraint.Untrusted ? "CHECK" : "WITH CHECK CHECK";
            Exec(local, $"ALTER TABLE {Q(constraint.Schema)}.{Q(constraint.Table)} {state} CONSTRAINT {Q(constraint.Name)};");
        }
    }
    foreach (Sequence sequence in database.Sequences)
    {
        var current = Convert.ToDecimal(sequence.CurrentValue, CultureInfo.InvariantCulture);
        var increment = Convert.ToDecimal(sequence.IncrementValue, CultureInfo.InvariantCulture);
        Exec(local, $"ALTER SEQUENCE {Q(sequence.Schema)}.{Q(sequence.Name)} RESTART WITH {(current + increment).ToString(CultureInfo.InvariantCulture)};");
    }
}
var schemaChecks = new List<object>();
var mismatches = 0;
foreach (var (kind, sql) in new (string, string)[]
{
    ("columns", """
        SELECT s.name,t.name,c.name,ROW_NUMBER() OVER(PARTITION BY c.object_id ORDER BY c.column_id),ty.name,c.max_length,c.precision,c.scale,c.is_nullable,c.collation_name,
               c.is_identity,CONVERT(nvarchar(40),ic.seed_value),CONVERT(nvarchar(40),ic.increment_value),cc.definition,cc.is_persisted
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
        JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id WHERE t.is_ms_shipped=0
        """),
    ("indexes", """
        SELECT s.name,t.name,i.name,i.type,i.is_unique,i.is_primary_key,i.is_unique_constraint,i.is_disabled,i.filter_definition,
               c.name,ic.key_ordinal,ic.is_descending_key,ic.is_included_column,ic.index_column_id
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id
        LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.is_ms_shipped=0
        """),
    ("modules", """
        SELECT s.name,o.name,o.type,m.definition,m.uses_ansi_nulls,m.uses_quoted_identifier,m.is_schema_bound
        FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id JOIN sys.sql_modules m ON m.object_id=o.object_id
        WHERE o.is_ms_shipped=0
        """),
    ("foreign-key-columns", """
        SELECT s.name,t.name,f.name,c.name,rs.name,rt.name,rc.name,fc.constraint_column_id,f.update_referential_action,f.delete_referential_action
        FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id=f.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.tables rt ON rt.object_id=f.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
        JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
        JOIN sys.columns c ON c.object_id=fc.parent_object_id AND c.column_id=fc.parent_column_id
        JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
        WHERE t.is_ms_shipped=0
        """)
})
{
    var left = SchemaRows(source, sql); var right = SchemaRows(local, sql);
    var equal = left.SequenceEqual(right); if (!equal) mismatches++;
    schemaChecks.Add(new { Kind = kind, SourceCount = left.Count, TargetCount = right.Count, Match = equal,
        SourceOnly = left.Except(right).ToArray(), TargetOnly = right.Except(left).ToArray() });
    Console.WriteLine($"Schema {kind}: {left.Count}/{right.Count}, match={equal}");
}
var constraintMatch = Constraints(source).OrderBy(c => c.Schema).ThenBy(c => c.Name).SequenceEqual(Constraints(local).OrderBy(c => c.Schema).ThenBy(c => c.Name));
if (!constraintMatch) mismatches++;
schemaChecks.Add(new { Kind = "constraints-and-trust", Match = constraintMatch });
var checks = new List<object>();
foreach (var table in tables)
{
    var name = Q(table.Schema) + "." + Q(table.Name);
    var columns = table.Columns.Cast<Column>().Where(c => !c.Computed && c.DataType.SqlDataType != SqlDataType.Timestamp).Select(c => c.Name).ToList();
    var left = Fingerprint(source, name, columns);
    var right = Fingerprint(local, name, columns);
    var equal = left == right; if (!equal) mismatches++;
    Console.WriteLine($"Data {name}: {left.Rows}/{right.Rows}, match={equal}");
    checks.Add(new { Table = name, SourceRows = left.Rows, TargetRows = right.Rows, Match = equal, SourceHash = left.Hash, TargetHash = right.Hash });
}
var result = new { inventory, VerificationAtUtc = DateTime.UtcNow, MismatchedTables = mismatches,
    Note = "No source write freeze. Per-table SHA256 fingerprints; computed columns and SQL rowversion are regenerated, not copied.", Schema = schemaChecks, Tables = checks };
File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified {tables.Count} tables; mismatches={mismatches}. SQL rowversion values were regenerated by SQL Server.");
if (mismatches != 0) Environment.ExitCode = 2;
else if (mode is "clone" or "resume" or "sync-unfinished" or "finalize") Exec(local, "EXEC sys.sp_updateextendedproperty @name=N'AMES.CloneReady',@value=1;");

static (long Rows, string Hash) Fingerprint(SqlConnection conn, string table, List<string> columns)
{
    static string Quote(string value) => "[" + value.Replace("]", "]]") + "]";
    using var cmd = new SqlCommand($"SELECT {string.Join(",", columns.Select(Quote))} FROM {table} WITH(NOLOCK)", conn) { CommandTimeout = 60 };
    using var reader = cmd.ExecuteReader();
    var hashes = new List<string>();
    while (reader.Read())
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var value = reader.GetValue(i);
            if (value is DBNull) { writer.Write(-1); continue; }
            var bytes = value switch
            {
                byte[] binary => binary,
                DateTime date => BitConverter.GetBytes(date.Ticks),
                DateTimeOffset date => Encoding.UTF8.GetBytes(date.ToString("O", CultureInfo.InvariantCulture)),
                double number => Encoding.UTF8.GetBytes(number.ToString("R", CultureInfo.InvariantCulture)),
                float number => Encoding.UTF8.GetBytes(number.ToString("R", CultureInfo.InvariantCulture)),
                _ => Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture)!)
            };
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        writer.Flush(); hashes.Add(Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, checked((int)data.Length)))));
    }
    hashes.Sort(StringComparer.Ordinal);
    using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var hash in hashes) all.AppendData(Convert.FromHexString(hash));
    return (hashes.Count, Convert.ToHexString(all.GetHashAndReset()));
}

static List<string> SchemaRows(SqlConnection connection, string sql)
{
    using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
    using var reader = command.ExecuteReader();
    var rows = new List<string>();
    while (reader.Read())
    {
        var values = new object[reader.FieldCount]; reader.GetValues(values);
        rows.Add(JsonSerializer.Serialize(values.Select(v => v is DBNull ? null : v is string text ? Regex.Replace(text, @"\s+", " ").Trim() : v)));
    }
    rows.Sort(StringComparer.Ordinal); return rows;
}

static List<ConstraintState> Constraints(SqlConnection connection)
{
    using var command = new SqlCommand("""
        SELECT s.name,t.name,o.name,RTRIM(o.type),COALESCE(col.name,N''),
               COALESCE(c.definition,d.definition,
                   CASE WHEN f.object_id IS NOT NULL THEN CONCAT(OBJECT_SCHEMA_NAME(f.referenced_object_id),N'.',OBJECT_NAME(f.referenced_object_id),N'|',
                       (SELECT STRING_AGG(CONCAT(COL_NAME(fc.parent_object_id,fc.parent_column_id),N'=',COL_NAME(fc.referenced_object_id,fc.referenced_column_id)),N',')
                        WITHIN GROUP(ORDER BY fc.constraint_column_id) FROM sys.foreign_key_columns fc WHERE fc.constraint_object_id=f.object_id))
                   WHEN k.object_id IS NOT NULL THEN
                       (SELECT STRING_AGG(CONCAT(COL_NAME(ix.object_id,ix.column_id),N':',ix.is_descending_key),N',') WITHIN GROUP(ORDER BY ix.key_ordinal)
                        FROM sys.index_columns ix WHERE ix.object_id=k.parent_object_id AND ix.index_id=k.unique_index_id AND ix.key_ordinal>0)
                   ELSE N'' END),COALESCE(c.is_disabled,f.is_disabled,0),
               COALESCE(c.is_not_trusted,f.is_not_trusted,0)
        FROM sys.objects o JOIN sys.tables t ON t.object_id=o.parent_object_id
        JOIN sys.schemas s ON s.schema_id=t.schema_id
        LEFT JOIN sys.check_constraints c ON c.object_id=o.object_id
        LEFT JOIN sys.default_constraints d ON d.object_id=o.object_id
        LEFT JOIN sys.foreign_keys f ON f.object_id=o.object_id
        LEFT JOIN sys.key_constraints k ON k.object_id=o.object_id
        LEFT JOIN sys.columns col ON col.object_id=t.object_id AND col.column_id=COALESCE(c.parent_column_id,d.parent_column_id,0)
        WHERE o.type IN ('C','D','F','PK','UQ') AND t.is_ms_shipped=0
        """, connection);
    using var reader = command.ExecuteReader();
    var result = new List<ConstraintState>();
    while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), Convert.ToBoolean(reader.GetValue(6)), Convert.ToBoolean(reader.GetValue(7))));
    return result;
}

record ConstraintState(string Schema, string Table, string Name, string Type, string Column, string Definition, bool Disabled, bool Untrusted);
