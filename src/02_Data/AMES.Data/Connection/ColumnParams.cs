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

    /// <summary>
    /// "테이블.컬럼"(dbo 스키마) 또는 "프로시저.@매개변수"(dbo)의 정보. 없으면 예외 — 매개변수에 적은 컬럼 이름이 틀렸다는 뜻이다.
    /// </summary>
    public static ColumnInfo Info(SqlConnection conn, string tableColumn, SqlTransaction? tx = null)
        => TryInfo(conn, tableColumn, tx)
           ?? throw new InvalidOperationException($"Column {tableColumn} was not found in dbo — check the parameter's column tag.");

    public static ColumnInfo? TryInfo(SqlConnection conn, string tableColumn, SqlTransaction? tx = null)
    {
        var map = Cache.GetOrAdd($"{conn.DataSource}|{conn.Database}", _ => Load(conn, tx));
        return map.TryGetValue(tableColumn, out var info) ? info : null;
    }

    static IReadOnlyDictionary<string, ColumnInfo> Load(SqlConnection conn, SqlTransaction? tx)
    {
        var map = new Dictionary<string, ColumnInfo>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new SqlCommand("""
            SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION,
                   COALESCE(NUMERIC_SCALE, DATETIME_PRECISION) AS Scale
            FROM   INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo'
            UNION ALL
            SELECT SPECIFIC_NAME, PARAMETER_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION,
                   COALESCE(NUMERIC_SCALE, DATETIME_PRECISION)
            FROM   INFORMATION_SCHEMA.PARAMETERS WHERE SPECIFIC_SCHEMA = 'dbo' AND PARAMETER_NAME <> ''
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

    /// <summary>
    /// 조회 조건용(10-13) — 형식은 비교하는 컬럼에서 정하되 값을 검사하지도 자르지도 않는다. 길이는 컬럼과 값 중 긴 쪽이라
    /// 너무 긴 값은 잘려서 다른 행과 맞지 않고 그대로 "없음"이 되며, CP949 에 없는 문자가 든 값은 nvarchar 로 보내 '?' 로
    /// 바뀌지 않게 한다. 컬럼을 모르거나(<paramref name="tableColumn"/> null·DB 에 없음) 값의 형식이 컬럼과 다르면 값의 형식을 쓴다
    /// (문자열 nvarchar · DateTime datetime2 — AddWithValue 의 datetime 처럼 밀리초를 반올림하지 않는다).
    /// </summary>
    public static SqlParameter AddFilter(SqlCommand cmd, string name, string? tableColumn, object? value)
        => AddFilterInfo(cmd, name, tableColumn is null ? null : TryInfo(cmd.Connection, tableColumn, cmd.Transaction), value);

    public static SqlParameter AddFilterInfo(SqlCommand cmd, string name, ColumnInfo? info, object? value)
    {
        var v = value is DBNull ? null : value;
        var type = info?.DataType.ToLowerInvariant();
        switch (type)
        {
            case "varchar" or "char" or "nvarchar" or "nchar" when v is null or string:
            {
                var s = (string?)v;
                var unicode = type.StartsWith('n') || (s is not null && !Encodable(s));
                var len = s is null ? 0 : unicode ? s.Length : Cp949.GetByteCount(s);
                var max = unicode ? 4000 : 8000;
                var size = info!.MaxLength is > 0 and var n && type.StartsWith('n') == unicode ? Math.Max(n, len) : Math.Max(len, 1);
                var p = cmd.Parameters.Add(name, unicode ? SqlDbType.NVarChar : SqlDbType.VarChar, info.MaxLength == -1 || size > max ? -1 : size);
                p.Value = (object?)s ?? DBNull.Value;
                return p;
            }
            case "date" when v is DateTime d && d.TimeOfDay == TimeSpan.Zero:
                return With(cmd.Parameters.Add(name, SqlDbType.Date), d);
            case "date" or "datetime" or "datetime2" or "smalldatetime" when v is null:
                return With(cmd.Parameters.Add(name, TypeOf(type)), null);
            case "tinyint" or "smallint" or "int" or "bigint" when v is null || (IsInteger(v) && Fits(type, Convert.ToInt64(v))):
                return With(cmd.Parameters.Add(name, TypeOf(type)), v);
            case "bit" when v is null or bool:
            case "uniqueidentifier" when v is null or Guid:
                return With(cmd.Parameters.Add(name, TypeOf(type)), v);
            case "decimal" or "numeric" when v is null:
                return With(cmd.Parameters.Add(name, SqlDbType.Decimal), null);
        }
        return AddByValue(cmd, name, v);
    }

    /// <summary>컬럼과 무관한 값(TOP·DATEADD 인자, 여러 컬럼을 잇는 검색어 등) — .NET 형식으로 정한다.</summary>
    static SqlParameter AddByValue(SqlCommand cmd, string name, object? v)
    {
        switch (v)
        {
            case null: return With(cmd.Parameters.Add(name, SqlDbType.NVarChar, 1), null);
            case string s: return With(cmd.Parameters.Add(name, SqlDbType.NVarChar, s.Length > 4000 ? -1 : Math.Max(s.Length, 1)), s);
            case DateTime d: { var p = cmd.Parameters.Add(name, SqlDbType.DateTime2); p.Scale = 7; return With(p, d); }
            case DateOnly d: return With(cmd.Parameters.Add(name, SqlDbType.Date), d.ToDateTime(TimeOnly.MinValue));
            case bool b: return With(cmd.Parameters.Add(name, SqlDbType.Bit), b);
            case byte or short or int: return With(cmd.Parameters.Add(name, SqlDbType.Int), Convert.ToInt32(v));
            case long l: return With(cmd.Parameters.Add(name, SqlDbType.BigInt), l);
            case Guid g: return With(cmd.Parameters.Add(name, SqlDbType.UniqueIdentifier), g);
            default: { var p = new SqlParameter(name, v); cmd.Parameters.Add(p); return p; }   // decimal·double·TimeSpan·byte[] — SqlClient 추론
        }
    }

    static SqlParameter With(SqlParameter p, object? v) { p.Value = v ?? DBNull.Value; return p; }
    static bool IsInteger(object v) => v is byte or sbyte or short or ushort or int or uint or long;
    static bool Fits(string type, long n) => type switch
    {
        "tinyint" => n is >= byte.MinValue and <= byte.MaxValue,
        "smallint" => n is >= short.MinValue and <= short.MaxValue,
        "int" => n is >= int.MinValue and <= int.MaxValue,
        _ => true,
    };

    static bool Encodable(string s)
    {
        try { Cp949.GetByteCount(s); return true; }
        catch (EncoderFallbackException) { return false; }
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
