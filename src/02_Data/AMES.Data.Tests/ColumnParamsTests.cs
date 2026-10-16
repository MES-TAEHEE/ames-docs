using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

// 공용 헬퍼 매개변수 = 컬럼 형식·길이(10-12) — 저장 전 검사 규칙. DB 없이 컬럼 정보를 직접 준다
public class ColumnParamsTests
{
    static ColumnParams.ColumnInfo Col(string type, int? len = null, byte? prec = null, int? scale = null) => new("T", "C", type, len, prec, scale);
    static SqlParameter Add(ColumnParams.ColumnInfo c, object? v) => ColumnParams.Add(new SqlCommand(), "@P", c, v);
    static DbValueException Fails(ColumnParams.ColumnInfo c, object? v) => Assert.Throws<DbValueException>(() => Add(c, v));

    [Fact]
    public void Varchar_counts_cp949_bytes()
    {
        var p = Add(Col("varchar", 20), new string('X', 20));
        Assert.Equal(SqlDbType.VarChar, p.SqlDbType);
        Assert.Equal(20, p.Size);
        Add(Col("varchar", 20), new string('가', 10));                       // 한글 10자 = 20바이트
        var e = Fails(Col("varchar", 20), new string('가', 11));             // 22바이트
        Assert.Equal(DbValueProblem.TooLong, e.Problem);
        Assert.True(e.BytesUnit);
        Assert.Equal(22, e.Actual);
        Assert.Equal(DbValueProblem.TooLong, Fails(Col("varchar", 20), new string('X', 21)).Problem);
    }

    [Fact]
    public void Varchar_rejects_characters_cp949_cannot_store()
    {
        var e = Fails(Col("varchar", 20), "A😀");
        Assert.Equal(DbValueProblem.Unencodable, e.Problem);
        Assert.Equal("😀", e.BadChar);
    }

    [Fact]
    public void Nvarchar_counts_characters()
    {
        var p = Add(Col("nvarchar", 60), new string('가', 60));
        Assert.Equal(SqlDbType.NVarChar, p.SqlDbType);
        Assert.Equal(60, p.Size);
        Add(Col("nvarchar", 10), "A😀가");                                   // 이모지도 nvarchar 는 저장된다
        var e = Fails(Col("nvarchar", 60), new string('가', 61));
        Assert.False(e.BytesUnit);
        Assert.Equal(61, e.Actual);
    }

    [Fact]
    public void Max_columns_have_no_length_limit()
    {
        Assert.Equal(-1, Add(Col("nvarchar", -1), new string('가', 10000)).Size);
        Assert.Equal(-1, Add(Col("varbinary", -1), new byte[100000]).Size);
    }

    [Fact]
    public void Decimal_rounds_to_column_scale_and_rejects_integer_overflow()
    {
        var p = Add(Col("decimal", null, 4, 2), 7.926m);
        Assert.Equal(7.93m, p.Value);
        Assert.Equal((byte)4, p.Precision);
        Assert.Equal((byte)2, p.Scale);
        Assert.Equal(99.99m, Add(Col("decimal", null, 4, 2), 99.99m).Value);
        Assert.Equal(DbValueProblem.OutOfRange, Fails(Col("decimal", null, 4, 2), 100m).Problem);
        Assert.Equal(DbValueProblem.OutOfRange, Fails(Col("decimal", null, 4, 2), 99.995m).Problem);   // 반올림하면 100.00
    }

    [Fact]
    public void Integer_ranges_follow_the_column_type()
    {
        Assert.Equal(SqlDbType.SmallInt, Add(Col("smallint"), 32767).SqlDbType);
        Assert.Equal(DbValueProblem.OutOfRange, Fails(Col("smallint"), 32768).Problem);
        Assert.Equal(DbValueProblem.OutOfRange, Fails(Col("tinyint"), -1).Problem);
        Assert.Equal(SqlDbType.Int, Add(Col("int"), int.MaxValue).SqlDbType);
    }

    [Fact]
    public void Null_becomes_dbnull_and_other_types_map()
    {
        Assert.Equal(DBNull.Value, Add(Col("varchar", 20), null).Value);
        Assert.Equal(DBNull.Value, Add(Col("int"), DBNull.Value).Value);
        Assert.Equal(SqlDbType.Bit, Add(Col("bit"), true).SqlDbType);
        Assert.Equal(SqlDbType.Date, Add(Col("date"), DateTime.Today).SqlDbType);
        Assert.Equal(SqlDbType.Time, Add(Col("time", null, null, 7), TimeSpan.FromHours(7)).SqlDbType);
        Assert.Equal(SqlDbType.DateTime2, Add(Col("datetime2", null, null, 7), DateTime.Now).SqlDbType);
        Assert.Throws<NotSupportedException>(() => Add(Col("geography"), null));
    }

    // ── 조회 조건(AddFilter, 10-13) — 형식은 컬럼에서, 값은 검사·절단하지 않는다 ──
    static SqlParameter Filter(ColumnParams.ColumnInfo? c, object? v) => ColumnParams.AddFilterInfo(new SqlCommand(), "@P", c, v);

    [Fact]
    public void Filter_types_strings_from_the_column_and_never_truncates()
    {
        var p = Filter(Col("varchar", 20), "LINE-INJ-01");
        Assert.Equal(SqlDbType.VarChar, p.SqlDbType);
        Assert.Equal(20, p.Size);
        var longer = Filter(Col("varchar", 20), new string('가', 15));     // 30바이트 — 잘리지 않고 그대로 "없음"
        Assert.Equal(30, longer.Size);
        Assert.Equal(new string('가', 15), longer.Value);
        Assert.Equal(SqlDbType.NVarChar, Filter(Col("nvarchar", 60), "가").SqlDbType);
        Assert.Equal(80, Filter(Col("nvarchar", 60), new string('가', 80)).Size);
        Assert.Equal(-1, Filter(Col("varchar", -1), "X").Size);
        Assert.Equal(DBNull.Value, Filter(Col("varchar", 20), null).Value);
    }

    [Fact]
    public void Filter_keeps_characters_cp949_cannot_store()
    {
        var p = Filter(Col("varchar", 20), "A😀");                      // '?' 로 바뀌어 다른 행과 맞지 않게 nvarchar 로
        Assert.Equal(SqlDbType.NVarChar, p.SqlDbType);
        Assert.Equal("A😀", p.Value);
    }

    [Fact]
    public void Filter_dates_keep_time_unless_the_column_is_date_and_value_is_midnight()
    {
        Assert.Equal(SqlDbType.Date, Filter(Col("date"), new DateTime(2026, 10, 13)).SqlDbType);
        Assert.Equal(SqlDbType.DateTime2, Filter(Col("date"), new DateTime(2026, 10, 13, 15, 0, 0)).SqlDbType);
        var p = Filter(Col("datetime", null, null, 3), new DateTime(2026, 10, 13, 15, 0, 0, 1));
        Assert.Equal(SqlDbType.DateTime2, p.SqlDbType);                // AddWithValue 의 datetime 처럼 밀리초를 반올림하지 않는다
        Assert.Equal(7, p.Scale);
    }

    [Fact]
    public void Filter_falls_back_to_the_value_type_when_column_type_does_not_fit()
    {
        Assert.Equal(SqlDbType.SmallInt, Filter(Col("smallint"), 7).SqlDbType);
        Assert.Equal(SqlDbType.Int, Filter(Col("smallint"), 70000).SqlDbType);   // 범위 밖 — 예외 없이 값 형식
        Assert.Equal(SqlDbType.NVarChar, Filter(Col("int"), "12").SqlDbType);     // 문자열 값은 지금처럼 문자열로 비교
        Assert.Equal(SqlDbType.Bit, Filter(Col("bit"), true).SqlDbType);
    }

    [Fact]
    public void Filter_without_a_column_uses_the_value_type()
    {
        Assert.Equal(SqlDbType.NVarChar, Filter(null, "%abc%").SqlDbType);
        Assert.Equal(SqlDbType.Int, Filter(null, 30).SqlDbType);
        Assert.Equal(SqlDbType.Bit, Filter(null, false).SqlDbType);
        Assert.Equal(SqlDbType.DateTime2, Filter(null, DateTime.Now).SqlDbType);
        Assert.Equal(DBNull.Value, Filter(null, null).Value);
    }

    [Fact]
    public void Message_uses_the_formatter_when_set()
    {
        var e = Fails(Col("nvarchar", 3), "가나다라");
        Assert.Contains("up to 3 characters", e.Message);
        DbValueException.Formatter = x => $"{x.Column}:{x.Limit}:{x.Actual}";
        try { Assert.Equal("C:3:4", e.Message); }
        finally { DbValueException.Formatter = null; }
    }
}
