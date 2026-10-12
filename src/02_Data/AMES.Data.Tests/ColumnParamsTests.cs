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
