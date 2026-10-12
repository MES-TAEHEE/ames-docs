using System.Collections.Concurrent;
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Connection;

/// <summary>
/// 매개변수 형식·길이를 그 값이 들어가는 DB 컬럼에서 정한다(10-12 — 공용 헬퍼의 AddWithValue 대체).
/// 컬럼 정보(INFORMATION_SCHEMA.COLUMNS)는 서버·DB 마다 처음 쓸 때 한 번 읽어 캐시한다.
/// 저장 전에 값을 검사해 컬럼에 맞지 않으면 <see cref="DbValueException"/> — 형식을 명시하면 SqlClient 가 긴 값을 예외 없이
/// 잘라 보내므로 검사가 없으면 잘린 채 저장된다. varchar·char 는 CP949 바이트(한글 2바이트)로 세고 CP949 에 없는 문자도 거부한다
/// (그대로 보내면 '?' 로 저장된다). decimal 은 컬럼 소수 자리로 반올림(서버 변환과 같은 결과)하고 정수부가 넘치면 거부한다.
/// </summary>
public static class ColumnParams
{
    public sealed record ColumnInfo(string Table, string Column, string DataType, int? MaxLength, byte? Precision, int? Scale);

    static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, ColumnInfo>> Cache = new(StringComparer.OrdinalIgnoreCase);

    static Encoding? _cp949;
    static Encoding Cp949
    {
        get
        {
            if (_cp949 is not null) return _cp949;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
    }

    /// <summary>"테이블.컬럼"(dbo 스키마)의 정보. 없으면 예외 — 매개변수에 적은 컬럼 이름이 틀렸다는 뜻이다.</summary>
    public static ColumnInfo Info(SqlConnection conn, string tableColumn, SqlTransaction? tx = null)
    {
        var map = Cache.GetOrAdd($"{conn.DataSource}|{conn.Database}", _ => Load(conn, tx));
        return map.TryGetValue(tableColumn, out var info)
            ? info
            : throw new InvalidOperationException($"Column {tableColumn} was not found in dbo — check the parameter's column tag.");
    }

    static IReadOnlyDictionary<string, ColumnInfo> Load(SqlConnection conn, SqlTransaction? tx)
    {
        var map = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("""
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION,
                   COALESCE(NUMERIC_SCALE, DATETIME_PRECISION) AS Scale
            FROM   INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo'
            """, conn, tx);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var info = new ColumnInfo(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetByte(4),
                r.IsDBNull(5) ? null : Convert.ToInt32(r.GetValue(5)));
            map[$"{info.Table}.{info.Column}"] = info;
        }
        return map;
    }

    /// <summary>컬럼 형식으로 매개변수를 만들어 붙인다. 값이 컬럼에 맞지 않으면 <see cref="DbValueException"/>.</summary>
    public static SqlParameter Add(SqlCommand cmd, string name, string tableColumn, object? value)
        => Add(cmd, name, Info(cmd.Connection, tableColumn, cmd.Transaction), value);

    public static SqlParameter Add(SqlCommand cmd, string name, ColumnInfo info, object? value)
    {
        var p = cmd.Parameters.Add(name, TypeOf(info.DataType));
        var v = value is null || value is DBNull ? null : value;
        switch (info.DataType.ToLowerInvariant())
        {
            case "varchar": case "char":
                p.Size = info.MaxLength ?? -1;
                if (v is not null) { var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!; CheckBytes(info, s); v = s; }
                break;
            case "nvarchar": case "nchar":
                p.Size = info.MaxLength ?? -1;
                if (v is not null)
                {
                    var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;
                    if (info.MaxLength is > 0 and var max && s.Length > max)
                        throw new DbValueException(info, DbValueProblem.TooLong, max, s.Length);
                    v = s;
                }
                break;
            case "varbinary": case "binary":
                p.Size = info.MaxLength ?? -1;
                if (v is byte[] b && info.MaxLength is > 0 and var bmax && b.Length > bmax)
                    throw new DbValueException(info, DbValueProblem.TooLong, bmax, b.Length);
                break;
            case "decimal": case "numeric":
                p.Precision = info.Precision ?? 18; p.Scale = (byte)(info.Scale ?? 0);
                if (v is not null)
                {
                    var d = Math.Round(Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture), p.Scale, MidpointRounding.AwayFromZero);
                    var intDigits = p.Precision - p.Scale;
                    if (Math.Abs(Math.Truncate(d)) >= (decimal)Math.Pow(10, intDigits))
                        throw new DbValueException(info, DbValueProblem.OutOfRange, intDigits, null);
                    v = d;
                }
                break;
            case "tinyint": CheckRange(info, v, byte.MinValue, byte.MaxValue); break;
            case "smallint": CheckRange(info, v, short.MinValue, short.MaxValue); break;
            case "int": CheckRange(info, v, int.MinValue, int.MaxValue); break;
            case "datetime2": case "time": case "datetimeoffset":
                p.Scale = (byte)(info.Scale ?? 7); break;
        }
        p.Value = v ?? DBNull.Value;
        return p;
    }

    static void CheckBytes(ColumnInfo info, string s)
    {
        int bytes;
        try { bytes = Cp949.GetByteCount(s); }
        catch (EncoderFallbackException ex)
        {
            throw new DbValueException(info, DbValueProblem.Unencodable, null, null, ex.CharUnknown != default ? ex.CharUnknown.ToString() : ex.CharUnknownHigh.ToString() + ex.CharUnknownLow);
        }
        if (info.MaxLength is > 0 and var max && bytes > max)
            throw new DbValueException(info, DbValueProblem.TooLong, max, bytes, bytesUnit: true);
    }

    static void CheckRange(ColumnInfo info, object? v, long min, long max)
    {
        if (v is null) return;
        var n = Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
        if (n < min || n > max) throw new DbValueException(info, DbValueProblem.OutOfRange, null, null);
    }

    static SqlDbType TypeOf(string dataType) => dataType.ToLowerInvariant() switch
    {
        "varchar" => SqlDbType.VarChar, "char" => SqlDbType.Char,
        "nvarchar" => SqlDbType.NVarChar, "nchar" => SqlDbType.NChar,
        "text" => SqlDbType.Text, "ntext" => SqlDbType.NText,
        "int" => SqlDbType.Int, "bigint" => SqlDbType.BigInt, "smallint" => SqlDbType.SmallInt, "tinyint" => SqlDbType.TinyInt,
        "bit" => SqlDbType.Bit, "decimal" or "numeric" => SqlDbType.Decimal, "money" => SqlDbType.Money,
        "float" => SqlDbType.Float, "real" => SqlDbType.Real,
        "date" => SqlDbType.Date, "datetime" => SqlDbType.DateTime, "datetime2" => SqlDbType.DateTime2,
        "smalldatetime" => SqlDbType.SmallDateTime, "time" => SqlDbType.Time, "datetimeoffset" => SqlDbType.DateTimeOffset,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "varbinary" => SqlDbType.VarBinary, "binary" => SqlDbType.Binary,
        _ => throw new NotSupportedException($"Column type {dataType} is not mapped."),
    };
}

public enum DbValueProblem { TooLong, Unencodable, OutOfRange }

/// <summary>
/// 값이 저장될 컬럼에 맞지 않는다(너무 김·CP949 에 없는 문자·범위 밖). 화면은 <see cref="Exception.Message"/> 를 그대로 보이므로
/// 문구는 <see cref="Formatter"/>(Web 이 기동 때 다국어로 지정)로 만든다 — 지정이 없으면 영어.
/// </summary>
public sealed class DbValueException : ArgumentException
{
    public static Func<DbValueException, string>? Formatter { get; set; }

    public string Table { get; }
    public string Column { get; }
    public DbValueProblem Problem { get; }
    public int? Limit { get; }
    public int? Actual { get; }
    public bool BytesUnit { get; }
    public string? BadChar { get; }

    public DbValueException(ColumnParams.ColumnInfo info, DbValueProblem problem, int? limit, int? actual, string? badChar = null, bool bytesUnit = false)
        : base($"{info.Table}.{info.Column}: {problem}")
    {
        Table = info.Table; Column = info.Column; Problem = problem; Limit = limit; Actual = actual; BytesUnit = bytesUnit; BadChar = badChar;
    }

    public override string Message
    {
        get
        {
            try { if (Formatter?.Invoke(this) is { Length: > 0 } s) return s; } catch { }
            return Problem switch
            {
                DbValueProblem.TooLong     => $"{Column} is too long — up to {Limit} {(BytesUnit ? "bytes (Korean letters take 2)" : "characters")}, got {Actual}.",
                DbValueProblem.Unencodable => $"{Column} contains a character that cannot be stored: '{BadChar}'.",
                _                          => $"{Column} is out of the allowed range.",
            };
        }
    }
}
