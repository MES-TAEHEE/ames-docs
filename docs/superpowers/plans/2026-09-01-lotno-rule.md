# INJ LotNo 9자리 채번 신규칙 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** INJ 원천 Lot 코드를 타임스탬프 40자 형식에서 9자리 신규칙(`[년1][월1][일1][라인코드2][순번4]`, 예 `A91I10001`)으로 교체하고, 채번을 원자적 카운터로 전환한다.

**Architecture:** 인코딩·채번 로직을 `AMES.Data`의 static `LotNoGenerator`로 모으고, `InjLotRepository`의 두 생성 경로(에이전트 자동 `CreateRawLot`, Pop 수동 `CreateManualRawLots`)가 호출자 트랜잭션 안에서 이를 호출한다. 순번은 신설 `SYS_LotSeq` 카운터 테이블의 `UPDATE … OUTPUT` 원자 증가로 채번하며 `MAX+1` 스캔·"1ms 밀기" 중복 회피를 제거한다. DB 변경은 관례대로 정본(`AMES_Schema.sql`) 반영 + 기존 DB용 멱등 `migrate_lotno_rule.sql` 두 갈래.

**Tech Stack:** .NET 10 / ADO.NET(Microsoft.Data.SqlClient) / SQL Server 2022 / xUnit(SkippableFact 통합 테스트)

**Spec:** `docs/superpowers/specs/2026-09-01-lotno-rule-design.md`

**커밋 방침:** 사용자 선호 — 커밋 개수 최소화. DB 스키마 1커밋 + 기능 본체(생성기+리포지토리+테스트+문서) 1커밋. 태스크마다 커밋하지 않는다.

**테스트 DB:** 통합 테스트는 `AMES.InjAgent.Tests`의 기본 연결(원격 `98.95.142.192,1433`, `AMES_TEST_CONN` 환경변수로 덮어쓰기 가능)을 쓴다. Task 1 마이그레이션이 그 DB에 먼저 적용되어야 통과한다.

---

## 파일 구조

| 파일 | 역할 |
|---|---|
| Create `dist/migrate_lotno_rule.sql` | 기존 DB용 멱등 마이그레이션 (컬럼·테이블·인덱스·시드) |
| Modify `dist/AMES_Schema.sql` | 정본 동기화 — MD_Line.LotPrefix, SYS_LotSeq, LotCode 유니크 인덱스, 라인 시드 |
| Create `src/02_Data/AMES.Data/Services/LotNoGenerator.cs` | 인코딩(순수 static) + 원자 채번 |
| Modify `src/02_Data/AMES.Data/Repositories/InjLotRepository.cs` | 두 생성 경로를 생성기 호출로 교체 |
| Create `src/07_Etc/AMES.InjAgent.Tests/LotNoGeneratorTests.cs` | 인코딩 경계 단위 테스트 (DB 불필요) |
| Modify `src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs` | 채번·동시성·예외 통합 테스트 |
| Modify `CLAUDE.md` | 마이그레이션 목록에 `migrate_lotno_rule.sql` 추가 |

---

### Task 1: DB 마이그레이션 — migrate_lotno_rule.sql + 정본 반영

**Files:**
- Create: `dist/migrate_lotno_rule.sql`
- Modify: `dist/AMES_Schema.sql:649-664` (MD_Line CREATE), `dist/AMES_Schema.sql:3573-3580` (MD_Line 시드), `dist/AMES_Schema.sql:1584-1586` (tbl_Lot 직후)

- [ ] **Step 1: migrate_lotno_rule.sql 작성**

`migrate_inj_lot_print_claim.sql`의 스타일(멱등, 배치별 가드, `-b` 전제)을 따른다:

```sql
-- ════════════════════════════════════════════════════════════════════════
-- migrate_lotno_rule.sql — INJ LotNo 9자리 신규칙 채번 기반
--
--   LotCode 를 타임스탬프 40자에서 9자리([년1][월1][일1][라인코드2][순번4])로
--   전환한다. 순번은 SYS_LotSeq 카운터의 원자 증가 — MAX+1 스캔을 쓰지 않는다.
--   년은 (연도-2026) mod 26 → A~Z 26년 순환. 월 1~9/A~C, 일 1~9/A~V.
--
-- 비파괴·재실행 가능(idempotent). 적용 (-b 필수):
--   sqlcmd(ODBC17 전체경로) -S <server>,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_lotno_rule.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;   -- 필터드 인덱스 생성에 필수
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.MD_Line') AND name = N'LotPrefix')
  ALTER TABLE dbo.MD_Line ADD [LotPrefix] CHAR(2) NULL;  -- LotNo 라인코드 2자
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.MD_Line') AND name = N'UX_MD_Line_LotPrefix')
  CREATE UNIQUE INDEX UX_MD_Line_LotPrefix
      ON dbo.MD_Line([LotPrefix]) WHERE [LotPrefix] IS NOT NULL;
GO

-- INJ 라인 시드. 신규 라인은 마스터 등록 시 부여한다.
UPDATE dbo.MD_Line SET LotPrefix = 'I1' WHERE LineID = 'LINE-INJ-01' AND LotPrefix IS NULL;
UPDATE dbo.MD_Line SET LotPrefix = 'I2' WHERE LineID = 'LINE-INJ-02' AND LotPrefix IS NULL;
GO

-- 채번 카운터. Header = 년월일(3) + 라인코드(2). 롤백 시 카운터도 롤백 → 결번 없음.
IF OBJECT_ID(N'dbo.SYS_LotSeq', N'U') IS NULL
CREATE TABLE dbo.SYS_LotSeq (
  [Header]     CHAR(5)   NOT NULL,
  [LastSeq]    INT       NOT NULL,
  [ModifiedTS] DATETIME2 NOT NULL CONSTRAINT DF_SYS_LotSeq_ModifiedTS DEFAULT SYSDATETIME(),
  CONSTRAINT PK_SYS_LotSeq PRIMARY KEY CLUSTERED ([Header])
);
GO

-- 지금까지 중복은 코드로만 막았다 — DB 를 최종 방어선으로.
-- 기존 데이터에 중복 LotCode 가 있으면 여기서 실패한다. 그 경우 중복을 먼저 정리할 것.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.tbl_Lot') AND name = N'UX_tbl_Lot_LotCode')
  CREATE UNIQUE INDEX UX_tbl_Lot_LotCode
      ON dbo.tbl_Lot([LotCode]) WHERE [LotCode] IS NOT NULL;
GO

PRINT N'✓ migrate_lotno_rule.sql applied';
GO
```

- [ ] **Step 2: AMES_Schema.sql 정본 동기화 (3곳)**

(a) `CREATE TABLE dbo.MD_Line` (649행) — `[ShiftPattern]` 줄 아래에 컬럼 추가:

```sql
  [ShiftPattern]              VARCHAR(20)              NULL,
  [LotPrefix]                 CHAR(2)                  NULL,  -- LotNo 라인코드 2자 (유니크)
```

그리고 `);` `GO` 뒤에 인덱스 추가:

```sql
CREATE UNIQUE INDEX UX_MD_Line_LotPrefix ON dbo.MD_Line([LotPrefix]) WHERE [LotPrefix] IS NOT NULL;
GO
```

(b) MD_Line 시드 INSERT (3574행) — 컬럼 목록에 `LotPrefix` 추가, INJ 라인만 값 부여:

```sql
INSERT INTO dbo.MD_Line (LineID, LineName, WCID, PlantCode, DailyCap, ShiftPattern, LotPrefix, RfidEnabledFlag, Status, CreatedBy, CreatedTS) VALUES
  ('LINE-INJ-01', N'Injection Line 1 (650T)',  'WC-INJ', 'SAV', 4800, '2-SHIFT', 'I1', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-INJ-02', N'Injection Line 2 (850T)',  'WC-INJ', 'SAV', 3600, '2-SHIFT', 'I2', 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-IMG-01', N'Wrapping Line 1',           'WC-IMG', 'SAV', 1200, '2-SHIFT', NULL, 0, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-01', N'Paint Line 1 (Powder)',     'WC-PNT', 'GEO',  800, '3-SHIFT', NULL, 1, 'ACTIVE', 'admin', SYSDATETIME()),
  ('LINE-PNT-02', N'Paint Line 2 (Liquid)',     'WC-PNT', 'GEO',  600, '3-SHIFT', NULL, 1, 'ACTIVE', 'admin', SYSDATETIME());
GO
```

(c) `tbl_Lot` CREATE 종료 `GO`(1586행) 뒤에 유니크 인덱스와 SYS_LotSeq 추가:

```sql
CREATE UNIQUE INDEX UX_tbl_Lot_LotCode ON dbo.tbl_Lot([LotCode]) WHERE [LotCode] IS NOT NULL;
GO

-- ── SYS_LotSeq  (LotNo 채번 카운터 — 헤더별 마지막 순번)
CREATE TABLE dbo.SYS_LotSeq (
  [Header]                    CHAR(5)              NOT NULL,  -- 년월일(3) + 라인코드(2)
  [LastSeq]                   INT                  NOT NULL,
  [ModifiedTS]                DATETIME2            NOT NULL CONSTRAINT DF_SYS_LotSeq_ModifiedTS DEFAULT SYSDATETIME(),
  CONSTRAINT PK_SYS_LotSeq PRIMARY KEY CLUSTERED ([Header])
);
GO
```

※ 정본에만 넣고 `dist/rebuild_db.sh`의 `FILES` 배열에는 추가하지 않는다 — 재구축 경로는 정본이 담당하고, migrate 파일은 기존 DB 전용 (print_claim 전례와 동일).

- [ ] **Step 3: 마이그레이션을 테스트 DB에 적용**

Run (Bash):
```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S 98.95.142.192,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -b -i dist/migrate_lotno_rule.sql
```
Expected: `✓ migrate_lotno_rule.sql applied` (경로 없거나 접속 불가면 로컬 Docker `-S localhost,1433` 시도)

- [ ] **Step 4: 적용 검증**

Run (Bash):
```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S 98.95.142.192,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "SELECT LineID, LotPrefix FROM dbo.MD_Line WHERE LotPrefix IS NOT NULL; SELECT COUNT(*) FROM dbo.SYS_LotSeq;"
```
Expected: `LINE-INJ-01 I1`, `LINE-INJ-02 I2` 두 행 + 카운트 0

- [ ] **Step 5: 커밋 (DB 스키마 1커밋)**

```bash
git add dist/migrate_lotno_rule.sql dist/AMES_Schema.sql
git commit -m "feat(db): LotNo 신규칙 채번 기반 — SYS_LotSeq·MD_Line.LotPrefix·LotCode 유니크"
```

---

### Task 2: LotNoGenerator 인코딩 (TDD)

**Files:**
- Create: `src/07_Etc/AMES.InjAgent.Tests/LotNoGeneratorTests.cs`
- Create: `src/02_Data/AMES.Data/Services/LotNoGenerator.cs`

- [ ] **Step 1: 실패하는 단위 테스트 작성**

`src/07_Etc/AMES.InjAgent.Tests/LotNoGeneratorTests.cs`:

```csharp
using AMES.Data.Services;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>인코딩 순수 함수 테스트 — DB 불필요, 항상 실행된다.</summary>
public class LotNoGeneratorTests
{
    [Theory]
    [InlineData(2026, 'A')]
    [InlineData(2027, 'B')]
    [InlineData(2051, 'Z')]
    [InlineData(2052, 'A')]   // 26년 주기 순환
    [InlineData(2077, 'Z')]
    public void EncodeYear_cycles_every_26_years(int year, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeYear(year));

    [Theory]
    [InlineData(1, '1')]
    [InlineData(9, '9')]
    [InlineData(10, 'A')]
    [InlineData(12, 'C')]
    public void EncodeMonth_digits_then_ABC(int month, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeMonth(month));

    [Theory]
    [InlineData(1, '1')]
    [InlineData(9, '9')]
    [InlineData(10, 'A')]
    [InlineData(31, 'V')]
    public void EncodeDay_digits_then_A_to_V(int day, char expected)
        => Assert.Equal(expected, LotNoGenerator.EncodeDay(day));

    [Fact]
    public void BuildHeader_composes_5_chars()
        => Assert.Equal("A91I1", LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), "I1"));

    [Fact]
    public void BuildHeader_rejects_non_2char_prefix()
        => Assert.Throws<ArgumentException>(() => LotNoGenerator.BuildHeader(new DateTime(2026, 9, 1), "I"));
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter LotNoGeneratorTests`
Expected: 컴파일 오류 — `LotNoGenerator` 타입 없음 (이것이 이 단계의 "실패")

- [ ] **Step 3: 인코딩 구현**

`src/02_Data/AMES.Data/Services/LotNoGenerator.cs`:

```csharp
using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>
/// INJ 원천 Lot 9자리 채번: [년1][월1][일1][라인코드2][순번4] (예 A91I10001).
/// 년은 (연도-2026) mod 26 → A~Z 26년 순환 — Lot 수명이 26년을 넘지 않아 실무 모호성 없음.
/// 순번은 SYS_LotSeq 원자 증가 — MAX+1 스캔의 동시 중복과 테이블 스캔 비용을 피한다.
/// </summary>
public static class LotNoGenerator
{
    public static char EncodeYear(int year)
        => (char)('A' + (((year - 2026) % 26) + 26) % 26);

    public static char EncodeMonth(int month) => month switch
    {
        >= 1 and <= 9   => (char)('0' + month),
        >= 10 and <= 12 => (char)('A' + month - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(month)),
    };

    public static char EncodeDay(int day) => day switch
    {
        >= 1 and <= 9   => (char)('0' + day),
        >= 10 and <= 31 => (char)('A' + day - 10),
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };

    public static string BuildHeader(DateTime date, string linePrefix)
    {
        if (linePrefix.Length != 2)
            throw new ArgumentException($"LotPrefix must be 2 chars: '{linePrefix}'", nameof(linePrefix));
        return $"{EncodeYear(date.Year)}{EncodeMonth(date.Month)}{EncodeDay(date.Day)}{linePrefix}";
    }
}
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter LotNoGeneratorTests`
Expected: PASS (15 테스트) — 커밋은 Task 5 종료 시 일괄

---

### Task 3: LotNoGenerator.NextLotNo — 원자 채번 (TDD)

**Files:**
- Modify: `src/02_Data/AMES.Data/Services/LotNoGenerator.cs` (클래스에 메서드 추가)
- Modify: `src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs` (통합 테스트 추가)

- [ ] **Step 1: 실패하는 통합 테스트 추가**

`InjLotRepositoryTests.cs` 클래스 끝에 추가 (기존 `TryFactory`/`Conn` 재사용):

```csharp
    [SkippableFact]
    public void NextLotNo_increments_within_header_and_rolls_new_header()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();

        var d1 = new DateTime(2026, 9, 1);
        var a = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        var b = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        Assert.Equal(9, a.Length);
        Assert.Equal(a[..5], b[..5]);
        Assert.Equal(int.Parse(a[5..]) + 1, int.Parse(b[5..]));

        var c = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1.AddDays(1));
        Assert.NotEqual(a[..5], c[..5]);   // 날짜가 바뀌면 새 헤더

        tx.Rollback();   // 카운터도 롤백 — 테스트 흔적 없음
    }

    [SkippableFact]
    public void NextLotNo_line_without_prefix_throws()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-IMG-01", DateTime.Now));
        Assert.Contains("LotPrefix", ex.Message);
        tx.Rollback();
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter NextLotNo`
Expected: 컴파일 오류 — `NextLotNo` 없음

- [ ] **Step 3: NextLotNo 구현**

`LotNoGenerator.cs` 클래스에 추가 (`BuildHeader` 아래):

```csharp
    /// <summary>
    /// 호출자의 트랜잭션 안에서 다음 LotNo 를 원자적으로 채번한다.
    /// 커밋 전까지 같은 헤더의 채번이 직렬화되고, 롤백 시 카운터도 롤백된다(결번 없음).
    /// </summary>
    public static string NextLotNo(SqlConnection conn, SqlTransaction tx, string lineId, DateTime date)
    {
        string? prefix;
        using (var cmd = new SqlCommand(
            "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L;", conn, tx))
        {
            cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
            prefix = (cmd.ExecuteScalar() as string)?.Trim();
        }
        if (string.IsNullOrEmpty(prefix))
            throw new InvalidOperationException(
                $"MD_Line.LotPrefix 미등록: {lineId} — 라인 마스터에 2자 코드를 등록해야 채번할 수 있다.");

        var header = BuildHeader(date, prefix);
        var seq = NextSeq(conn, tx, header);
        if (seq > 9999)
            throw new InvalidOperationException($"LotNo 순번 초과(9999): header={header}");
        return header + seq.ToString("D4");
    }

    static int NextSeq(SqlConnection conn, SqlTransaction tx, string header)
    {
        const string updateSql = """
            UPDATE dbo.SYS_LotSeq SET LastSeq += 1, ModifiedTS = SYSDATETIME()
            OUTPUT inserted.LastSeq WHERE Header = @H;
            """;
        int? Exec(string sql)
        {
            using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            return cmd.ExecuteScalar() as int?;
        }

        var seq = Exec(updateSql);
        if (seq is not null) return seq.Value;

        // 그날 그 라인의 첫 채번 — INSERT 경쟁에서 지면 PK 충돌 → UPDATE 재시도 1회.
        try
        {
            using var ins = new SqlCommand(
                "INSERT INTO dbo.SYS_LotSeq (Header, LastSeq) VALUES (@H, 1);", conn, tx);
            ins.Parameters.Add("@H", SqlDbType.Char, 5).Value = header;
            ins.ExecuteNonQuery();
            return 1;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return Exec(updateSql)
                ?? throw new InvalidOperationException($"SYS_LotSeq 채번 실패: {header}");
        }
    }
```

- [ ] **Step 4: 통과 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter NextLotNo`
Expected: PASS 2 (DB 미기동 환경이면 Skip 2 — 그 경우 DB 접속 가능한 환경에서 재확인)

---

### Task 4: InjLotRepository 두 생성 경로 교체 (TDD)

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/InjLotRepository.cs:65-66` (CreateRawLot), `:207-223` (CreateManualRawLots)
- Modify: `src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs`

- [ ] **Step 1: 실패하는 통합 테스트 추가**

`InjLotRepositoryTests.cs`에 추가:

```csharp
    [SkippableFact]
    public void CreateRawLot_uses_9char_rule_with_incrementing_seq()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (id1, c1) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 90001);
        var (id2, c2) = repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 90002);
        try
        {
            Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", c1);
            Assert.Equal(c1[..5], c2[..5]);
            Assert.Equal(int.Parse(c1[5..]) + 1, int.Parse(c2[5..]));
        }
        finally { Cleanup(f, id1); Cleanup(f, id2); }
    }

    [SkippableFact]
    public void CreateManualRawLots_consecutive_seq_and_9char_rule()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var lots = repo.CreateManualRawLots("LINE-INJ-01", "83335-P8000RBQ", null, 3, "E-TEST");
        try
        {
            Assert.Equal(3, lots.Count);
            Assert.All(lots, l => Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", l.LotCode!));
            var seqs = lots.Select(l => int.Parse(l.LotCode![5..])).ToList();
            Assert.Equal(seqs[0] + 1, seqs[1]);
            Assert.Equal(seqs[1] + 1, seqs[2]);
        }
        finally { foreach (var l in lots) Cleanup(f, l.LotId); }
    }

    [SkippableFact]
    public void CreateManualRawLots_line_without_prefix_throws_and_rolls_back()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            repo.CreateManualRawLots("LINE-IMG-01", "83335-P8000RBQ", null, 1, "E-TEST"));
        Assert.Contains("LotPrefix", ex.Message);
    }

    [SkippableFact]
    public void CreateRawLot_parallel_yields_unique_codes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var results = new System.Collections.Concurrent.ConcurrentBag<(int LotId, string LotCode)>();
        Parallel.For(0, 8, i =>
            results.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-650-01", Lh(), 91000 + i)));
        try
        {
            Assert.Equal(8, results.Count);
            Assert.Equal(8, results.Select(r => r.LotCode).Distinct().Count());
        }
        finally { foreach (var (id, _) in results) Cleanup(f, id); }
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter InjLotRepositoryTests`
Expected: 신규 4개 FAIL — LotCode 가 아직 `L260901…` 타임스탬프 형식 (정규식 불일치)

- [ ] **Step 3: CreateRawLot 교체**

`InjLotRepository.cs` 파일 상단에 using 추가:

```csharp
using AMES.Data.Services;
```

65~66행:

```csharp
            var lotCode = $"L{DateTime.Now:yyMMddHHmmssfff}-{lineId}-{map.CavityPos}";
            if (lotCode.Length > 40) throw new InvalidOperationException($"LotCode too long: {lotCode}");
```

을 다음으로 교체:

```csharp
            var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);
```

- [ ] **Step 4: CreateManualRawLots 교체**

(a) 164행 주석에서 LotCode 언급 제거:

```csharp
            // 품번 → 캐비티/색상 매핑. 금형 미지정 WO 는 NULL 로 남긴다.
```

(b) 207행 `var ts = DateTime.Now;` 삭제, 210~223행("1ms 밀기" 루프 전체)을 다음 한 줄로 교체:

```csharp
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);
```

교체 후 for 루프 시작부는 다음 형태가 된다:

```csharp
            for (var i = 0; i < qty; i++)
            {
                var lotCode = LotNoGenerator.NextLotNo(conn, tx, lineId, DateTime.Now);

                int lotId; DateTime createdTs;
                using (var cmd = new SqlCommand("""
```

- [ ] **Step 5: 전체 테스트 통과 확인**

Run: `dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj`
Expected: 전부 PASS (DB 미기동이면 통합분 Skip). 기존 `CreateRawLot_then_appears_in_unconfirmed_list` 등도 형식 무관하게 통과해야 한다.

- [ ] **Step 6: 솔루션 빌드 확인**

Run: `dotnet build src\AMES.sln`
Expected: Build succeeded, 경고 0 (다른 프로젝트에서 LotCode 형식을 파싱하는 곳은 없음 — 완전일치 매칭뿐)

---

### Task 5: 문서 갱신 + 기능 본체 커밋

**Files:**
- Modify: `CLAUDE.md:240` 부근 (DB 스키마 영역 문단)

- [ ] **Step 1: CLAUDE.md 마이그레이션 목록에 추가**

`CLAUDE.md`의 "라벨 발행 선점 컬럼(...)" 문장 뒤에 이어서:

```markdown
LotNo 채번 기반(`SYS_LotSeq` · `MD_Line.LotPrefix` · `tbl_Lot.LotCode` 유니크 인덱스)은 `dist/migrate_lotno_rule.sql` — INJ 원천 Lot 은 9자리 신규칙(`[년1][월1][일1][라인코드2][순번4]`, 년=A~Z 26년 순환)으로 채번되며, `LotPrefix` 미등록 라인은 채번이 예외로 막힌다.
```

- [ ] **Step 2: 기능 본체 커밋 (코드+테스트+문서 1커밋)**

```bash
git add src/02_Data/AMES.Data/Services/LotNoGenerator.cs src/02_Data/AMES.Data/Repositories/InjLotRepository.cs src/07_Etc/AMES.InjAgent.Tests/LotNoGeneratorTests.cs src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs CLAUDE.md
git commit -m "feat(inj): LotNo 9자리 신규칙 채번 — LotNoGenerator + 원자 카운터 전환"
```

※ `appsettings*.json` 로컬 변경분은 스테이징하지 않는다 (사용자 선호 — 원격 IP 는 로컬 전용).

---

## 배포 주의

- **마이그레이션 없이 신버전 Pop/InjAgent 를 배포하면 안 된다** — `SYS_LotSeq`/`LotPrefix` 부재 시 채번이 매번 예외로 죽는다 (에이전트 루프는 dispatch 로그, 수동입력은 토스트로 드러남). 순서: ① `migrate_lotno_rule.sql` → ② 앱 배포.
- 기존 Lot 은 재발번하지 않는다. 신·구 형식 혼재는 완전일치 조회라 무해.
- 새 INJ 라인 등록 시 `MD_Line.LotPrefix` 부여를 잊으면 그 라인은 채번 불가 — 의도된 fail-fast.
