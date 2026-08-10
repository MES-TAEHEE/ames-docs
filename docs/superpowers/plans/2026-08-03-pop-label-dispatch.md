# Pop 라벨 발행 이전 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사출 라벨 발행 책임을 `AMES.InjAgent`에서 `AMES.Pop`의 세션 백그라운드 서비스로 옮긴다.

**Architecture:** Pop이 1초마다 `PR_InjLot`에서 미출력 LOT을 **원자적으로 선점**(claim)해 ZPL을 발행한다. 선점은 신규 컬럼 `PrintClaimTS`/`PrintClaimStation`으로 하고, 성공 시에만 `PrintedCount`를 올린다. 워터마크(세션 시작 시점의 `MAX(LotID)`)는 고정이며, 재시도 판정은 `PrintedCount = 0 AND PrintClaimTS IS NULL`이 담당한다. InjAgent에서는 발행 코드를 완전히 제거한다.

**Tech Stack:** .NET 10, ADO.NET(raw SqlCommand), WinForms + BlazorWebView, xUnit, SQL Server 2022

**Spec:** [docs/superpowers/specs/2026-08-03-pop-label-dispatch-design.md](../specs/2026-08-03-pop-label-dispatch-design.md)

---

## File Structure

| 파일 | 책임 |
|---|---|
| `dist/migrate_inj_lot_print_claim.sql` (신규) | 기존 DB용 멱등 ALTER |
| `dist/AMES_Schema.sql` (수정) | `PR_InjLot` 정본 컬럼 2개 |
| `dist/migrate_inj_agent.sql` (수정) | 같은 정본 (DROP 후 재생성 경로) |
| `AMES.Data/Repositories/InjLotRepository.cs` (수정) | `GetMaxLotId` · `ClaimForPrint` · `ReleasePrintClaim` |
| `AMES.Pop/Services/LabelDispatchPorts.cs` (신규) | `IInjLotClaimStore` · `ILabelSink` — 테스트 경계 |
| `AMES.Pop/Services/LabelDispatcher.cs` (신규) | 발행 루프 순수 로직 (`Tick()` 단위 테스트 가능) |
| `AMES.Pop/Services/LabelDispatchAdapters.cs` (신규) | 위 두 포트의 실제 구현 (PopServices · LabelPrinter) |
| `AMES.Pop.Tests/LabelDispatcherTests.cs` (신규) | 디스패처 단위 테스트 |
| `AMES.Pop/Common/AppConfig.cs` (수정) | `PrinterPollMs` · `PrinterMaxFailures` |
| `AMES.Pop/Forms/PopBlazorForm.cs` (수정) | DI 등록 + 수명 관리 |
| `AMES.Pop/Pages/InjPopups/ManualEntryPopup.razor` (수정) | 인라인 발행 제거 |
| `AMES.Pop/Pages/InjMain.razor` (수정) | 재출력 성공 시 실패 카운터 리셋 |
| InjAgent 6개 파일 (수정/삭제) | 발행 경로 제거 |

`LabelDispatcher`는 타이머를 갖지 않는다. 타이머는 `PopBlazorForm`이 소유하고 `Tick()`을 호출한다 — 이렇게 해야 테스트에서 시간을 기다리지 않고 검증할 수 있다.

---

## Task 1: DB 스키마에 클레임 컬럼 추가

**Files:**
- Create: `dist/migrate_inj_lot_print_claim.sql`
- Modify: `dist/AMES_Schema.sql:1774` (PrintedCount 줄 바로 아래)
- Modify: `dist/migrate_inj_agent.sql:26` (PrintedCount 줄 바로 아래)

- [ ] **Step 1: 멱등 ALTER 스크립트 작성**

`dist/migrate_inj_lot_print_claim.sql`:

```sql
-- ════════════════════════════════════════════════════════════════════════
-- migrate_inj_lot_print_claim.sql — 라벨 발행 선점 컬럼 (Pop 디스패처용)
--
--   라벨 발행 주체가 InjAgent → Pop 으로 이전되면서, 같은 라인에 터미널이
--   여러 대 있어도 한 장만 나가도록 DB 에서 원자적으로 선점해야 한다.
--   PrintedCount 를 선점 플래그로 쓰지 않는 이유: 출력 전에 올리면
--   프린터 장애 시 "카운트는 1인데 실물 라벨은 없는" 거짓 상태가 남는다.
--
-- 비파괴·재실행 가능(idempotent). 적용 (-b 필수 — 아래 가드 주석 참조):
--   sqlcmd(ODBC17 전체경로) -S localhost,1433 -U sa -P ... -d AMES_DEV -f 65001 -b -i dist/migrate_inj_lot_print_claim.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- OBJECT_ID 가 NULL 이면 sys.columns 조회가 0행이라 아래 NOT EXISTS 가 참이 되고,
-- ALTER TABLE 이 "Invalid object name" 으로 죽어 근본 원인을 알기 어렵다.
-- RETURN 은 자기 배치만 끝내므로 -b 없이 실행하면 뒤 배치가 계속 돌다 실패하고도
-- 성공 트레일러를 찍고 exit 0 이 된다 — -b 가 가드 동작에 필수다.
IF OBJECT_ID(N'dbo.PR_InjLot', N'U') IS NULL
BEGIN
  RAISERROR(N'PR_InjLot 없음 — dist/migrate_inj_agent.sql 를 먼저 적용하세요.', 16, 1);
  RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'PrintClaimTS')
  ALTER TABLE dbo.PR_InjLot ADD [PrintClaimTS] DATETIME2 NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'PrintClaimStation')
  ALTER TABLE dbo.PR_InjLot ADD [PrintClaimStation] VARCHAR(20) NULL;
GO

-- 클레임 쿼리는 (PrintedCount, LotID) 로 좁힌 뒤 PrintClaimTS 를 본다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PR_InjLot') AND name = N'IX_PR_InjLot_PrintClaim')
  CREATE INDEX IX_PR_InjLot_PrintClaim
      ON dbo.PR_InjLot([PrintedCount], [LotID]) INCLUDE([PrintClaimTS]);
GO

PRINT N'✓ migrate_inj_lot_print_claim.sql applied';
```

한글이 들어가는 `PRINT`·`RAISERROR` 리터럴에는 **반드시 `N` 접두사**를 붙인다. dev DB collation 이 `SQL_Latin1_General_CP1_CI_AS`(CP1252)라 `N` 없는 리터럴의 한글은 서버 쪽에서 `?`로 영구 손상된다 (`SELECT UNICODE(SUBSTRING('없음',1,1))` → 63).

- [ ] **Step 2: 정본 스키마 2곳에 컬럼 추가**

`dist/AMES_Schema.sql`과 `dist/migrate_inj_agent.sql` **양쪽 모두**에서 `PR_InjLot`의 아래 줄을 찾는다:

```sql
  [PrintedCount]              INT                  NOT NULL DEFAULT 0,
```

바로 아래에 두 줄을 삽입한다 (두 파일 모두 동일):

```sql
  [PrintClaimTS]              DATETIME2                NULL,  -- 라벨 발행 선점 시각 (NULL = 미선점)
  [PrintClaimStation]         VARCHAR(20)              NULL,  -- 선점한 Pop 터미널 StationId
```

인덱스도 **두 파일 모두**에 추가한다. 각 파일에서 `CREATE INDEX IX_PR_InjLot_Equip ...` 줄과 그 뒤의 `GO` 다음에 삽입:

```sql
CREATE INDEX IX_PR_InjLot_PrintClaim ON dbo.PR_InjLot([PrintedCount], [LotID]) INCLUDE([PrintClaimTS]);
GO
```

한쪽에만 넣으면 정본이 갈린다. 지금은 `migrate_inj_agent.sql`이 `PR_InjLot`을 DROP 후 재생성해서 결과가 가려지지만, `AMES_Schema.sql`만 단독 적용하는 경로(신규 환경·스키마 비교·생성기 재실행)에서는 컬럼은 있고 인덱스만 없는 상태가 되고, 1초 폴링 클레임 쿼리가 오류 없이 조용히 clustered index scan으로 떨어진다.

- [ ] **Step 3: 실행 중인 DB에 적용**

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -b -i dist/migrate_inj_lot_print_claim.sql
```

기대: 오류 없이 종료 (출력 없음). 두 번 실행해도 오류가 없어야 한다.

- [ ] **Step 4: 컬럼 생성 확인**

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "SELECT name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.PR_InjLot') AND name LIKE 'PrintClaim%' ORDER BY name;"
```

기대 출력:
```
name
--------------------
PrintClaimStation
PrintClaimTS
```

- [ ] **Step 5: 커밋**

`dist/AMES_Schema.sql`은 **스테이징하지 않는다.** 이 파일에는 사용자가 별도로 진행 중인 미커밋 변경(MD_Item 컬럼, PP_EquipSignal 테이블 등)이 섞여 있어, 함께 커밋하면 무관한 작업이 휩쓸려 들어간다. 파일은 수정하되 워킹 트리에 둔다.

```bash
git add dist/migrate_inj_lot_print_claim.sql dist/migrate_inj_agent.sql
git commit -m "feat(db): add PR_InjLot label print claim columns"
```

커밋 전 `git status --short`로 `dist/AMES_Schema.sql`이 스테이징되지 않았는지 확인한다. `git add -A`/`git add dist/`처럼 넓게 스테이징하지 않는다.

---

## Task 2: 저장소에 클레임 메서드 3종 추가

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/InjLotRepository.cs` (`IncrementPrintedCount` 아래에 추가)

이 태스크는 실제 DB가 있어야 검증되므로 단위 테스트 대신 **Task 8의 수동 동시성 검증**으로 확인한다. 여기서는 컴파일과 스모크 쿼리까지 확인한다.

- [ ] **Step 1: 세 메서드 추가**

`InjLotRepository.cs`의 `IncrementPrintedCount` 메서드가 끝나는 `}` 다음에 삽입한다:

```csharp
    /// <summary>라벨 디스패처 워터마크 — 세션 시작 시점의 라인 최대 INJ LotID.</summary>
    public int GetMaxLotId(string lineId)
    {
        // ProcessCode 필터가 없으면 같은 라인의 비-INJ LOT 이 워터마크를 최신 INJ LOT
        // 너머로 밀어올려, 그 사이 미출력분이 영구 제외된다 (워터마크는 세션 중 전진하지 않음).
        const string sql = "SELECT ISNULL(MAX(LotID),0) FROM dbo.tbl_Lot WHERE LineID = @Line AND ProcessCode = 'INJ';";
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() as int? ?? 0;
    }

    /// <summary>
    /// 미출력 LOT 을 원자적으로 선점하고 라벨 조립용 전체 데이터를 돌려준다.
    /// 같은 라인에 Pop 터미널이 여러 대여도 한 터미널만 각 LOT 을 가져간다.
    /// staleSeconds 가 지난 선점은 회수 대상 — 선점 직후 터미널이 죽어도 라벨이 유실되지 않는다.
    ///
    /// 불변식: staleSeconds > top × 프린터 최악 지연(5초 = ZplPrinter 연결 2초 + 송신 3초).
    /// 이 관계가 깨지면 첫 터미널이 배치를 처리하는 중에 다른 터미널이 뒷부분을 회수해
    /// 같은 라벨이 두 장 나간다. 현재 값: 60 > 5 × 5 = 25.
    ///
    /// 호출측 계약 — 선점만 하므로 뒤처리는 호출자 책임이다:
    ///   출력 성공 → IncrementPrintedCount(lotId)
    ///   출력 실패 → ReleasePrintClaim(lotId, stationId)
    /// 둘 다 안 부르면 staleSeconds 후 자동 재시도된다(= 라벨이 한 장 더 나간다).
    /// </summary>
    public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId,
                                         int staleSeconds = 60, int top = 5)
    {
        var sql = """
            DECLARE @claimed TABLE (LotID INT);

            UPDATE TOP (@Top) e
            SET    e.PrintClaimTS = SYSDATETIME(), e.PrintClaimStation = @Station
            OUTPUT INSERTED.LotID INTO @claimed
            FROM   dbo.PR_InjLot e
            JOIN   dbo.tbl_Lot   l ON l.LotID = e.LotID
            WHERE  l.LineID       = @Line
              AND  e.LotID        > @After
              AND  e.PrintedCount = 0
              AND  (e.PrintClaimTS IS NULL
                    OR e.PrintClaimTS < DATEADD(second, -@Stale, SYSDATETIME()));

            """ + SelectLotView + """

            WHERE  l.LotID IN (SELECT LotID FROM @claimed)
            ORDER  BY l.LotID;
            """;

        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Line",    SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@After",   SqlDbType.Int        ).Value = afterLotId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationId;
        cmd.Parameters.Add("@Stale",   SqlDbType.Int        ).Value = staleSeconds;
        cmd.Parameters.Add("@Top",     SqlDbType.Int        ).Value = top;
        using var rdr = cmd.ExecuteReader();
        var list = new List<InjLotDto>();
        while (rdr.Read()) list.Add(MapToDto(rdr));
        return list;
    }

    /// <summary>출력 실패 시 선점 반납 — 다음 틱에 재시도된다.</summary>
    public void ReleasePrintClaim(int lotId, string stationId)
    {
        // 내가 선점한 것만 반납한다. 스테일 회수로 다른 터미널이 이미 가져간 LOT 을
        // 뒤늦은 실패 보고가 풀어버리면 그 터미널이 출력 중인 라벨이 재선점된다.
        const string sql = """
            UPDATE dbo.PR_InjLot
            SET    PrintClaimTS = NULL, PrintClaimStation = NULL
            WHERE  LotID = @L AND PrintedCount = 0 AND PrintClaimStation = @Station;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L",       SqlDbType.Int        ).Value = lotId;
        cmd.Parameters.Add("@Station", SqlDbType.VarChar, 20).Value = stationId;
        cmd.ExecuteNonQuery();
    }
```

`GetMaxLotId`는 `ProcessCode = 'INJ'`로 거르고 `ClaimForPrint`는 안 거른다 — **의도된 비대칭이다.** 워터마크는 `tbl_Lot`만 읽으므로 직접 걸러야 하지만, 클레임은 `PR_InjLot`과 조인하고 그 테이블은 `CreateRawLot`·`CreateManualRawLots` 두 곳에서만 쓰이며 둘 다 같은 트랜잭션에서 `ProcessCode = 'INJ'`인 `tbl_Lot` 행을 만든다. 클레임 쪽에 필터를 더하는 것은 1Hz 쿼리에 도달 불가능한 조건을 얹는 일이다.

`SelectLotView` 상수 선언부(약 291행)에도 결합 계약을 한 줄 남긴다 — 이제 4곳이 여기에 `WHERE`를 이어 붙인다:

```csharp
    // 호출부가 WHERE 를 이어 붙인다 — 이 상수에 WHERE 를 넣으면 4곳이 동시에 깨진다.
    const string SelectLotView = """
```

`dist/migrate_inj_lot_print_claim.sql`의 기존 인덱스 블록 다음(`PRINT` 앞)에 라인 인덱스도 추가한다. 같은 인덱스를 `dist/AMES_Schema.sql`의 `tbl_Lot` 정의 쪽에도 넣되 **커밋하지 않는다**:

```sql
-- 클레임 조인이 라인으로 좁혀지지 않으면, Pop 이 꺼진 라인의 미출력 LOT 을
-- 살아있는 다른 라인 터미널이 1초마다 전부 스캔한 뒤 버린다.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.tbl_Lot') AND name = N'IX_tbl_Lot_Line')
  CREATE INDEX IX_tbl_Lot_Line ON dbo.tbl_Lot([LineID], [LotID]);
GO
```

- [ ] **Step 2: 빌드 확인**

```bash
dotnet build "src/02_Data/AMES.Data/AMES.Data.csproj" -v q
```

기대: `Build succeeded.` / `0 Error(s)`

- [ ] **Step 3: 클레임 쿼리 스모크 테스트**

기존 데이터에 영향을 주지 않고 문법과 동작을 확인한다 (`@After`를 매우 크게 줘서 0건 반환):

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -b -Q "DECLARE @claimed TABLE (LotID INT); UPDATE TOP (20) e SET e.PrintClaimTS=SYSDATETIME(), e.PrintClaimStation='SMOKE' OUTPUT INSERTED.LotID INTO @claimed FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID=e.LotID WHERE l.LineID='LINE-INJ-01' AND e.LotID > 2147483000 AND e.PrintedCount=0 AND (e.PrintClaimTS IS NULL OR e.PrintClaimTS < DATEADD(second,-30,SYSDATETIME())); SELECT COUNT(*) AS Claimed FROM @claimed;"
```

기대 출력:
```
Claimed
-----------
          0
```

`Claimed`가 0이 아니면 `@After` 조건이 안 먹은 것이므로 쿼리를 다시 확인한다.

- [ ] **Step 4: 커밋**

```bash
git add src/02_Data/AMES.Data/Repositories/InjLotRepository.cs
git commit -m "feat(data): add label print claim/release to InjLotRepository"
```

---

## Task 3: Pop 테스트 프로젝트 신설

**Files:**
- Create: `src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj`
- Create: `src/03_Pop/AMES.Pop.Tests/SmokeTest.cs`
- Modify: `src/03_Pop/AMES.Pop/AMES.Pop.csproj`
- Modify: `src/AMES.sln`

Pop의 `Services`/`Common` 타입은 `internal`이므로 테스트 어셈블리에 노출해야 한다.

- [ ] **Step 1: 테스트 프로젝트 생성**

`src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);MSB3277</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AMES.Pop\AMES.Pop.csproj" />
    <ProjectReference Include="..\..\01_Shared\AMES.Contracts\AMES.Contracts.csproj" />
    <ProjectReference Include="..\..\01_Shared\AMES.Devices\AMES.Devices.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: internal 노출 설정**

`src/03_Pop/AMES.Pop/AMES.Pop.csproj`의 `</PropertyGroup>` 바로 다음(첫 `<ItemGroup>` 앞)에 삽입:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="AMES.Pop.Tests" />
  </ItemGroup>
```

- [ ] **Step 3: 스모크 테스트 작성**

`src/03_Pop/AMES.Pop.Tests/SmokeTest.cs`:

```csharp
using Xunit;

namespace AMES.Pop.Tests;

public class SmokeTest
{
    [Fact]
    public void Test_project_references_pop_assembly()
    {
        var asm = typeof(AMES.Pop.Services.AppState).Assembly;
        Assert.Equal("AMES.Pop", asm.GetName().Name);
    }
}
```

- [ ] **Step 4: 솔루션에 등록**

```bash
dotnet sln src/AMES.sln add src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj
```

기대: `Project ... added to the solution.`

- [ ] **Step 5: 테스트 실행**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" -v q
```

기대: `Passed! - Failed: 0, Passed: 2` (스모크 2개)

> AMES.Pop.exe가 실행 중이면 파일 잠금(MSB3027)으로 빌드가 실패한다. 실패 시 실행 중인 Pop을 종료하고 재시도한다.

- [ ] **Step 6: 커밋**

```bash
git add src/03_Pop/AMES.Pop.Tests src/03_Pop/AMES.Pop/AMES.Pop.csproj src/AMES.sln
git commit -m "test(pop): add AMES.Pop.Tests project"
```

---

## Task 4: 디스패처 포트 정의 + 첫 실패 테스트

**Files:**
- Create: `src/03_Pop/AMES.Pop/Services/LabelDispatchPorts.cs`
- Create: `src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs`
- Create: `src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs`:

```csharp
using AMES.Contracts.Dto;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class LabelDispatcherTests
{
    sealed class FakeSource : IInjLotClaimStore
    {
        public int MaxLotId = 100;
        public List<InjLotDto> NextClaim = new();
        public List<int> Claimed = new();
        public List<int> Released = new();
        public List<(int LotId, string Station)> ReleasedBy = new();
        public List<int> Incremented = new();
        public int LastAfterLotId = -1;
        public bool FailClaim;

        public int GetMaxLotId(string lineId) => MaxLotId;

        public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId)
        {
            LastAfterLotId = afterLotId;
            if (FailClaim) throw new InvalidOperationException("db down");
            var batch = NextClaim;
            NextClaim = new List<InjLotDto>();
            Claimed.AddRange(batch.Select(l => l.LotId));
            return batch;
        }

        public void ReleasePrintClaim(int lotId, string stationId)
        {
            ReleasedBy.Add((lotId, stationId));
            Released.Add(lotId);
        }

        public void IncrementPrintedCount(int lotId) => Incremented.Add(lotId);
    }

    sealed class FakeSink : ILabelSink
    {
        public List<int> Printed = new();
        public HashSet<int> FailFor = new();
        public void Print(InjLotDto lot)
        {
            if (FailFor.Contains(lot.LotId)) throw new IOException("printer offline");
            Printed.Add(lot.LotId);
        }
    }

    static InjLotDto Lot(int id, string status = "RAW") => new()
    {
        LotId = id, LotCode = $"L{id}", ItemNo = "83335-P8000RBQ",
        LineId = "LINE-INJ-01", ConfirmStatus = status, CreatedTS = new DateTime(2026, 8, 3),
    };

    static (LabelDispatcher D, FakeSource S, FakeSink K) Build(int maxFailures = 3)
    {
        var s = new FakeSource();
        var k = new FakeSink();
        var d = new LabelDispatcher(s, k, maxFailures, _ => { });
        d.Start("LINE-INJ-01", "POP-DEV-01");
        return (d, s, k);
    }

    [Fact]
    public void Start_captures_watermark_and_claims_only_newer_lots()
    {
        var (d, s, _) = Build();
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Equal(100, s.LastAfterLotId);   // 시작 시점 MAX(LotID)
    }
}
```

- [ ] **Step 2: 테스트가 컴파일 실패하는지 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" -v q
```

기대: FAIL — `error CS0246: The type or namespace name 'IInjLotClaimStore' could not be found`

- [ ] **Step 3: 포트 정의**

`src/03_Pop/AMES.Pop/Services/LabelDispatchPorts.cs`:

```csharp
using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>라벨 디스패처가 보는 LOT 저장소 — 테스트에서 대체 가능하도록 좁게 정의.</summary>
internal interface IInjLotClaimStore
{
    int GetMaxLotId(string lineId);
    List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId);

    /// <summary>내가 선점한 것만 반납된다 — stationId 가 소유권 검증에 쓰인다.</summary>
    void ReleasePrintClaim(int lotId, string stationId);

    void IncrementPrintedCount(int lotId);
}

/// <summary>라벨 출력 대상 — 실패 시 예외.</summary>
internal interface ILabelSink
{
    void Print(InjLotDto lot);
}
```

- [ ] **Step 4: 디스패처 최소 구현**

`src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs`:

```csharp
using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>
/// 사출 라벨 자동 발행 루프. 타이머는 갖지 않는다 — 호출자가 Tick() 을 주기적으로
/// 부르고, 테스트는 시간을 기다리지 않고 직접 부른다.
///
/// 워터마크는 Start() 시점에 한 번만 잡고 전진시키지 않는다. 전진시키면
/// 출력 실패로 반납한 LOT 이 LotID > watermark 조건에서 영구 제외되어
/// 재시도가 불가능해진다. 재시도 판정은 DB 쪽 PrintedCount=0 AND PrintClaimTS IS NULL 이 담당.
/// </summary>
internal sealed class LabelDispatcher
{
    private readonly IInjLotClaimStore _source;
    private readonly ILabelSink         _sink;
    private readonly int                _maxFailures;
    private readonly Action<string>     _log;

    private string? _lineId;
    private string  _stationId = string.Empty;
    private int     _watermark;

    public LabelDispatcher(IInjLotClaimStore source, ILabelSink sink,
                           int maxFailures, Action<string> log)
    {
        _source      = source;
        _sink        = sink;
        _maxFailures = maxFailures;
        _log         = log;
    }

    public void Start(string lineId, string stationId)
    {
        _lineId    = lineId;
        _stationId = stationId;
        _watermark = _source.GetMaxLotId(lineId);
    }

    public void Tick()
    {
        if (_lineId is null) return;
        var batch = _source.ClaimForPrint(_lineId, _watermark, _stationId);
        foreach (var lot in batch)
        {
            _sink.Print(lot);
            _source.IncrementPrintedCount(lot.LotId);
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" -v q
```

기대: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: 커밋**

```bash
git add src/03_Pop/AMES.Pop/Services src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs
git commit -m "feat(pop): add label dispatcher skeleton with watermark"
```

---

## Task 5: 성공·실패 경로

**Files:**
- Modify: `src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs`
- Modify: `src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs`

- [ ] **Step 0: 앞선 리뷰가 지적한 테스트 표현력 문제 두 가지를 먼저 고친다**

**(a) `FakeSource`가 반납→재선점을 모델링하지 않는다.** 실제 저장소는 반납 시 `PrintClaimTS`를 NULL로 되돌려 다음 틱에 그 LOT이 **다시 잡히게** 한다. 이 왕복이 워터마크를 고정하는 이유 전부다. 지금 fake는 반납을 기록만 하므로, 이 상태로 Task 5·6을 끝내면 테스트 15개가 초록인데 정작 지키려는 불변식은 자동 검증이 0이다.

`FakeSource`를 아래로 교체한다 (필드 `_issued` 추가, `ClaimForPrint`·`ReleasePrintClaim` 수정):

```csharp
    sealed class FakeSource : IInjLotClaimStore
    {
        public int MaxLotId = 100;
        public List<InjLotDto> NextClaim = new();
        public List<int> Claimed = new();
        public List<int> Released = new();
        public List<(int LotId, string Station)> ReleasedBy = new();
        public List<int> Incremented = new();
        public int LastAfterLotId = -1;
        public bool FailClaim = false;

        // 선점했던 LOT — 반납되면 다시 대기열로 돌아간다 (실제 DB 의 PrintClaimTS = NULL 효과).
        private readonly Dictionary<int, InjLotDto> _issued = new();

        public int GetMaxLotId(string lineId) => MaxLotId;

        public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId)
        {
            LastAfterLotId = afterLotId;
            if (FailClaim) throw new InvalidOperationException("db down");
            // 실제 저장소의 AND e.LotID > @After 를 흉내낸다 — 이게 없으면
            // 워터마크를 전진시키는 회귀를 아래 재시도 테스트가 잡지 못한다.
            var batch = NextClaim.Where(l => l.LotId > afterLotId).ToList();
            NextClaim = NextClaim.Where(l => l.LotId <= afterLotId).ToList();
            foreach (var lot in batch) _issued[lot.LotId] = lot;
            Claimed.AddRange(batch.Select(l => l.LotId));
            return batch;
        }

        public void ReleasePrintClaim(int lotId, string stationId)
        {
            ReleasedBy.Add((lotId, stationId));
            Released.Add(lotId);
            if (_issued.TryGetValue(lotId, out var lot)) NextClaim.Add(lot);
        }

        public void IncrementPrintedCount(int lotId) => Incremented.Add(lotId);
    }
```

`FailClaim`에 명시적 초기화(`= false`)를 붙인 것은 CS0649 경고를 없애기 위함이다 — 이 필드는 Task 6에서야 대입된다.

**(b) 테스트 이름이 검증하지 못하는 것을 약속한다.** `Start_captures_watermark_and_claims_only_newer_lots`는 `LastAfterLotId == 100`만 본다. "newer lots만 잡는다"는 필터는 SQL 쪽 `AND e.LotID > @After`에 있어 디스패처 단위 테스트로는 닿지 않는다. 이름을 바꾸고 주석을 단다:

```csharp
    [Fact]
    public void Start_captures_watermark_and_passes_it_as_claim_floor()
    {
        // LotID > @After 필터 자체는 DB 쪽이라 여기서 검증할 수 없다 — Task 10 수동 검증 담당.
        var (d, s, _) = Build();
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Equal(100, s.LastAfterLotId);   // 시작 시점 MAX(LotID)
    }
```

- [ ] **Step 1: 실패 테스트 3개 추가**

`LabelDispatcherTests.cs`의 `Start_captures_watermark_and_passes_it_as_claim_floor` 테스트 다음에 추가:

```csharp
    [Fact]
    public void Successful_print_increments_count_and_keeps_claim()
    {
        var (d, s, k) = Build();
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));
        d.Tick();

        Assert.Equal(new[] { 101, 102 }, k.Printed);
        Assert.Equal(new[] { 101, 102 }, s.Incremented);
        Assert.Empty(s.Released);
    }

    [Fact]
    public void Failed_print_releases_claim_and_does_not_increment()
    {
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));
        d.Tick();

        Assert.Equal(new[] { 102 }, k.Printed);        // 102 는 계속 진행
        Assert.Equal(new[] { 102 }, s.Incremented);
        Assert.Equal(new[] { 101 }, s.Released);       // 101 만 반납
    }

    [Fact]
    public void Release_passes_own_station_for_ownership_check()
    {
        // 스테일 회수로 다른 터미널이 가져간 LOT 을 뒤늦은 실패가 풀어버리면
        // 그 터미널이 출력 중인 라벨이 재선점된다 — 반납은 소유자만.
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();

        Assert.Equal(new[] { (101, "POP-DEV-01") }, s.ReleasedBy);
    }

    [Fact]
    public void Ng_blocked_lots_are_printed_too()
    {
        var (d, s, k) = Build();
        s.NextClaim.Add(Lot(101, "NG_BLOCKED"));
        d.Tick();

        Assert.Equal(new[] { 101 }, k.Printed);
    }

    [Fact]
    public void Failed_lot_is_retried_on_the_next_tick()
    {
        // 이 태스크의 핵심 불변식. 배치에 성공분을 섞는 이유: 워터마크를
        // "성공한 LOT 까지만" 전진시키는 회귀가 가장 그럴듯한 모양인데,
        // 실패분만 있는 배치로는 그걸 잡지 못한다.
        var (d, s, k) = Build();
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        s.NextClaim.Add(Lot(102));

        d.Tick();                                      // 101 실패 → 반납, 102 성공
        Assert.Equal(new[] { 102 }, k.Printed);
        Assert.Equal(new[] { 101 }, s.Released);

        k.FailFor.Clear();                             // 프린터 복구
        d.Tick();                                      // 101 이 다시 잡혀 성공
        Assert.Equal(new[] { 102, 101 }, k.Printed);
        Assert.Equal(new[] { 102, 101 }, s.Incremented);
        Assert.Equal(100, s.LastAfterLotId);           // 워터마크는 그대로
    }
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" --filter "FullyQualifiedName~LabelDispatcherTests" -v q
```

기대: FAIL — `Failed_print_releases_claim_and_does_not_increment`에서 `IOException: printer offline`이 밖으로 던져진다.

- [ ] **Step 3: Tick 에 예외 처리 추가**

`LabelDispatcher.cs`의 `Tick()` 메서드를 통째로 교체:

```csharp
    public void Tick()
    {
        if (_lineId is null) return;

        var batch = _source.ClaimForPrint(_lineId, _watermark, _stationId);
        foreach (var lot in batch)
        {
            try { _sink.Print(lot); }
            catch (Exception ex)
            {
                // 반납을 먼저 — _log 가 던지면 반납이 유실되고 배치 나머지가 중단된다.
                try { _source.ReleasePrintClaim(lot.LotId, _stationId); }
                catch (Exception rel) { _log($"claim release failed ({lot.LotCode}): {rel.Message}"); }
                _log($"label print failed ({lot.LotCode}): {ex.Message}");
                continue;
            }

            // 라벨은 이미 나왔다 — 반납하면 다음 틱에 두 장째가 나온다. 카운트가 0 으로
            // 남으므로 스테일 회수로만 재시도되고, 그건 의도된 동작이다.
            try { _source.IncrementPrintedCount(lot.LotId); }
            catch (Exception ex) { _log($"printed but count not incremented ({lot.LotCode}): {ex.Message}"); }
        }
    }
```

출력 성공과 카운트 갱신을 같은 `try`에 두면 안 된다. 라벨이 물리적으로 나온 뒤 DB 증가만 실패했을 때 `label print failed` 로 거짓 로그를 남기고 클레임을 반납해, 다음 틱(~1초)에 같은 라벨이 두 장째 나온다. 이 설계 전체가 두 장을 막으려고 있다. Task 6 에서는 이 구분이 더 중요해진다 — 합쳐두면 일시적 DB 장애가 프린터 실패로 집계되어 멀쩡한 프린터에서 자동 발행이 멈춘다.

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" -v q
```

기대: `Passed! - Failed: 0, Passed: 8`

- [ ] **Step 5: 커밋**

```bash
git add src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs
git commit -m "feat(pop): release claim on print failure so next tick retries"
```

---

## Task 6: 연속 실패 시 자동 정지 + 재진입 금지

**Files:**
- Modify: `src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs`
- Modify: `src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs`

- [ ] **Step 1: 실패 테스트 5개 추가**

`LabelDispatcherTests.cs`의 마지막 테스트 다음에 추가:

```csharp
    [Fact]
    public void Stops_after_max_consecutive_failures()
    {
        var (d, s, k) = Build(maxFailures: 3);
        k.FailFor.Add(101); k.FailFor.Add(102); k.FailFor.Add(103);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102), Lot(103) });
        d.Tick();

        Assert.True(d.IsStopped);
        Assert.Equal(3, s.Released.Count);

        s.NextClaim.Add(Lot(104));      // 정지 후에는 클레임조차 하지 않는다
        d.Tick();
        Assert.Empty(k.Printed);
        Assert.DoesNotContain(104, s.Claimed);
    }

    [Fact]
    public void One_success_resets_the_failure_counter()
    {
        // maxFailures 를 2 로 둬야 한다. 3 이면 실패가 2번뿐이라 카운터가
        // 연속이든 누적이든 정지하지 않아, 리셋을 지워도 테스트가 통과한다.
        var (d, s, k) = Build(maxFailures: 2);
        k.FailFor.Add(101); k.FailFor.Add(103);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102), Lot(103) });   // 실패·성공·실패
        d.Tick();

        Assert.False(d.IsStopped);      // 연속 아님 → 정지하지 않는다
    }

    [Fact]
    public void Resume_request_is_consumed_by_the_next_tick()
    {
        var (d, s, k) = Build(maxFailures: 1);
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.True(d.IsStopped);

        d.Resume();
        Assert.True(d.IsStopped);       // 아직 — 진행 중인 틱과 경쟁하지 않으려고 미룬다

        k.FailFor.Clear();
        s.NextClaim.Add(Lot(102));
        d.Tick();                        // 여기서 소비된다
        Assert.False(d.IsStopped);
        // 101 은 반납되어 대기열로 돌아왔으므로 102 와 함께 다시 나간다.
        Assert.Equal(new[] { 101, 102 }, k.Printed);
    }

    [Fact]
    public void Resume_resets_the_failure_counter()
    {
        // 리셋이 없으면 Resume 후 단 한 번의 실패로 다시 멈춘다.
        var (d, s, k) = Build(maxFailures: 2);
        k.FailFor.Add(101); k.FailFor.Add(102);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102) });
        d.Tick();
        Assert.True(d.IsStopped);

        d.Resume();
        k.FailFor.Clear();
        k.FailFor.Add(101);              // 재개 후 1회만 실패
        d.Tick();
        Assert.False(d.IsStopped);       // 카운터가 리셋됐다면 1회로는 안 멈춘다
    }

    [Fact]
    public void Stop_does_not_clear_the_failure_halt()
    {
        // Stop() 은 세션 종료, IsStopped 는 실패로 인한 자동 정지 — 서로 다른 개념이다.
        var (d, s, k) = Build(maxFailures: 1);
        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.True(d.IsStopped);

        d.Stop();
        Assert.True(d.IsStopped);
    }

    [Fact]
    public void Stopping_abandons_the_rest_of_the_batch()
    {
        var (d, s, k) = Build(maxFailures: 1);
        var raised = 0;
        d.OnStopped += () => raised++;
        k.FailFor.Add(101); k.FailFor.Add(102);
        s.NextClaim.AddRange(new[] { Lot(101), Lot(102) });
        d.Tick();

        Assert.Equal(1, raised);
        Assert.Equal(new[] { 101 }, s.Released);   // 102 는 손대지 않았다
    }

    [Fact]
    public void Db_failure_does_not_stop_dispatch()
    {
        var (d, s, k) = Build(maxFailures: 1);
        s.FailClaim = true;
        d.Tick();
        Assert.False(d.IsStopped);      // 일시적 DB 장애는 정지 사유가 아니다

        s.FailClaim = false;
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Equal(new[] { 101 }, k.Printed);
    }

    [Fact]
    public void Stop_clears_line_so_tick_does_nothing()
    {
        var (d, s, k) = Build();
        d.Stop();
        s.NextClaim.Add(Lot(101));
        d.Tick();
        Assert.Empty(k.Printed);
        Assert.Empty(s.Claimed);
    }

    [Fact]
    public void Stopping_raises_event_once()
    {
        var (d, s, k) = Build(maxFailures: 1);
        var raised = 0;
        d.OnStopped += () => raised++;

        k.FailFor.Add(101);
        s.NextClaim.Add(Lot(101));
        d.Tick();
        d.Tick();                       // 이미 정지 — 다시 발생하지 않는다

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Tick_is_not_reentrant()
    {
        // 폴링 주기(1초) < TCP 타임아웃(2초) 이라 틱이 겹칠 수 있다.
        var s = new FakeSource();
        var k = new ReentrantSink();
        var d = new LabelDispatcher(s, k, maxFailures: 3, _ => { });
        d.Start("LINE-INJ-01", "POP-DEV-01");
        k.Dispatcher = d;

        s.NextClaim.Add(Lot(101));
        d.Tick();

        Assert.Equal(1, k.PrintCalls);          // 중첩 호출이 한 번 더 뽑지 않았다
        Assert.Single(s.Claimed);               // 중첩 틱은 클레임도 하지 않았다
    }

    sealed class ReentrantSink : ILabelSink
    {
        public LabelDispatcher? Dispatcher;
        public int PrintCalls;
        public void Print(InjLotDto lot)
        {
            PrintCalls++;
            Dispatcher?.Tick();                 // 출력 도중 다음 틱이 들어온 상황
        }
    }
```

- [ ] **Step 2: 테스트 실패 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" --filter "FullyQualifiedName~LabelDispatcherTests" -v q
```

기대: FAIL — `error CS1061: 'LabelDispatcher' does not contain a definition for 'IsStopped'`

- [ ] **Step 3: 정지·재개·재진입 방지 구현**

`LabelDispatcher.cs` 전체를 아래로 교체:

```csharp
using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>
/// 사출 라벨 자동 발행 루프. 타이머는 갖지 않는다 — 호출자가 Tick() 을 주기적으로
/// 부르고, 테스트는 시간을 기다리지 않고 직접 부른다.
///
/// 워터마크는 Start() 시점에 한 번만 잡고 전진시키지 않는다. 전진시키면
/// 출력 실패로 반납한 LOT 이 LotID > watermark 조건에서 영구 제외되어
/// 재시도가 불가능해진다. 재시도 판정은 DB 쪽 PrintedCount=0 AND PrintClaimTS IS NULL 이 담당.
/// </summary>
internal sealed class LabelDispatcher
{
    private readonly IInjLotClaimStore _source;
    private readonly ILabelSink         _sink;
    private readonly int                _maxFailures;
    private readonly Action<string>     _log;

    private string? _lineId;
    private string  _stationId = string.Empty;
    private int     _watermark;
    private int     _consecutiveFailures;
    private int     _running;            // 재진입 게이트 (0=유휴, 1=실행중)
    private int     _resumeRequested;    // Resume 요청 (0=없음, 1=대기)

    // 스레드풀 타이머가 쓰고 UI 스레드가 읽는다.
    private volatile bool _isStopped;

    /// <summary>연속 실패로 자동 발행이 멈춘 상태. Resume() 요청으로만 해제된다.</summary>
    public bool IsStopped { get => _isStopped; private set => _isStopped = value; }

    /// <summary>자동 발행이 멈출 때 1회 발생. 배선 코드가 작업자에게 토스트로 알린다.</summary>
    public event Action? OnStopped;

    public LabelDispatcher(IInjLotClaimStore source, ILabelSink sink,
                           int maxFailures, Action<string> log)
    {
        _source      = source;
        _sink        = sink;
        _maxFailures = maxFailures;
        _log         = log;
    }

    public void Start(string lineId, string stationId)
    {
        _lineId              = lineId;
        _stationId           = stationId;
        _consecutiveFailures = 0;
        IsStopped            = false;
        try { _watermark = _source.GetMaxLotId(lineId); }
        catch (Exception ex)
        {
            // 워터마크를 못 잡으면 과거분까지 쏟아질 수 있으므로 시작하지 않는다.
            _log($"watermark init failed: {ex.Message}");
            _lineId = null;
        }
    }

    /// <summary>세션 종료. 실패 정지(IsStopped)와는 다른 개념이라 건드리지 않는다 — Start() 가 리셋한다.</summary>
    public void Stop() => _lineId = null;

    /// <summary>
    /// 작업자가 프린터를 고쳤다는 신호 (수동 재출력 성공) — 자동 발행 재개 요청.
    /// 여기서 바로 IsStopped 를 내리지 않는다: 진행 중인 틱이 실패 경로를 끝내며
    /// 다시 세우면 요청이 조용히 사라진다. 다음 틱이 이 플래그를 소비한다(≤1초).
    /// </summary>
    public void Resume() => Interlocked.Exchange(ref _resumeRequested, 1);

    public void Tick()
    {
        if (_lineId is null) return;

        // 폴링 주기(1초)보다 TCP 연결 타임아웃(2초)이 길어 틱이 겹칠 수 있다.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            // Resume 요청은 틱 안에서 소비한다 — 바깥에서 IsStopped 를 내리면
            // 진행 중이던 틱의 실패 경로가 그 위에 다시 써버린다.
            if (Interlocked.Exchange(ref _resumeRequested, 0) == 1)
            {
                _consecutiveFailures = 0;
                IsStopped            = false;
            }
            if (IsStopped) return;

            // 세션 값을 틱 시작 시점에 고정한다. 로그아웃→재로그인이 진행 중인 틱과
            // 겹치면(프린터 타임아웃 2초) 아래 반납이 새 세션의 StationId 로 나가
            // ReleasePrintClaim 의 소유권 검증이 무력화된다.
            var lineId    = _lineId;
            var stationId = _stationId;
            var watermark = _watermark;
            if (lineId is null) return;

            List<InjLotDto> batch;
            try { batch = _source.ClaimForPrint(lineId, watermark, stationId); }
            catch (Exception ex)
            {
                // DB 일시 장애는 정지 사유가 아니다 — 다음 틱에 재시도.
                _log($"claim failed: {ex.Message}");
                return;
            }

            foreach (var lot in batch)
            {
                try { _sink.Print(lot); }
                catch (Exception ex)
                {
                    // 반납을 먼저 — _log 가 던지면 반납이 유실되고 배치 나머지가 중단된다.
                    try { _source.ReleasePrintClaim(lot.LotId, stationId); }
                    catch (Exception rel) { _log($"claim release failed ({lot.LotCode}): {rel.Message}"); }
                    _log($"label print failed ({lot.LotCode}): {ex.Message}");

                    if (++_consecutiveFailures >= _maxFailures)
                    {
                        IsStopped = true;
                        _log($"auto dispatch stopped after {_consecutiveFailures} consecutive failures");
                        OnStopped?.Invoke();
                        return;
                    }
                    continue;
                }

                // 라벨은 이미 나왔다 — 반납하면 다음 틱에 두 장째가 나온다. 카운트가 0 으로
                // 남으므로 스테일 회수로만 재시도된다. DB 장애를 프린터 실패로 세지도 않는다.
                try { _source.IncrementPrintedCount(lot.LotId); }
                catch (Exception ex) { _log($"printed but count not incremented ({lot.LotCode}): {ex.Message}"); }
                _consecutiveFailures = 0;
            }
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test "src/03_Pop/AMES.Pop.Tests/AMES.Pop.Tests.csproj" -v q
```

기대: `Passed! - Failed: 0, Passed: 18` (스모크 2 + 디스패처 16)

- [ ] **Step 5: 커밋**

```bash
git add src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs src/03_Pop/AMES.Pop.Tests/LabelDispatcherTests.cs
git commit -m "feat(pop): stop dispatch after repeated printer failures, guard re-entry"
```

---

## Task 7: 어댑터 + 설정 + DI 배선

**Files:**
- Create: `src/03_Pop/AMES.Pop/Services/LabelDispatchAdapters.cs`
- Modify: `src/03_Pop/AMES.Pop/Common/AppConfig.cs:29` (프린터 속성 뒤), `:52` (로드 뒤)
- Modify: `src/03_Pop/AMES.Pop/appsettings.json`
- Modify: `src/03_Pop/AMES.Pop/Forms/PopBlazorForm.cs`

- [ ] **Step 1: 어댑터 작성**

`src/03_Pop/AMES.Pop/Services/LabelDispatchAdapters.cs`:

```csharp
using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>IInjLotClaimStore → PopServices.InjLots 위임.</summary>
internal sealed class RepoInjLotClaimStore : IInjLotClaimStore
{
    public int GetMaxLotId(string lineId) => PopServices.InjLots.GetMaxLotId(lineId);

    public List<InjLotDto> ClaimForPrint(string lineId, int afterLotId, string stationId)
        => PopServices.InjLots.ClaimForPrint(lineId, afterLotId, stationId);

    public void ReleasePrintClaim(int lotId, string stationId)
        => PopServices.InjLots.ReleasePrintClaim(lotId, stationId);

    public void IncrementPrintedCount(int lotId) => PopServices.InjLots.IncrementPrintedCount(lotId);
}

/// <summary>ILabelSink → 기존 LabelPrinter 위임 (ZPL 조립·출력 공용 경로).</summary>
internal sealed class ZplLabelSink : ILabelSink
{
    public void Print(InjLotDto lot) => LabelPrinter.Print(lot, AppConfig.Current.LineId);
}
```

- [ ] **Step 2: 설정 2개 추가**

`AppConfig.cs`에서 `public string PrinterOutputDir { get; }` 다음 줄에 삽입:

```csharp
    /// <summary>라벨 자동 발행 폴링 주기(ms). 0 이하면 자동 발행 비활성.</summary>
    public int PrinterPollMs { get; }

    /// <summary>연속 출력 실패가 이 횟수에 도달하면 자동 발행을 멈춘다.</summary>
    public int PrinterMaxFailures { get; }
```

`PrinterOutputDir = root[...]` 대입문 다음 줄에 삽입:

```csharp
        PrinterPollMs      = int.TryParse(root["PopTerminal:Printer:PollMs"], out var pms) ? pms : 1000;
        PrinterMaxFailures = int.TryParse(root["PopTerminal:Printer:MaxFailures"], out var pmf) ? pmf : 3;
```

`src/03_Pop/AMES.Pop/appsettings.json`의 `"Printer"` 블록을 교체:

```json
    "Printer": {
      "Mode": "File",
      "Host": "127.0.0.1",
      "Port": 9100,
      "OutputDir": "labels",
      "PollMs": 1000,
      "MaxFailures": 3
    }
```

- [ ] **Step 3: DI 등록 + 타이머 배선**

`PopBlazorForm.cs`의 `services.AddSingleton<ConfirmService>();` 다음 줄에 삽입:

```csharp
        services.AddSingleton<LabelDispatcher>(_ => new LabelDispatcher(
            new RepoInjLotClaimStore(), new ZplLabelSink(),
            AppConfig.Current.PrinterMaxFailures,
            msg => System.Diagnostics.Debug.WriteLine($"[LabelDispatcher] {msg}")));
```

같은 파일에서 `Services  = services.BuildServiceProvider(),` 를 아래로 바꾸고, 생성된 provider를 필드에 보관한다:

```csharp
        var provider = services.BuildServiceProvider();

        _webView = new BlazorWebView
        {
            Dock      = DockStyle.Fill,
            HostPage  = "wwwroot/index.html",
            StartPath = "/login",
            Services  = provider,
        };
```

그리고 `Controls.Add(_webView);` 다음에 삽입:

```csharp
        WireLabelDispatcher(provider);
```

클래스 안(`private readonly BlazorWebView _webView;` 아래)에 필드와 메서드를 추가한다:

```csharp
    private System.Threading.Timer? _labelTimer;

    // 라벨 발행은 화면 수명과 무관해야 한다 — 로그인 동안 계속, 어느 화면이든.
    // WinForms 타이머를 쓰면 안 된다: Tick() 이 DB + TCP 를 동기로 타는데
    // 프린터 연결 타임아웃이 2초라 UI 스레드가 그만큼 얼어붙는다.
    private void WireLabelDispatcher(IServiceProvider provider)
    {
        var pollMs = AppConfig.Current.PrinterPollMs;
        if (pollMs <= 0 || AppConfig.Current.ModuleCode != "INJ") return;

        var state      = provider.GetRequiredService<AppState>();
        var toasts     = provider.GetRequiredService<ToastService>();
        var dispatcher = provider.GetRequiredService<LabelDispatcher>();

        dispatcher.OnStopped += () => toasts.Bad(PopLang.T("LabelAutoDispatchStopped"));

        // 백그라운드 타이머 콜백에서 예외가 새어 나가면 프로세스가 죽는다.
        _labelTimer = new System.Threading.Timer(_ =>
        {
            try { dispatcher.Tick(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LabelDispatcher] tick failed: {ex}"); }
        }, null, Timeout.Infinite, Timeout.Infinite);

        state.OnChange += () =>
        {
            if (state.Session is { } s)
            {
                dispatcher.Start(s.LineId, s.TerminalId);
                _labelTimer.Change(pollMs, pollMs);
            }
            else
            {
                _labelTimer.Change(Timeout.Infinite, Timeout.Infinite);
                dispatcher.Stop();
            }
        };
    }
```

파일 상단에 `using Microsoft.Extensions.DependencyInjection;`과 `using AMES.Pop.Common;`이 없으면 추가한다 (`AddSingleton`·`GetRequiredService`·`AppConfig`·`PopLang`용).

`ToastHost`가 `InvokeAsync`로 마샬링하므로 백그라운드 스레드에서 토스트를 띄워도 안전하다.

- [ ] **Step 3b: 토스트 문구 리소스 추가**

`src/03_Pop/AMES.Pop/Resources/PopStrings.resx`에서 `LabelPrintFailed` 항목을 찾아 그 다음 줄에 삽입:

```xml
  <data name="LabelAutoDispatchStopped" xml:space="preserve"><value>프린터 오류로 라벨 자동 발행이 중지되었습니다. 재출력 버튼을 누르면 재개됩니다.</value></data>
```

`src/03_Pop/AMES.Pop/Resources/PopStrings.en.resx`의 같은 위치에 삽입:

```xml
  <data name="LabelAutoDispatchStopped" xml:space="preserve"><value>Auto label dispatch stopped after printer errors. Press Reprint to resume.</value></data>
```

- [ ] **Step 4: 빌드 확인**

```bash
dotnet build "src/03_Pop/AMES.Pop/AMES.Pop.csproj" -v q
```

기대: `Build succeeded.` / `0 Error(s)`

- [ ] **Step 5: 커밋**

```bash
git add src/03_Pop/AMES.Pop/Services/LabelDispatchAdapters.cs src/03_Pop/AMES.Pop/Common/AppConfig.cs src/03_Pop/AMES.Pop/appsettings.json src/03_Pop/AMES.Pop/Forms/PopBlazorForm.cs src/03_Pop/AMES.Pop/Resources/PopStrings.resx src/03_Pop/AMES.Pop/Resources/PopStrings.en.resx
git commit -m "feat(pop): wire label dispatcher to session lifecycle"
```

---

## Task 8: 발행 경로 일원화 (수동 입력 인라인 발행 제거)

**Files:**
- Modify: `src/03_Pop/AMES.Pop/Pages/InjPopups/ManualEntryPopup.razor:69-84`
- Modify: `src/03_Pop/AMES.Pop/Pages/InjMain.razor:381-391`

수동 발행분도 `LotID > watermark AND PrintedCount = 0` 조건에 걸리므로, 인라인 발행을 두면 디스패처가 **같은 라벨을 한 번 더** 뽑는다.

- [ ] **Step 1: 인라인 발행 제거**

`ManualEntryPopup.razor`에서 아래 블록을 찾는다:

```csharp
            var printed = 0;
            string? printError = null;
            foreach (var lot in lots)
            {
                try
                {
                    LabelPrinter.Print(lot, Session.LineId);
                    PopServices.InjLots.IncrementPrintedCount(lot.LotId);
                    printed++;
                }
                catch (Exception ex) { printError ??= ex.Message; }
            }

            Toasts.Ok(T("ManualLotsCreated", qty, printed));
            if (printError is not null)
                Toasts.Bad($"{T("LabelPrintFailed")} ({printed}/{lots.Count}): {printError}");
```

아래로 교체한다:

```csharp
            // 라벨은 LabelDispatcher 가 발행한다 — 여기서 뽑으면 디스패처와 이중 발행된다.
            Toasts.Ok(T("ManualLotsCreated", lots.Count));
```

- [ ] **Step 1b: 토스트 문구를 사실에 맞게 고친다**

현재 문구는 `LOT {0}건 발행 · 라벨 {1}장 출력`인데, 이제 이 시점에 라벨은 **아직 안 나왔다** — 디스패처가 최대 1초 뒤에 뽑는다. 그대로 두면 토스트가 거짓말을 한다. 자리표시자도 2개에서 1개로 준다.

`src/03_Pop/AMES.Pop/Resources/PopStrings.resx`:
```xml
  <data name="ManualLotsCreated" xml:space="preserve"><value>LOT {0}건 발행 — 라벨은 곧 출력됩니다. 스캔하면 실적 확정</value></data>
```

`src/03_Pop/AMES.Pop/Resources/PopStrings.en.resx`:
```xml
  <data name="ManualLotsCreated" xml:space="preserve"><value>{0} lots issued — labels print shortly. Scan to confirm</value></data>
```

`LabelPrintFailed` 키는 이 파일에서 유일한 사용처가 사라진다. 다른 곳에서 쓰이는지 확인하고(`grep -rn "LabelPrintFailed" src/`), 안 쓰이면 양쪽 resx에서 제거한다.

- [ ] **Step 2: 재출력 성공 시 자동 발행 재개**

`InjMain.razor`의 `@inject ToastService Toasts` 다음 줄에 추가:

```razor
@inject AMES.Pop.Services.LabelDispatcher Dispatcher
```

같은 파일의 `Reprint` 메서드에서 `Toasts.Ok(...)` 다음 줄에 추가:

```csharp
            Dispatcher.Resume();   // 프린터가 살아났다는 신호 — 자동 발행 재개
```

- [ ] **Step 3: 빌드 확인**

```bash
dotnet build "src/03_Pop/AMES.Pop/AMES.Pop.csproj" -v q
```

기대: `Build succeeded.` / `0 Error(s)`

> 화면에서 실제로 확인할 것: 수동 발행 후 토스트가 "라벨 N장 출력"이라고 말하지 않는지. 라벨은 디스패처가 최대 1초 뒤에 뽑으므로 그 문구는 사실이 아니다.

**`ModuleCode != "INJ"` 또는 `PollMs <= 0` 인 터미널 주의.** 그 경우 `WireLabelDispatcher`가 조기 return 해서 디스패처는 DI 에 등록만 되고 한 번도 `Start()`/`Tick()` 되지 않는다. `InjMain` 의 `Dispatcher.Resume()` 는 그런 터미널에서 아무 일도 하지 않는 무해한 호출이 된다 — `InjMain` 자체가 INJ 전용 화면이라 실제로는 도달하지 않지만, 예외를 던지지 않는다는 점만 확인하면 된다.

- [ ] **Step 4: 커밋**

```bash
git add src/03_Pop/AMES.Pop/Pages/InjPopups/ManualEntryPopup.razor src/03_Pop/AMES.Pop/Pages/InjMain.razor
git commit -m "refactor(pop): route all label printing through the dispatcher"
```

---

## Task 9: InjAgent 발행 경로 제거

**Files:**
- Delete: `src/07_Etc/AMES.InjAgent/Core/ZplLabelPrinter.cs`
- Modify: `src/07_Etc/AMES.InjAgent/Core/Interfaces.cs:38,46-50`
- Modify: `src/07_Etc/AMES.InjAgent/Core/MachinePoller.cs:21,34-43,149-155`
- Modify: `src/07_Etc/AMES.InjAgent/Core/DbInjAgentStore.cs:24`
- Modify: `src/07_Etc/AMES.InjAgent/Core/AgentConfig.cs:22,49-55`
- Modify: `src/07_Etc/AMES.InjAgent/Program.cs:39,41-45`
- Modify: `src/07_Etc/AMES.InjAgent/appsettings.json`
- Modify: `src/07_Etc/AMES.InjAgent.Tests/MachinePollerTests.cs`, `PollerRunnerTests.cs`

- [ ] **Step 1: 테스트에서 프린터 기대 제거**

`MachinePollerTests.cs`에서 `FakePrinter` 클래스 전체를 삭제한다:

```csharp
    sealed class FakePrinter : ILabelPrinter
    {
        public List<string> Printed = new();
        public void PrintLabel(string lotCode, string itemNo, string? itemName,
                               string? colorCode, string? cavityPos, string lineId)
            => Printed.Add(lotCode);
    }
```

`FakeStore`에서 아래 두 줄을 삭제한다:

```csharp
        public List<int> LabelPrinted = new();
```
```csharp
        public void MarkLabelPrinted(int lotId) => LabelPrinted.Add(lotId);
```

`Build()` 헬퍼를 교체한다:

```csharp
    static (MachinePoller Poller, FakeMachine M, FakeRobot R, FakeStore S) Build()
    {
        var m = new FakeMachine();
        var r = new FakeRobot();
        var s = new FakeStore();
        var cfg = new MachineConfig { EquipId = "INJ-650-01", LineId = "LINE-INJ-01", ModbusIp = "x", FenetIp = "y" };
        var poller = new MachinePoller(cfg, m, r, s, _ => { });
        return (poller, m, r, s);
    }
```

`Build()`를 4-튜플로 받던 호출부를 모두 3-튜플로 고친다. 예:
- `var (poller, m, _, s, _) = Build();` → `var (poller, m, _, s) = Build();`
- `var (poller, m, r, s, p) = Build();` → `var (poller, m, r, s) = Build();`

`Shot_count_change_creates_lot_per_cavity_and_prints` 테스트에서 아래 두 줄을 삭제하고:

```csharp
        Assert.Equal(2, p.Printed.Count);
        Assert.Equal(new[] { 100, 101 }, s.LabelPrinted);   // 발행 성공 → PrintedCount 반영
```

테스트 이름을 `Shot_count_change_creates_lot_per_cavity`로 바꾼다.

`PollerRunnerTests.cs`에서 `NopPrinter` 클래스와 `NopStore.MarkLabelPrinted` 줄을 삭제하고, 생성자 호출을 고친다:

```csharp
        var poller = new MachinePoller(cfg, m, r, new NopStore(), _ => { });
```

- [ ] **Step 2: 테스트가 컴파일 실패하는지 확인**

```bash
dotnet test "src/07_Etc/AMES.InjAgent.Tests/AMES.InjAgent.Tests.csproj" -v q
```

기대: FAIL — `error CS1729: 'MachinePoller' does not contain a constructor that takes 5 arguments`

- [ ] **Step 3: 프로덕션 코드에서 발행 제거**

`ZplLabelPrinter.cs` 파일을 삭제한다:

```bash
git rm src/07_Etc/AMES.InjAgent/Core/ZplLabelPrinter.cs
```

`Interfaces.cs`에서 `IInjAgentStore`의 아래 줄을 삭제:

```csharp
    void MarkLabelPrinted(int lotId);
```

같은 파일 맨 끝의 `ILabelPrinter` 인터페이스 전체를 삭제:

```csharp
/// <summary>라벨 발행 (AMES.Devices 어댑터).</summary>
public interface ILabelPrinter
{
    void PrintLabel(string lotCode, string itemNo, string? itemName, string? colorCode, string? cavityPos, string lineId);
}
```

`DbInjAgentStore.cs`에서 아래 줄을 삭제:

```csharp
    public void MarkLabelPrinted(int lotId) => _lots.IncrementPrintedCount(lotId);
```

`MachinePoller.cs`에서 `private readonly ILabelPrinter _printer;` 줄을 삭제하고, 생성자를 교체:

```csharp
    public MachinePoller(MachineConfig cfg, IInjectionMachine machine, IRobotLink robot,
                         IInjAgentStore store, Action<string> log)
    {
        _cfg = cfg;
        _machine = machine;
        _robot = robot;
        _store = store;
        _log = msg => log($"[{cfg.EquipId}] {msg}");
    }
```

같은 파일에서 아래 블록을 찾아:

```csharp
                _log($"{m.CavityPos} LOT created: {lotCode}");
                try
                {
                    _printer.PrintLabel(lotCode, m.ItemNo, m.ItemName, m.ColorCode, m.CavityPos, _cfg.LineId);
                    try { _store.MarkLabelPrinted(lotId); }
                    catch (Exception ex) { _log($"Printed-count update failed ({lotCode}): {ex.Message}"); }
                }
                catch (Exception ex) { _log($"Label print failed ({lotCode}): {ex.Message}"); }
```

아래로 교체한다 (라벨은 Pop 이 발행):

```csharp
                _log($"{m.CavityPos} LOT created: {lotCode}");
```

`AgentConfig.cs`에서 `public ZplPrinterOptions Printer { get; }` 줄과 아래 블록을 삭제:

```csharp
        Printer = new ZplPrinterOptions
        {
            Mode      = root["Agent:Printer:Mode"]      ?? "File",
            Host      = root["Agent:Printer:Host"]      ?? "127.0.0.1",
            Port      = int.TryParse(root["Agent:Printer:Port"], out var pp) ? pp : 9100,
            OutputDir = root["Agent:Printer:OutputDir"] ?? "labels",
        };
```

파일 첫 줄의 `using AMES.Devices;`도 삭제한다 (더 이상 쓰이지 않음).

`Program.cs`에서 `var printer = new ZplLabelPrinter(cfg.Printer);` 줄을 삭제하고, 폴러 생성을 교체:

```csharp
        var pollers = cfg.Machines.Select(m => new MachinePoller(
            m,
            new ModbusMachineClient(m.ModbusIp, m.ModbusPort),
            new FEnetClient(m.FenetIp, m.FenetPort),
            store, MainForm.EnqueueLog)).ToList();
```

`appsettings.json`에서 `"Printer"` 블록 전체를 삭제한다. `"Machines"` 배열 닫는 `]` 뒤의 쉼표도 함께 제거해야 JSON이 유효하다:

```json
{
  "ConnectionStrings": {
    "AMES": "Server=localhost,1433;Database=AMES_DEV;User Id=sa;Password=AmesDev!2026Sa;TrustServerCertificate=True;Encrypt=True;"
  },
  "Agent": {
    "PollingMs": 100,
    "Machines": [
      {
        "EquipId": "INJ-650-01",
        "LineId": "LINE-INJ-01",
        "ModbusIp": "127.0.1.1",
        "ModbusPort": 502,
        "FenetIp": "127.0.2.1",
        "FenetPort": 2004
      }
    ]
  }
}
```

- [ ] **Step 4: 테스트 통과 확인**

```bash
dotnet test "src/07_Etc/AMES.InjAgent.Tests/AMES.InjAgent.Tests.csproj" -v q
```

기대: `Passed! - Failed: 0` (총 개수는 53에서 줄어들 수 있다 — 삭제한 단언만큼)

> AMES.InjAgent.exe가 실행 중이면 파일 잠금으로 빌드가 실패한다. 종료 후 재시도한다.

- [ ] **Step 5: AMES.Devices 참조 정리 확인**

InjAgent가 여전히 `AMES.Devices`를 쓰는지 확인한다:

```bash
grep -rn "AMES.Devices\|ZplPrinter\|ZplLabel" src/07_Etc/AMES.InjAgent/ || echo "(참조 없음 — csproj 에서 제거 가능)"
```

`(참조 없음)`이면 `src/07_Etc/AMES.InjAgent/AMES.InjAgent.csproj`에서 아래 줄을 삭제한다:

```xml
    <ProjectReference Include="..\..\01_Shared\AMES.Devices\AMES.Devices.csproj" />
```

그리고 다시 빌드해 확인한다:

```bash
dotnet build "src/07_Etc/AMES.InjAgent/AMES.InjAgent.csproj" -v q
```

기대: `Build succeeded.`

- [ ] **Step 6: 커밋**

```bash
git add -A src/07_Etc
git commit -m "refactor(agent): remove label printing, agent now collects only"
```

---

## Task 10: 통합 검증

**Files:** 없음 (실행 검증만)

- [ ] **Step 1: 솔루션 전체 빌드**

```bash
dotnet build src/AMES.sln -v q
```

기대: `Build succeeded.` / `0 Error(s)`

> 실행 중인 AMES.Pop / AMES.InjAgent / AMES.Web이 있으면 DLL 잠금(MSB3027)이 난다. 컴파일 오류와 구분하려면:
> ```bash
> dotnet build src/AMES.sln -v q 2>&1 | grep "error CS" || echo "(컴파일 오류 없음)"
> ```

- [ ] **Step 2: 전체 테스트**

```bash
dotnet test src/AMES.sln -v q
```

기대: 두 테스트 프로젝트 모두 `Failed: 0`

- [ ] **Step 3: DB 원자적 클레임 수동 검증**

단위 테스트로는 검증할 수 없는 부분이다. 먼저 테스트용 미출력 LOT을 확인한다:

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "SELECT TOP 5 e.LotID, e.PrintedCount, e.PrintClaimTS FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID=e.LotID WHERE l.LineID='LINE-INJ-01' AND e.PrintedCount=0 ORDER BY e.LotID DESC;"
```

미출력 LOT이 없으면 InjAgent 시뮬레이터를 돌려 몇 개 만든 뒤 진행한다.

같은 클레임을 **연속 두 번** 실행해 두 번째가 0건인지 확인한다 (`<LotID>`는 위에서 본 값보다 1 작은 수):

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "DECLARE @c TABLE(LotID INT); UPDATE TOP (20) e SET e.PrintClaimTS=SYSDATETIME(), e.PrintClaimStation='T1' OUTPUT INSERTED.LotID INTO @c FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID=e.LotID WHERE l.LineID='LINE-INJ-01' AND e.LotID > <LotID> AND e.PrintedCount=0 AND (e.PrintClaimTS IS NULL OR e.PrintClaimTS < DATEADD(second,-30,SYSDATETIME())); SELECT COUNT(*) AS FirstClaim FROM @c; DELETE @c; UPDATE TOP (20) e SET e.PrintClaimTS=SYSDATETIME(), e.PrintClaimStation='T2' OUTPUT INSERTED.LotID INTO @c FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID=e.LotID WHERE l.LineID='LINE-INJ-01' AND e.LotID > <LotID> AND e.PrintedCount=0 AND (e.PrintClaimTS IS NULL OR e.PrintClaimTS < DATEADD(second,-30,SYSDATETIME())); SELECT COUNT(*) AS SecondClaim FROM @c;"
```

기대: `FirstClaim`은 1 이상, `SecondClaim`은 **0**. 두 번째가 0이 아니면 스테일 조건이나 `PrintedCount` 필터가 잘못된 것이다.

검증 후 선점을 원복한다:

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "UPDATE dbo.PR_InjLot SET PrintClaimTS=NULL, PrintClaimStation=NULL WHERE PrintClaimStation IN ('T1','T2','SMOKE');"
```

- [ ] **Step 4: Pop 실기 확인**

`labels/` 출력 폴더를 비우고 Pop을 띄운다:

```bash
rm -rf src/03_Pop/AMES.Pop/bin/Debug/net10.0-windows/labels
dotnet run --project src/03_Pop/AMES.Pop/AMES.Pop.csproj
```

로그인 후 수동 실적 입력으로 LOT 3개를 발행하고, 약 2초 뒤 파일이 생겼는지 확인한다:

```bash
ls src/03_Pop/AMES.Pop/bin/Debug/net10.0-windows/labels/
```

기대: `.zpl` 파일 3개.

**파일 개수만으로는 이중 발행을 못 잡는다.** `ZplPrinter`의 File 모드는 `{LotCode}.zpl`로 쓰고 **같은 이름이면 덮어쓴다**(재출력 시 최신본만 남기는 의도된 동작). 같은 LOT이 두 번 발행돼도 파일은 그대로 3개다. 따라서 아래를 함께 확인해야 증거가 된다:

- 출력 호출 횟수를 독립적으로 센다 (헤드리스 검증이면 sink 호출 카운터)
- DB에서 그 3건의 `PrintedCount`가 **정확히 1**인지 확인 — 2면 두 번 나간 것이다
- 출력 후 `ClaimForPrint`를 한 번 더 불러 **0건**인지 확인 (이미 출력된 건 재선점 안 됨)

DB에서도 확인한다:

```bash
"C:/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/SQLCMD.EXE" -S localhost,1433 -U sa -P 'AmesDev!2026Sa' -d AMES_DEV -f 65001 -Q "SELECT TOP 5 LotID, PrintedCount, PrintClaimStation FROM dbo.PR_InjLot ORDER BY LotID DESC;"
```

기대: 방금 만든 3건의 `PrintedCount = 1`, `PrintClaimStation = POP-DEV-01`

- [ ] **Step 5: CLAUDE.md 갱신**

`CLAUDE.md`의 `07_Etc/AMES.InjAgent` 설명 줄을 찾는다:

```
07_Etc/AMES.InjAgent       ← WinForms 상주 에이전트, 사출기 Modbus/취출로봇 FEnet 수집 (net10.0-windows)
```

라벨 발행 주체가 바뀐 것을 반영한다:

```
07_Etc/AMES.InjAgent       ← WinForms 상주 에이전트, 사출기 Modbus/취출로봇 FEnet 수집 (net10.0-windows)
                              ※ 라벨 발행은 하지 않는다 — AMES.Pop 의 LabelDispatcher 담당
```

- [ ] **Step 6: 커밋**

```bash
git add CLAUDE.md
git commit -m "docs: note that label dispatch moved from agent to Pop"
```

---

## 완료 기준

- [ ] `dotnet test src/AMES.sln` 전부 통과
- [ ] 수동 실적 3건 → `.zpl` 파일 정확히 3개 (이중 발행 없음)
- [ ] 동일 클레임 2회 연속 실행 시 두 번째가 0건
- [ ] InjAgent 소스에 `ZplPrinter`/`ILabelPrinter` 참조 없음
- [ ] 프린터를 끈 상태(`Mode: "Tcp"`, 없는 Host)에서 Pop이 3회 실패 후 멈추고, 재출력 버튼을 누르면 재개
