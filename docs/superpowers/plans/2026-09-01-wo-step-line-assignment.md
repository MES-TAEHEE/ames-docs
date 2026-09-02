# WO 공정 단계별 라인 배정·실적 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PP_WorkOrderRouting` 단계 행을 라인·상태·완료수량의 정본으로 만들고, Release 시 BOP 라인을 기본값으로 단계별 라인을 배정하며, Pop 터미널이 단계 행 기준으로 WO 를 받고 실적을 쌓게 한다.

**Architecture:** 헤더(`PP_WorkOrder`)는 품목·수량·납기·전체 상태만 가진다. 단계 행이 `LineID`·`Status`·`CompletedQty` 를 가지며, 실적 반영은 `WorkOrderRepository.BumpStepCompleted` 한 곳으로 모은다. 헤더 `CompletedQty`·`Closed` 는 "라인이 있는 마지막 단계" 가 동기화한다. 헤더 `LineID` 는 쓰기를 중단하고 컬럼만 남긴다.

**Tech Stack:** .NET 10, ADO.NET(`Microsoft.Data.SqlClient`), Blazor Server + Radzen(Web), Blazor Hybrid(Pop), xunit + `Xunit.SkippableFact`(AMES_DEV 통합 테스트), SQL Server 2022.

**Spec:** `docs/superpowers/specs/2026-09-01-wo-step-line-assignment-design.md`

---

## 작업 전 확인 (필수)

**커밋 정책:** 이 프로젝트는 "기능 본체 1커밋" 이 원칙이다. 태스크마다 커밋하지 않는다. 각 태스크 끝에서 빌드·테스트가 녹색인지만 확인하고, **Task 12 에서 한 번에 커밋**한다.

**선행 작업 충돌:** 작업 트리에 "RoutingType 필수" 작업분(`WorkOrderRepository.cs`, `PpRepository.cs`, `PlanConfirm.razor`, `WorkOrderManualDialog.razor`, resx, `WorkOrderRepositoryTests.cs` 등)이 미커밋 상태일 수 있다. 이 계획은 같은 파일을 크게 손댄다.

- [ ] `git status --short` 를 실행한다. `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs` 또는 `PpRepository.cs` 가 `M` 으로 나오면 **여기서 멈추고 사용자에게 그 작업을 먼저 커밋해 달라고 요청**한다. 그 변경을 이 계획의 커밋에 섞지 않는다.
- [ ] `src/03_Pop/AMES.Pop/appsettings*.json`, `src/06_Web/AMES.Web/appsettings.Development.json` 의 변경은 로컬 접속 문자열이다. **절대 스테이징하지 않는다.**

**DB:** 통합 테스트 기본 접속은 `192.168.2.137`(LAN). 원격 Docker(`98.95.142.192`)를 쓰려면 PowerShell 에서:

```powershell
$env:AMES_TEST_CONN = "Server=98.95.142.192,1433;Database=AMES_DEV;User Id=ames_app;Password=!Dev2026;TrustServerCertificate=True;Encrypt=True;Connect Timeout=10;"
```

마이그레이션은 테스트가 붙는 DB 에 먼저 적용한다. sqlcmd 는 반드시 ODBC17 전체 경로 + `-f 65001`:

```powershell
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S 98.95.142.192,1433 -U ames_app -P '!Dev2026' -d AMES_DEV -C -f 65001 -b -i dist\migrate_wo_step_line.sql
```

**테스트 전제 마스터(AMES_DEV 시드):** `LINE-INJ-01`·`LINE-INJ-02`(INJ, ACTIVE), `LINE-IMG-01`(IMG), `LINE-PNT-01/02`(PNT), `LINE-QC-01`(INACTIVE), FG 라인 없음. 스테이션 `ST-INJ-01 → LINE-INJ-01`, `ST-IMG-01 → LINE-IMG-01`. 라우팅 템플릿 A = INJ(1)→IMG(2), B = INJ(1)→PNT(2)→QC(3)→FG(4). 품목 `83335-P8000RBQ` 존재(기존 테스트 사용).

---

## 파일 구조

| 파일 | 역할 | 변경 |
|---|---|---|
| `dist/migrate_wo_step_line.sql` | 컬럼·인덱스 추가, Pending 정리, 백필 | 생성 |
| `dist/AMES_Schema.sql` | 정본 스키마에 컬럼·인덱스 반영 | 수정 |
| `CLAUDE.md` | 마이그레이션 안내 한 줄 | 수정 |
| `src/01_Shared/AMES.Contracts/Dto/WorkOrderDto.cs` | 단계 필드 4개 + 의미 규칙 주석 | 수정 |
| `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs` | `PreviewRouting`·`ReleaseWo(steps)`·`ListSteps`·`BumpStepCompleted`·`FindStepId`, 라인 범위 조회 단계 기준, `AcceptWo(routingLineId)`, `AddCompletedQty`·구 `ReleaseWo`·`GenerateWoRouting` 삭제 | 수정 |
| `src/02_Data/AMES.Data/Repositories/PpRepository.cs` | `ReleaseWo` 삭제, `WoLite.RouteLines`, 라인 필터 EXISTS | 수정 |
| `src/02_Data/AMES.Data/Repositories/InjLotRepository.cs` | `ConfirmByLotCode` 단계 선택 + `BumpStepCompleted` | 수정 |
| `src/02_Data/AMES.Data/Repositories/ProductionRepository.cs` | `RecordCycle` 단계 찾기 + `BumpStepCompleted` | 수정 |
| `src/02_Data/AMES.Data/Repositories/LineScheduleRepository.cs` | `ListLineWos` 단계 기준 | 수정 |
| `src/02_Data/AMES.Data/Repositories/OeeRepository.cs` | `ListLines` 단계 기준 | 수정 |
| `src/06_Web/AMES.Web/Components/Pages/Pp/WoReleaseLineDialog.razor` | 단계별 라인 다이얼로그로 재작성 | 수정 |
| `src/06_Web/AMES.Web/Components/Pages/Pp/WorkOrder.razor` | Release 호출, LINE 컬럼, 상세 칩, CSV | 수정 |
| `src/06_Web/AMES.Web/Components/Pages/Pp/WoRelease.razor` | `WorkOrderRepository` 로 Release, LINE 컬럼 | 수정 |
| `src/06_Web/AMES.Web/Resources/SharedResources.resx`, `.en.resx` | 키 6개 | 수정 |
| `src/03_Pop/AMES.Pop/Pages/InjPopups/WoConfirmPopup.razor` | `AcceptWo(RoutingLineId)` | 수정 |
| `src/07_Etc/AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs` | 신규 테스트 9개 | 수정 |
| `src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs`, `ProductionRepositoryTests.cs` | 픽스처에 단계 행 추가 | 수정 |

---

### Task 1: 마이그레이션 스크립트 + 정본 스키마

**Files:**
- Create: `dist/migrate_wo_step_line.sql`
- Modify: `dist/AMES_Schema.sql:1324-1341`
- Modify: `CLAUDE.md` (DB 스키마 영역 문단)

- [ ] **Step 1: 마이그레이션 스크립트 작성**

`dist/migrate_wo_step_line.sql` 를 UTF-8(BOM 없음)로 생성:

```sql
-- ════════════════════════════════════════════════════════════════════════
-- migrate_wo_step_line.sql — WO 공정 단계별 라인 배정·실적 (PP_WorkOrderRouting 정본화)
--   · PP_WorkOrderRouting.CompletedQty 추가, (WoID,StepSeq) 유니크, (LineID,Status) 인덱스
--   · 단계 Status 'Pending' → Released / In Progress / Closed 로 통일
--   · 단계 행 없는 Released·In Progress·Closed WO 백필
--       RoutingType 있음: 템플릿 단계 전부. 헤더 라인 공정 = 헤더 라인, 타 공정 = 첫 활성 라인, 활성 라인 없음 = NULL
--       RoutingType NULL : 헤더 라인 공정 단계 1개
--       헤더 CompletedQty 는 손대지 않는다 (동기화는 다음 실적부터)
--   · 헤더 LineID NULL 인 대상은 백필하지 않고 WoID 를 PRINT
-- 적용 순서: migrate_routing_step.sql 다음.
-- idempotent(멱등). 적용: sqlcmd(ODBC17) -f 65001 -b -i dist/migrate_wo_step_line.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) 컬럼 ────────────────────────────────────────────────────────────
IF COL_LENGTH('dbo.PP_WorkOrderRouting', 'CompletedQty') IS NULL
    ALTER TABLE dbo.PP_WorkOrderRouting
        ADD CompletedQty DECIMAL(14,3) NOT NULL
            CONSTRAINT DF_PP_WorkOrderRouting_CompletedQty DEFAULT (0);
GO

-- ── 2) 인덱스 ──────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_PP_WorkOrderRouting_Wo_Step'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrderRouting'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrderRouting_Wo_Step
        ON dbo.PP_WorkOrderRouting (WoID, StepSeq);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PP_WorkOrderRouting_Line_Status'
                 AND object_id = OBJECT_ID('dbo.PP_WorkOrderRouting'))
    CREATE NONCLUSTERED INDEX IX_PP_WorkOrderRouting_Line_Status
        ON dbo.PP_WorkOrderRouting (LineID, Status)
        INCLUDE (WoID, StepSeq, CompletedQty);
GO

-- ── 3) Pending → 헤더 기준 상태 ────────────────────────────────────────
UPDATE r
SET    r.Status = CASE WHEN w.Status = 'Closed'                                    THEN 'Closed'
                       WHEN w.Status = 'In Progress' AND r.LineID = w.LineID       THEN 'In Progress'
                       ELSE 'Released' END,
       r.ModifiedBy = 'MIGRATE', r.ModifiedTS = SYSDATETIME()
FROM   dbo.PP_WorkOrderRouting r
JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
WHERE  r.Status = 'Pending';
GO

-- ── 4) 백필 ────────────────────────────────────────────────────────────
DECLARE @Wo TABLE (
    WoID int, RoutingType char(1), LineID varchar(20), Status varchar(20),
    CompletedQty decimal(14,3), LineProc varchar(10));

INSERT INTO @Wo
SELECT w.WoID, w.RoutingType, w.LineID, w.Status, ISNULL(w.CompletedQty, 0), wc.ProcessCode
FROM   dbo.PP_WorkOrder w
LEFT JOIN dbo.MD_Line       l  ON l.LineID = w.LineID
LEFT JOIN dbo.MD_WorkCenter wc ON wc.WCID  = l.WCID
WHERE  w.Status IN ('Released', 'In Progress', 'Closed')
  AND  NOT EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID);

DECLARE @skip nvarchar(max) =
    (SELECT STRING_AGG(CAST(WoID AS nvarchar(10)), ',') FROM @Wo WHERE LineID IS NULL OR LineProc IS NULL);
IF @skip IS NOT NULL
    PRINT N'migrate_wo_step_line: 헤더 LineID 없음/공정 불명 — 백필 제외 WoID: ' + @skip;

-- 4a) RoutingType 있음 → 템플릿 단계 전부
INSERT INTO dbo.PP_WorkOrderRouting
       (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
SELECT x.WoID, rs.StepSeq, rs.ProcessCode,
       CASE WHEN rs.ProcessCode = x.LineProc THEN x.LineID
            ELSE (SELECT TOP 1 l.LineID
                  FROM dbo.MD_Line l JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
                  WHERE wc.ProcessCode = rs.ProcessCode
                    AND ISNULL(l.Status, 'ACTIVE') <> 'INACTIVE'
                  ORDER BY l.LineID) END,
       NULL, NULL,
       CASE WHEN x.Status = 'Closed'              THEN 'Closed'
            WHEN rs.ProcessCode = x.LineProc      THEN x.Status
            ELSE 'Released' END,
       CASE WHEN rs.ProcessCode = x.LineProc THEN x.CompletedQty ELSE 0 END,
       'MIGRATE', SYSDATETIME()
FROM   @Wo x
JOIN   dbo.MD_RoutingStep rs ON rs.RoutingType = x.RoutingType AND ISNULL(rs.ActiveFlag, 1) = 1
WHERE  x.LineID IS NOT NULL AND x.LineProc IS NOT NULL AND x.RoutingType IS NOT NULL;

-- 4b) RoutingType NULL → 헤더 라인 공정 단계 1개
INSERT INTO dbo.PP_WorkOrderRouting
       (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
SELECT x.WoID, 1, x.LineProc, x.LineID, NULL, NULL, x.Status, x.CompletedQty, 'MIGRATE', SYSDATETIME()
FROM   @Wo x
WHERE  x.LineID IS NOT NULL AND x.LineProc IS NOT NULL AND x.RoutingType IS NULL;

PRINT N'migrate_wo_step_line: 완료';
GO
```

- [ ] **Step 2: 정본 스키마 반영**

`dist/AMES_Schema.sql` 의 `PP_WorkOrderRouting` CREATE TABLE 에서 `[ActualEnd]` 줄 뒤에 컬럼을 추가하고, `GO` 뒤에 인덱스를 붙인다:

```sql
  [ActualEnd]                 DATETIME2                NULL,
  [CompletedQty]              DECIMAL(14,3)        NOT NULL DEFAULT 0,   -- 단계 완료수량 (정본). 헤더 CompletedQty 는 라인 있는 마지막 단계와 동기화
  [CreatedBy]                 VARCHAR(50)          NOT NULL,
```

```sql
  CONSTRAINT PK_PP_WorkOrderRouting PRIMARY KEY CLUSTERED ([RoutingLineID])
);
GO
CREATE UNIQUE NONCLUSTERED INDEX UX_PP_WorkOrderRouting_Wo_Step
  ON dbo.PP_WorkOrderRouting (WoID, StepSeq);
CREATE NONCLUSTERED INDEX IX_PP_WorkOrderRouting_Line_Status
  ON dbo.PP_WorkOrderRouting (LineID, Status) INCLUDE (WoID, StepSeq, CompletedQty);
GO
```

- [ ] **Step 3: CLAUDE.md 안내 추가**

`## DB 스키마 영역` 의 마지막 문단(LotNo 채번 기반 문장) 바로 뒤에 추가:

```markdown
WO 공정 단계(`PP_WorkOrderRouting.CompletedQty` · 인덱스 · 백필)는 `dist/migrate_wo_step_line.sql` — `migrate_routing_step.sql` 다음에 적용. 이 뒤로 라인 배정·상태·완료수량의 정본은 단계 행이며 `PP_WorkOrder.LineID` 는 쓰지 않는다(컬럼만 잔존). Pop 은 단계 `LineID` 로 WO 를 받고, 실적은 `WorkOrderRepository.BumpStepCompleted` 한 곳으로만 반영된다.
```

- [ ] **Step 4: 대상 DB 에 적용**

```powershell
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S 98.95.142.192,1433 -U ames_app -P '!Dev2026' -d AMES_DEV -C -f 65001 -b -i dist\migrate_wo_step_line.sql
```

Expected: `migrate_wo_step_line: 완료`. 백필 제외 PRINT 가 나오면 WoID 를 기록해 둔다.

- [ ] **Step 5: 적용 검증**

```powershell
& "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" -S 98.95.142.192,1433 -U ames_app -P '!Dev2026' -d AMES_DEV -C -f 65001 -W -Q "SELECT COL_LENGTH('dbo.PP_WorkOrderRouting','CompletedQty') AS ColLen; SELECT r.WoID, r.StepSeq, r.ProcessCode, r.LineID, r.Status, r.CompletedQty FROM dbo.PP_WorkOrderRouting r ORDER BY r.WoID, r.StepSeq; SELECT COUNT(*) AS StillPending FROM dbo.PP_WorkOrderRouting WHERE Status='Pending';"
```

Expected: `ColLen` = 9, 단계 행이 Released/In Progress WO 수만큼(2026-09-01 기준 9건 → A 라우팅 5건×2행 + NULL 4건×1행 = 14행), `StillPending` = 0.

- [ ] **Step 6: 두 번 실행해도 같은 결과인지 확인(멱등)**

Step 4 명령을 한 번 더 실행. Expected: 오류 없음, Step 5 의 행 수 동일.

---

### Task 2: `WorkOrderDto` 단계 필드

**Files:**
- Modify: `src/01_Shared/AMES.Contracts/Dto/WorkOrderDto.cs`

- [ ] **Step 1: 필드 추가**

`SoNumber` 프로퍼티 뒤, `ProgressPct` 앞에 삽입:

```csharp
    // ── 공정 단계 (PP_WorkOrderRouting) ──────────────────────────────
    // 필드 의미 규칙:
    //  · 라인 범위 조회(ListForLine, GetActiveForTerminal): LineId·Status·CompletedQty 는 그 라인 **단계** 값이고
    //    RoutingLineId·StepSeq·ProcessCode 가 채워진다. Pop 은 이걸로 단계 진행률을 그대로 표시한다.
    //  · 헤더 조회(ListAll, GetById): LineId 는 빈 문자열, Status·CompletedQty 는 헤더 값,
    //    RoutingLineId·StepSeq·ProcessCode 는 null, RouteLines 에 단계 라인 나열.
    /// <summary>PP_WorkOrderRouting.RoutingLineID — 라인 범위 조회에서만.</summary>
    public int?              RoutingLineId { get; init; }
    public int?              StepSeq       { get; init; }
    public string?           ProcessCode   { get; init; }
    /// <summary>"LINE-INJ-01 → LINE-IMG-01". 라인 없는 단계는 "QC(—)". 단계 행 없으면 null.</summary>
    public string?           RouteLines    { get; init; }
```

- [ ] **Step 2: 빌드**

```powershell
dotnet build src\01_Shared\AMES.Contracts\AMES.Contracts.csproj
```

Expected: `Build succeeded.`

---

### Task 3: `PreviewRouting` + 레코드 타입

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs`
- Test: `src/07_Etc/AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs`

- [ ] **Step 1: 테스트 픽스처 확장**

`WorkOrderRepositoryTests.cs` 의 상수·`SeedItems`·`CleanupItems` 를 아래로 교체한다(기존 3개 테스트는 그대로 둔다):

```csharp
    const string ItemNoRouting = "ITEST-WO-NORT";
    const string ItemRoutingA  = "ITEST-WO-RTA";
    const string ItemRoutingB  = "ITEST-WO-RTB";

    static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    static void Exec(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(AmesConnectionFactory f, string sql, params (string, object)[] ps)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    /// <summary>품목 3개(라우팅 NULL / A / B) + A 품목 BOP(ST-INJ-01, ST-IMG-01). B 품목은 BOP 없음.</summary>
    static void SeedItems(AmesConnectionFactory f)
    {
        CleanupItems(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, RoutingType, ActiveFlag, CreatedBy)
            VALUES (@A, N'ITEST no routing', NULL, 1, 'ITEST'),
                   (@B, N'ITEST routing A',  'A',  1, 'ITEST'),
                   (@C, N'ITEST routing B',  'B',  1, 'ITEST');
            INSERT INTO dbo.MD_Bop (BOPID, ItemNo, RoutingType, StepSeq, StationCode, ActiveFlag, CreatedBy)
            VALUES ('ITEST-BOP-A-10', @B, 'A', 10, 'ST-INJ-01', 1, 'ITEST'),
                   ('ITEST-BOP-A-20', @B, 'A', 20, 'ST-IMG-01', 1, 'ITEST');
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA), ("@C", ItemRoutingB));
    }

    static void CleanupItems(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE a FROM dbo.PR_WoAcceptance a
              JOIN dbo.PP_WorkOrder w ON w.WoID = a.WoID WHERE w.ItemNo IN (@A, @B, @C);
            DELETE r FROM dbo.PP_WorkOrderRouting r
              JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.PP_WorkOrder     WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.PP_CustomerOrder WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.MD_Bop           WHERE ItemNo IN (@A, @B, @C);
            DELETE FROM dbo.MD_Item          WHERE ItemNo IN (@A, @B, @C);
            """, ("@A", ItemNoRouting), ("@B", ItemRoutingA), ("@C", ItemRoutingB));
    }

    static int CreateDraft(AmesConnectionFactory f, string itemNo, decimal qty = 10)
    {
        var wo = new WorkOrderRepository(f).CreateManualWo(itemNo, qty, DateTime.Today.AddDays(7), "itest");
        Assert.NotEqual(string.Empty, wo);
        return (int)Scalar(f, "SELECT WoID FROM dbo.PP_WorkOrder WHERE WoNumber = @W;", ("@W", wo))!;
    }
```

- [ ] **Step 2: PreviewRouting 실패 테스트 작성**

파일 끝(클래스 닫는 중괄호 앞)에 추가:

```csharp
    // ── PreviewRouting ─────────────────────────────────────────────

    [SkippableFact]
    public void PreviewRouting_returns_bop_line_per_step_and_candidates()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId  = CreateDraft(f, ItemRoutingA);
            var steps = new WorkOrderRepository(f).PreviewRouting(woId);

            Assert.Equal(2, steps.Count);
            var inj = steps[0]; var img = steps[1];

            Assert.Equal(1, inj.StepSeq); Assert.Equal("INJ", inj.ProcessCode);
            Assert.Equal("LINE-INJ-01", inj.BopLineId);
            Assert.True(inj.LineRequired);
            Assert.Contains(inj.Candidates, c => c.LineId == "LINE-INJ-01");
            Assert.Contains(inj.Candidates, c => c.LineId == "LINE-INJ-02");
            Assert.DoesNotContain(inj.Candidates, c => c.LineId == "LINE-IMG-01");

            Assert.Equal(2, img.StepSeq); Assert.Equal("IMG", img.ProcessCode);
            Assert.Equal("LINE-IMG-01", img.BopLineId);
            Assert.True(img.LineRequired);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void PreviewRouting_marks_processes_without_active_line_as_optional()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId  = CreateDraft(f, ItemRoutingB);
            var steps = new WorkOrderRepository(f).PreviewRouting(woId);

            Assert.Equal(4, steps.Count);
            Assert.All(steps, s => Assert.Null(s.BopLineId));          // B 품목은 BOP 없음
            Assert.True (steps.Single(s => s.ProcessCode == "INJ").LineRequired);
            Assert.True (steps.Single(s => s.ProcessCode == "PNT").LineRequired);
            Assert.False(steps.Single(s => s.ProcessCode == "QC").LineRequired);   // LINE-QC-01 INACTIVE
            Assert.Empty(steps.Single(s => s.ProcessCode == "QC").Candidates);
            Assert.False(steps.Single(s => s.ProcessCode == "FG").LineRequired);   // FG 라인 없음
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void PreviewRouting_is_empty_for_non_draft_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Exec(f, "UPDATE dbo.PP_WorkOrder SET Status = 'Cancelled' WHERE WoID = @W;", ("@W", woId));
            Assert.Empty(new WorkOrderRepository(f).PreviewRouting(woId));
        }
        finally { CleanupItems(f); }
    }
```

- [ ] **Step 3: 컴파일 실패 확인**

```powershell
dotnet build src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `error CS1061: 'WorkOrderRepository' does not contain a definition for 'PreviewRouting'`

- [ ] **Step 4: 레코드 타입 + PreviewRouting 구현**

`WorkOrderRepository.cs` 의 `public WorkOrderRepository(AmesConnectionFactory f) => _factory = f;` 바로 뒤에 추가:

```csharp
    // ── 공정 단계 타입 ───────────────────────────────────────────────
    public sealed record LineOption(string LineId, string? LineName)
    {
        public string Display => string.IsNullOrEmpty(LineName) ? LineId : $"{LineId} · {LineName}";
    }

    /// <summary>
    /// Release 다이얼로그용 템플릿 단계. BopLineId = 품목 BOP 스테이션의 라인(공정 일치 첫 행, 활성 라인만).
    /// LineRequired = 그 공정에 활성 라인이 하나라도 있으면 true. Candidates = 그 공정의 활성 라인.
    /// </summary>
    public sealed record RoutingStepPreview(
        int StepSeq, string ProcessCode, string? BopLineId, bool LineRequired,
        IReadOnlyList<LineOption> Candidates);

    public sealed record StepLineChoice(int StepSeq, string? LineId);

    public sealed record StepRow(
        int RoutingLineId, int StepSeq, string ProcessCode, string? LineId, string Status, decimal CompletedQty);

    /// <summary>Draft/Planned WO 의 라우팅 템플릿 미리보기. 그 외 상태·RoutingType NULL 이면 빈 목록.</summary>
    public List<RoutingStepPreview> PreviewRouting(int woId)
    {
        using var conn = _factory.OpenConnection();
        return ReadPreview(conn, null, woId);
    }

    private static List<RoutingStepPreview> ReadPreview(SqlConnection conn, SqlTransaction? tx, int woId)
    {
        const string sql = """
            DECLARE @ItemNo varchar(20), @RT char(1);
            SELECT @ItemNo = ItemNo, @RT = RoutingType
            FROM   dbo.PP_WorkOrder
            WHERE  WoID = @WoID AND Status IN ('Draft','Planned');

            SELECT rs.StepSeq, rs.ProcessCode,
                   (SELECT TOP 1 st.LineID
                    FROM   dbo.MD_Bop b
                    JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
                    JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
                    JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
                    WHERE  b.ItemNo = @ItemNo AND b.RoutingType = @RT AND ISNULL(b.ActiveFlag,1) = 1
                      AND  sw.ProcessCode = rs.ProcessCode
                      AND  ISNULL(sl.Status,'ACTIVE') <> 'INACTIVE'
                    ORDER  BY b.StepSeq) AS BopLineID,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MD_Line l
                                          JOIN dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
                                          WHERE wc.ProcessCode = rs.ProcessCode
                                            AND ISNULL(l.Status,'ACTIVE') <> 'INACTIVE')
                             THEN 1 ELSE 0 END AS bit) AS LineRequired
            FROM   dbo.MD_RoutingStep rs
            WHERE  rs.RoutingType = @RT AND ISNULL(rs.ActiveFlag,1) = 1
            ORDER  BY rs.StepSeq;

            SELECT wc.ProcessCode, l.LineID, l.LineName
            FROM   dbo.MD_Line l
            JOIN   dbo.MD_WorkCenter wc ON wc.WCID = l.WCID
            WHERE  ISNULL(l.Status,'ACTIVE') <> 'INACTIVE'
              AND  wc.ProcessCode IN (SELECT ProcessCode FROM dbo.MD_RoutingStep
                                      WHERE RoutingType = @RT AND ISNULL(ActiveFlag,1) = 1)
            ORDER  BY wc.ProcessCode, l.LineID;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        using var rdr = cmd.ExecuteReader();

        var raw = new List<(int Seq, string Proc, string? Bop, bool Req)>();
        while (rdr.Read())
            raw.Add((Convert.ToInt32(rdr["StepSeq"]), (string)rdr["ProcessCode"],
                     rdr["BopLineID"] as string, (bool)rdr["LineRequired"]));

        var cands = new Dictionary<string, List<LineOption>>();
        if (rdr.NextResult())
            while (rdr.Read())
            {
                var proc = (string)rdr["ProcessCode"];
                if (!cands.TryGetValue(proc, out var list)) cands[proc] = list = new();
                list.Add(new LineOption((string)rdr["LineID"], rdr["LineName"] as string));
            }

        return raw.Select(r => new RoutingStepPreview(
                r.Seq, r.Proc, r.Bop, r.Req,
                cands.TryGetValue(r.Proc, out var c) ? c : Array.Empty<LineOption>()))
            .ToList();
    }
```

- [ ] **Step 5: 테스트 실행**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~WorkOrderRepositoryTests.PreviewRouting"
```

Expected: `Passed! - Failed: 0, Passed: 3`

---

### Task 4: `ReleaseWo(woId, steps, actor)` — 검증 + 단계 생성

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs:219-297` (구 `ReleaseWo`·`GenerateWoRouting` 교체)
- Test: `src/07_Etc/AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs`

이 태스크가 끝나면 Web 이 컴파일되지 않는다(구 시그니처 호출). Task 8 에서 복구한다. 테스트 프로젝트는 Data 만 참조하므로 계속 돈다.

- [ ] **Step 1: 실패 테스트 작성**

`WorkOrderRepositoryTests.cs` 끝에 추가:

```csharp
    // ── ReleaseWo ──────────────────────────────────────────────────

    static WorkOrderRepository.StepLineChoice[] StepsA(string? inj, string? img) =>
        new[] { new WorkOrderRepository.StepLineChoice(1, inj), new WorkOrderRepository.StepLineChoice(2, img) };

    [SkippableFact]
    public void ReleaseWo_rejects_missing_required_line_and_changes_nothing()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var repo = new WorkOrderRepository(f);

            var ex = Assert.Throws<InvalidOperationException>(
                () => repo.ReleaseWo(woId, StepsA(null, "LINE-IMG-01"), "itest"));
            Assert.Contains("Step 1 INJ", ex.Message);

            Assert.Equal("Draft", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;", ("@W", woId))!);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_rejects_line_of_another_process()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Assert.Throws<InvalidOperationException>(
                () => new WorkOrderRepository(f).ReleaseWo(woId, StepsA("LINE-IMG-01", "LINE-INJ-01"), "itest"));
            Assert.Equal("Draft", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_rejects_step_set_mismatch()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            Assert.Throws<InvalidOperationException>(
                () => new WorkOrderRepository(f).ReleaseWo(woId,
                        new[] { new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01") }, "itest"));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_creates_released_steps_and_leaves_header_line_null()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var n = new WorkOrderRepository(f).ReleaseWo(woId, StepsA("LINE-INJ-02", "LINE-IMG-01"), "itest");
            Assert.Equal(1, n);

            Assert.Equal("Released", Scalar(f, "SELECT Status FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));
            Assert.Equal(DBNull.Value, Scalar(f, "SELECT LineID FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));

            var steps = new WorkOrderRepository(f).ListSteps(woId);
            Assert.Equal(2, steps.Count);
            Assert.Equal(("INJ", "LINE-INJ-02", "Released", 0m), (steps[0].ProcessCode, steps[0].LineId, steps[0].Status, steps[0].CompletedQty));
            Assert.Equal(("IMG", "LINE-IMG-01", "Released", 0m), (steps[1].ProcessCode, steps[1].LineId, steps[1].Status, steps[1].CompletedQty));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_stores_null_line_for_optional_steps()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingB);
            var n = new WorkOrderRepository(f).ReleaseWo(woId, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-01"),
                new WorkOrderRepository.StepLineChoice(3, "LINE-QC-01"),   // 무시되어야 함(라인 불필요 공정)
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");
            Assert.Equal(1, n);

            var steps = new WorkOrderRepository(f).ListSteps(woId);
            Assert.Equal(4, steps.Count);
            Assert.Null(steps.Single(s => s.ProcessCode == "QC").LineId);
            Assert.Null(steps.Single(s => s.ProcessCode == "FG").LineId);
            Assert.Equal("LINE-PNT-01", steps.Single(s => s.ProcessCode == "PNT").LineId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ReleaseWo_returns_zero_for_already_released_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var woId = CreateDraft(f, ItemRoutingA);
            var repo = new WorkOrderRepository(f);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            Assert.Equal(0, repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest"));
        }
        finally { CleanupItems(f); }
    }
```

- [ ] **Step 2: 컴파일 실패 확인**

```powershell
dotnet build src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `ListSteps` 미정의·`ReleaseWo` 인자 불일치 오류.

- [ ] **Step 3: 구 `ReleaseWo` + `GenerateWoRouting` 을 새 구현으로 교체**

`WorkOrderRepository.cs` 에서 `// ── PP-004 lifecycle actions` 주석부터 `GenerateWoRouting` 메서드 끝(`CancelWo` 직전)까지를 아래로 교체:

```csharp
    // ── PP-004 lifecycle actions ─────────────────────────────────────

    /// <summary>
    /// WO 를 Released 로 전환하고 공정 단계 행(PP_WorkOrderRouting)을 생성. Draft/Planned 만.
    /// steps 는 다이얼로그가 확정한 단계별 라인. 템플릿과 다시 대조해 검증하고, 실패하면 아무것도 바꾸지 않는다.
    /// 헤더 LineID 는 쓰지 않는다. 반환: 1(발행) / 0(대상 아님).
    /// </summary>
    public int ReleaseWo(int woId, IReadOnlyList<StepLineChoice> steps, string actor)
    {
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            string? status; string? routingType;
            using (var cmd = new SqlCommand(
                "SELECT Status, RoutingType FROM dbo.PP_WorkOrder WITH (UPDLOCK, ROWLOCK) WHERE WoID = @WoID;", conn, tx))
            {
                cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return 0; }
                status      = rdr["Status"]      as string;
                routingType = rdr["RoutingType"] as string;
            }
            if (status is not ("Draft" or "Planned")) { tx.Rollback(); return 0; }
            if (routingType is null)
                throw new InvalidOperationException("WO has no RoutingType; routing template cannot be resolved.");

            var template = ReadPreview(conn, tx, woId);
            ValidateStepChoices(template, steps);

            using (var cmd = new SqlCommand("""
                UPDATE dbo.PP_WorkOrder
                   SET Status     = 'Released',
                       ReleasedAt = SYSDATETIME(),
                       ReleasedBy = @Actor,
                       ModifiedTS = SYSDATETIME(),
                       ModifiedBy = @Actor
                 WHERE WoID = @WoID AND Status IN ('Draft','Planned');
                DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @WoID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",  SqlDbType.Int).Value           = woId;
                cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
                cmd.ExecuteNonQuery();
            }

            const string insSql = """
                INSERT INTO dbo.PP_WorkOrderRouting
                       (WoID, StepSeq, ProcessCode, LineID, StdCycleSec, StdYieldPct, Status, CompletedQty, CreatedBy, CreatedTS)
                SELECT @WoID, @Seq, @Proc, @LineID,
                       (SELECT TOP 1 CAST(b.StdCycleTime AS int)
                        FROM   dbo.MD_Bop b
                        JOIN   dbo.MD_Station    st ON st.StationCode = b.StationCode
                        JOIN   dbo.MD_Line       sl ON sl.LineID      = st.LineID
                        JOIN   dbo.MD_WorkCenter sw ON sw.WCID        = sl.WCID
                        WHERE  b.ItemNo = w.ItemNo AND b.RoutingType = w.RoutingType
                          AND  sw.ProcessCode = @Proc
                        ORDER  BY b.StepSeq),
                       NULL, 'Released', 0, @Actor, SYSDATETIME()
                FROM   dbo.PP_WorkOrder w
                WHERE  w.WoID = @WoID;
                """;
            var choice = steps.ToDictionary(s => s.StepSeq, s => s.LineId);
            foreach (var t in template)
            {
                using var ins = new SqlCommand(insSql, conn, tx);
                ins.Parameters.Add("@WoID",   SqlDbType.Int).Value           = woId;
                ins.Parameters.Add("@Seq",    SqlDbType.Int).Value           = t.StepSeq;
                ins.Parameters.Add("@Proc",   SqlDbType.VarChar, 10).Value   = t.ProcessCode;
                ins.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value   =
                    t.LineRequired ? (object)choice[t.StepSeq]! : DBNull.Value;
                ins.Parameters.Add("@Actor",  SqlDbType.NVarChar, 450).Value = actor;
                ins.ExecuteNonQuery();
            }

            tx.Commit();
            return 1;
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>템플릿 단계 집합과 steps 가 1:1 이고, 라인 필수 단계는 그 공정의 활성 라인이 지정됐는지.</summary>
    private static void ValidateStepChoices(IReadOnlyList<RoutingStepPreview> template, IReadOnlyList<StepLineChoice> steps)
    {
        var bySeq = new Dictionary<int, string?>();
        foreach (var s in steps)
            if (!bySeq.TryAdd(s.StepSeq, s.LineId))
                throw new InvalidOperationException($"Step {s.StepSeq}: duplicated.");
        if (template.Count == 0 || bySeq.Count != template.Count || template.Any(t => !bySeq.ContainsKey(t.StepSeq)))
            throw new InvalidOperationException("Routing steps do not match the template.");

        foreach (var t in template)
        {
            if (!t.LineRequired) continue;
            var lineId = bySeq[t.StepSeq];
            if (string.IsNullOrWhiteSpace(lineId))
                throw new InvalidOperationException($"Step {t.StepSeq} {t.ProcessCode}: line required.");
            if (!t.Candidates.Any(c => c.LineId == lineId))
                throw new InvalidOperationException($"Step {t.StepSeq} {t.ProcessCode}: '{lineId}' is not an active {t.ProcessCode} line.");
        }
    }

    /// <summary>WO 의 공정 단계 행 (PP-04 상세 펼침용). StepSeq 순.</summary>
    public List<StepRow> ListSteps(int woId)
    {
        const string sql = """
            SELECT RoutingLineID, StepSeq, ProcessCode, LineID, Status, CompletedQty
            FROM   dbo.PP_WorkOrderRouting
            WHERE  WoID = @WoID
            ORDER  BY StepSeq;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@WoID", SqlDbType.Int).Value = woId;
        using var rdr = cmd.ExecuteReader();
        var list = new List<StepRow>();
        while (rdr.Read())
            list.Add(new StepRow(
                (int)rdr["RoutingLineID"],
                Convert.ToInt32(rdr["StepSeq"]),
                (string)rdr["ProcessCode"],
                rdr["LineID"] as string,
                rdr["Status"] as string ?? "Released",
                rdr["CompletedQty"] as decimal? ?? 0m));
        return list;
    }
```

- [ ] **Step 4: 테스트 실행**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~WorkOrderRepositoryTests"
```

Expected: `Passed! - Failed: 0, Passed: 12` (기존 3 + Preview 3 + Release 6)

---

### Task 5: `BumpStepCompleted` / `FindStepId` — 실적 단일 진입점

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs` (`AddCompletedQty` 교체)
- Test: `src/07_Etc/AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`WorkOrderRepositoryTests.cs` 끝에 추가:

```csharp
    // ── BumpStepCompleted ──────────────────────────────────────────

    static decimal Bump(AmesConnectionFactory f, int routingLineId, decimal qty)
    {
        using var conn = f.OpenConnection();
        using var tx   = conn.BeginTransaction();
        var n = WorkOrderRepository.BumpStepCompleted(conn, tx, routingLineId, qty, "itest");
        tx.Commit();
        return n;
    }

    static (string Status, decimal Completed, bool HasEnd) Header(AmesConnectionFactory f, int woId)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT Status, ISNULL(CompletedQty,0) AS C, CASE WHEN ActualEnd IS NULL THEN 0 ELSE 1 END AS E FROM dbo.PP_WorkOrder WHERE WoID = @W;", conn);
        cmd.Parameters.AddWithValue("@W", woId);
        using var r = cmd.ExecuteReader(); r.Read();
        return ((string)r["Status"], (decimal)r["C"], (int)r["E"] == 1);
    }

    [SkippableFact]
    public void BumpStepCompleted_syncs_header_only_from_last_line_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 10);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var steps = repo.ListSteps(woId);
            var inj = steps[0].RoutingLineId; var img = steps[1].RoutingLineId;

            Assert.Equal(4m, Bump(f, inj, 4));
            Assert.Equal(("Released", 0m, false), Header(f, woId));          // INJ 는 헤더에 안 올라간다

            Assert.Equal(6m, Bump(f, img, 6));
            Assert.Equal(("Released", 6m, false), Header(f, woId));          // 마지막 라인 단계 → 동기화

            Assert.Equal(10m, Bump(f, img, 4));
            Assert.Equal(("Closed", 10m, true), Header(f, woId));           // OrderQty 도달 → Closed

            steps = repo.ListSteps(woId);
            Assert.Equal("Released", steps[0].Status);                        // INJ 단계는 그대로
            Assert.Equal(("Closed", 10m), (steps[1].Status, steps[1].CompletedQty));
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void BumpStepCompleted_treats_last_line_step_as_last_for_routing_b()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingB, qty: 5);
            repo.ReleaseWo(woId, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-01"),
                new WorkOrderRepository.StepLineChoice(3, null),
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");
            var pnt = repo.ListSteps(woId).Single(s => s.ProcessCode == "PNT").RoutingLineId;

            Bump(f, pnt, 5);
            Assert.Equal(("Closed", 5m, true), Header(f, woId));   // QC·FG 는 라인 없음 → PNT 가 마지막
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void BumpStepCompleted_throws_for_unknown_step()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Assert.ThrowsAny<SqlException>(() => Bump(f, -1, 1));
    }

    [SkippableFact]
    public void FindStepId_resolves_step_by_wo_and_line()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var img = repo.ListSteps(woId)[1].RoutingLineId;

            using var conn = f.OpenConnection();
            using var tx   = conn.BeginTransaction();
            Assert.Equal(img, WorkOrderRepository.FindStepId(conn, tx, woId, "LINE-IMG-01"));
            Assert.Null(WorkOrderRepository.FindStepId(conn, tx, woId, "LINE-PNT-01"));
            tx.Rollback();
        }
        finally { CleanupItems(f); }
    }
```

- [ ] **Step 2: 테스트 프로젝트가 internal 을 보게 한다**

`src/02_Data/AMES.Data/AMES.Data.csproj` 에 `InternalsVisibleTo` 가 없으면 `<Project>` 안에 추가:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="AMES.InjAgent.Tests" />
  </ItemGroup>
```

(2026-09-01 기준 csproj 에 `InternalsVisibleTo` 가 없으므로 반드시 추가한다.)

- [ ] **Step 3: 컴파일 실패 확인**

```powershell
dotnet build src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `BumpStepCompleted`·`FindStepId` 미정의 오류.

- [ ] **Step 4: `AddCompletedQty` 를 아래 두 메서드로 교체**

`WorkOrderRepository.cs` 의 `AddCompletedQty` 메서드(주석 포함)를 삭제하고 그 자리에:

```csharp
    /// <summary>
    /// 실적 반영 단일 진입점. 호출측 트랜잭션에 참여한다.
    /// 단계 CompletedQty += qty. 헤더 OrderQty 도달 시 단계 Closed·ActualEnd.
    /// 이 단계가 "라인이 있는 마지막 단계"(LineID NOT NULL 중 최대 StepSeq)면 헤더 CompletedQty 를 동기화하고,
    /// 도달 시 헤더 Closed·ActualEnd. 반환: 단계의 새 CompletedQty. 단계가 없으면 SqlException(50001).
    /// </summary>
    internal static decimal BumpStepCompleted(SqlConnection conn, SqlTransaction tx, int routingLineId, decimal qty, string actor)
    {
        const string sql = """
            DECLARE @WoID int, @Seq int, @OrderQty decimal(14,3), @New decimal(14,3), @LastSeq int;

            SELECT @WoID = r.WoID, @Seq = r.StepSeq, @OrderQty = ISNULL(w.OrderQty, 0)
            FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
            JOIN   dbo.PP_WorkOrder        w WITH (UPDLOCK, ROWLOCK) ON w.WoID = r.WoID
            WHERE  r.RoutingLineID = @RL;
            IF @WoID IS NULL THROW 50001, 'Routing step not found.', 1;

            UPDATE dbo.PP_WorkOrderRouting
            SET    CompletedQty = CompletedQty + @Qty,
                   Status       = CASE WHEN CompletedQty + @Qty >= @OrderQty THEN 'Closed' ELSE Status END,
                   ActualEnd    = CASE WHEN CompletedQty + @Qty >= @OrderQty THEN SYSDATETIME() ELSE ActualEnd END,
                   ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
            WHERE  RoutingLineID = @RL;

            SELECT @New = CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @RL;
            SELECT @LastSeq = MAX(StepSeq) FROM dbo.PP_WorkOrderRouting WHERE WoID = @WoID AND LineID IS NOT NULL;

            IF @Seq = @LastSeq
                UPDATE dbo.PP_WorkOrder
                SET    CompletedQty = @New,
                       Status       = CASE WHEN @New >= @OrderQty THEN 'Closed' ELSE Status END,
                       ActualEnd    = CASE WHEN @New >= @OrderQty THEN SYSDATETIME() ELSE ActualEnd END,
                       ModifiedBy   = @Actor, ModifiedTS = SYSDATETIME()
                WHERE  WoID = @WoID;

            SELECT @New;
            """;
        using var cmd = new SqlCommand(sql, conn, tx);
        cmd.Parameters.Add("@RL",    SqlDbType.Int).Value           = routingLineId;
        cmd.Parameters.Add("@Qty",   SqlDbType.Decimal).Precision   = 14;
        cmd.Parameters["@Qty"].Scale = 3;
        cmd.Parameters["@Qty"].Value = qty;
        cmd.Parameters.Add("@Actor", SqlDbType.NVarChar, 450).Value = actor;
        return (decimal)cmd.ExecuteScalar()!;
    }

    /// <summary>(WoID, LineID) 로 단계 행을 찾는다. 같은 라인에 단계가 둘이면 StepSeq 가 작은 쪽. 없으면 null.</summary>
    internal static int? FindStepId(SqlConnection conn, SqlTransaction tx, int woId, string lineId)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP 1 RoutingLineID FROM dbo.PP_WorkOrderRouting
            WHERE  WoID = @WoID AND LineID = @LineID
            ORDER  BY StepSeq;
            """, conn, tx);
        cmd.Parameters.Add("@WoID",   SqlDbType.Int).Value         = woId;
        cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() is int id ? id : null;
    }
```

- [ ] **Step 5: 테스트 실행**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~WorkOrderRepositoryTests"
```

Expected: `Passed! - Failed: 0, Passed: 16`

---

### Task 6: 라인 범위 조회·접수를 단계 기준으로

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/WorkOrderRepository.cs` (`ListAll`, `ListForLine`, `GetActiveForTerminal`, `AcceptWo`, `Query` 매퍼)
- Test: `src/07_Etc/AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs`

- [ ] **Step 1: 실패 테스트 작성**

`WorkOrderRepositoryTests.cs` 끝에 추가:

```csharp
    // ── 라인 범위 조회 · 접수 ─────────────────────────────────────

    [SkippableFact]
    public void ListForLine_shows_wo_on_every_step_line_with_step_values()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA, qty: 10);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-02", "LINE-IMG-01"), "itest");
            Bump(f, repo.ListSteps(woId)[0].RoutingLineId, 3);

            var onInj = repo.ListForLine("LINE-INJ-02").Single(w => w.WoId == woId);
            Assert.Equal(("INJ", 1, "LINE-INJ-02", 3m, "Released"), (onInj.ProcessCode, onInj.StepSeq, onInj.LineId, onInj.CompletedQty, onInj.Status));
            Assert.NotNull(onInj.RoutingLineId);

            var onImg = repo.ListForLine("LINE-IMG-01").Single(w => w.WoId == woId);
            Assert.Equal(("IMG", 2, "LINE-IMG-01", 0m), (onImg.ProcessCode, onImg.StepSeq, onImg.LineId, onImg.CompletedQty));

            Assert.DoesNotContain(repo.ListForLine("LINE-INJ-01"), w => w.WoId == woId);
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void AcceptWo_marks_step_and_header_in_progress_and_active_lookup_finds_it()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var woId = CreateDraft(f, ItemRoutingA);
            repo.ReleaseWo(woId, StepsA("LINE-INJ-01", "LINE-IMG-01"), "itest");
            var img = repo.ListSteps(woId)[1].RoutingLineId;

            var acceptId = repo.AcceptWo(img, "IMG-T1", "itest-op", "E-ITEST", "{}");
            Assert.True(acceptId > 0);

            var steps = repo.ListSteps(woId);
            Assert.Equal("Released",    steps[0].Status);
            Assert.Equal("In Progress", steps[1].Status);
            Assert.Equal(("In Progress", 0m, false), Header(f, woId));
            Assert.Equal("IMG-T1", Scalar(f, "SELECT TerminalLock FROM dbo.PP_WorkOrder WHERE WoID = @W;", ("@W", woId)));

            var active = repo.GetActiveForTerminal("LINE-IMG-01", "IMG-T1");
            Assert.NotNull(active);
            Assert.Equal((woId, img, "IMG"), (active!.WoId, active.RoutingLineId, active.ProcessCode));

            Assert.Null(repo.GetActiveForTerminal("LINE-INJ-01", "INJ-T1"));   // INJ 단계는 아직 Released
        }
        finally { CleanupItems(f); }
    }

    [SkippableFact]
    public void ListAll_carries_route_lines_for_released_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        SeedItems(f);
        try
        {
            var repo = new WorkOrderRepository(f);
            var draft = CreateDraft(f, ItemRoutingA);
            var rel   = CreateDraft(f, ItemRoutingB);
            repo.ReleaseWo(rel, new[]
            {
                new WorkOrderRepository.StepLineChoice(1, "LINE-INJ-01"),
                new WorkOrderRepository.StepLineChoice(2, "LINE-PNT-02"),
                new WorkOrderRepository.StepLineChoice(3, null),
                new WorkOrderRepository.StepLineChoice(4, null),
            }, "itest");

            var all = repo.ListAll();
            Assert.Null(all.Single(w => w.WoId == draft).RouteLines);
            Assert.Equal("LINE-INJ-01 → LINE-PNT-02 → QC(—) → FG(—)", all.Single(w => w.WoId == rel).RouteLines);
            Assert.Null(all.Single(w => w.WoId == rel).RoutingLineId);
        }
        finally { CleanupItems(f); }
    }
```

- [ ] **Step 2: 컴파일 실패 확인**

```powershell
dotnet build src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `AcceptWo(int, string, string, string, string)` 는 시그니처가 같아 컴파일은 되지만 테스트가 실패한다. 실행:

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~WorkOrderRepositoryTests.ListForLine|FullyQualifiedName~WorkOrderRepositoryTests.AcceptWo|FullyQualifiedName~WorkOrderRepositoryTests.ListAll_carries"
```

Expected: 3개 모두 FAIL.

- [ ] **Step 3: `ListAll` 에 `RouteLines` 추가**

`ListAll` SQL 의 `so.SoNumber AS SoNumber` 줄 뒤에 추가:

```sql
                   so.SoNumber AS SoNumber,
                   (SELECT STRING_AGG(CAST(ISNULL(r.LineID, r.ProcessCode + N'(—)') AS nvarchar(40)), N' → ')
                               WITHIN GROUP (ORDER BY r.StepSeq)
                    FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID) AS RouteLines
```

- [ ] **Step 4: `ListForLine` 교체**

```csharp
    /// <summary>
    /// 이 라인에 배정된 공정 단계가 열려 있는 WO (단계 Status Released/In Progress).
    /// 반환 DTO 의 LineId·Status·CompletedQty 는 단계 값이다 (WorkOrderDto 주석 참고).
    /// </summary>
    public List<WorkOrderDto> ListForLine(string lineId)
    {
        const string sql = """
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, r.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority, w.RoutingType,
                   r.RoutingLineID, r.StepSeq, r.ProcessCode
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            WHERE  r.LineID = @LineID
              AND  r.Status IN ('Released','In Progress')
              AND  w.Status IN ('Released','In Progress')
            ORDER  BY CASE WHEN r.Status='In Progress' THEN 0 ELSE 1 END,
                      ISNULL(w.Priority,5),
                      ISNULL(w.DueDate,'9999-12-31'),
                      w.WoID;
            """;

        return Query(sql, cmd => cmd.Parameters.Add("@LineID", SqlDbType.VarChar, 20).Value = lineId);
    }
```

- [ ] **Step 5: `GetActiveForTerminal` 교체**

```csharp
    /// <summary>이 터미널이 진행 중인 단계의 WO. LineId·Status·CompletedQty 는 단계 값.</summary>
    public WorkOrderDto? GetActiveForTerminal(string lineId, string terminalId)
    {
        // Ranked by the most recent WO Confirm (PR_WoAcceptance.AcceptedAt), not
        // r.ActualStart — ActualStart is set once (first accept) and never bumped
        // on re-accept, so it can't tell which WO the operator switched to last.
        const string sql = """
            SELECT TOP 1 w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   w.OrderQty, w.OpenQty, r.CompletedQty, r.LineID,
                   w.MoldID, w.RecipeID, w.DueDate, r.Status, w.TerminalLock,
                   ISNULL(w.Priority,5) AS Priority, w.RoutingType,
                   r.RoutingLineID, r.StepSeq, r.ProcessCode
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID   = r.WoID
            JOIN   dbo.MD_Item      i ON i.ItemNo = w.ItemNo
            OUTER APPLY (
                SELECT MAX(a.AcceptedAt) AS LastAcceptedAt
                FROM   dbo.PR_WoAcceptance a
                WHERE  a.WoID = w.WoID AND a.TerminalID = @TerminalID
            ) la
            WHERE  r.LineID = @LineID
              AND  r.Status = 'In Progress'
              AND  w.Status = 'In Progress'
              AND (w.TerminalLock = @TerminalID OR w.TerminalLock IS NULL)
            ORDER  BY la.LastAcceptedAt DESC, r.ActualStart DESC;
            """;

        return Query(sql, cmd =>
        {
            cmd.Parameters.Add("@LineID",     SqlDbType.VarChar, 20).Value = lineId;
            cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20).Value = terminalId;
        }).FirstOrDefault();
    }
```

- [ ] **Step 6: `AcceptWo` 교체**

```csharp
    /// <summary>
    /// 공정 단계를 이 터미널에 접수. 단계·헤더 In Progress, 헤더 TerminalLock. 체크리스트는 PR_WoAcceptance(WoID) 에 기록.
    /// Returns the new AcceptID. 단계가 없으면 SqlException(50001).
    /// </summary>
    public int AcceptWo(int routingLineId, string terminalId, string operatorId,
                        string employeeNo, string checkResultsJson)
    {
        const string sql = """
            DECLARE @WoID int = (SELECT WoID FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID = @RL);
            IF @WoID IS NULL THROW 50001, 'Routing step not found.', 1;

            DECLARE @Out TABLE (AcceptID int);
            INSERT INTO dbo.PR_WoAcceptance
                (WoID, TerminalID, OperatorID, AcceptedAt, CheckResults, CheckPassed, CreatedBy, CreatedTS)
            OUTPUT INSERTED.AcceptID INTO @Out
            VALUES (@WoID, @TerminalID, @OperatorID, SYSDATETIME(), @Checks, 1, @CreatedBy, SYSDATETIME());

            UPDATE dbo.PP_WorkOrderRouting
            SET    Status      = 'In Progress',
                   ActualStart = ISNULL(ActualStart, SYSDATETIME()),
                   ModifiedBy  = @OperatorID, ModifiedTS = SYSDATETIME()
            WHERE  RoutingLineID = @RL;

            UPDATE dbo.PP_WorkOrder
            SET    Status       = 'In Progress',
                   TerminalLock = @TerminalID,
                   ActualStart  = ISNULL(ActualStart, SYSDATETIME()),
                   ModifiedBy   = @OperatorID, ModifiedTS = SYSDATETIME()
            WHERE  WoID = @WoID;

            SELECT AcceptID FROM @Out;
            """;
        using var conn = _factory.OpenConnection();
        using var tx   = conn.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(sql, conn, tx);
            cmd.Parameters.Add("@RL",         SqlDbType.Int           ).Value = routingLineId;
            cmd.Parameters.Add("@TerminalID", SqlDbType.VarChar, 20   ).Value = terminalId;
            cmd.Parameters.Add("@OperatorID", SqlDbType.NVarChar, 450 ).Value = operatorId;
            cmd.Parameters.Add("@Checks",     SqlDbType.NVarChar      ).Value = checkResultsJson;
            cmd.Parameters.Add("@CreatedBy",  SqlDbType.VarChar, 50   ).Value = employeeNo;
            var acceptId = (int)cmd.ExecuteScalar()!;
            tx.Commit();
            return acceptId;
        }
        catch { tx.Rollback(); throw; }
    }
```

- [ ] **Step 7: `Query` 매퍼에 단계 필드 추가**

`Query` 메서드의 `SoNumber = ...` 줄 뒤에:

```csharp
                SoNumber      = HasColumn(rdr, "SoNumber")      ? rdr["SoNumber"]      as string  : null,
                RoutingLineId = HasColumn(rdr, "RoutingLineID") ? rdr["RoutingLineID"] as int?    : null,
                StepSeq       = HasColumn(rdr, "StepSeq") && rdr["StepSeq"] is not DBNull ? (int?)Convert.ToInt32(rdr["StepSeq"]) : null,
                ProcessCode   = HasColumn(rdr, "ProcessCode")   ? rdr["ProcessCode"]   as string  : null,
                RouteLines    = HasColumn(rdr, "RouteLines")    ? rdr["RouteLines"]    as string  : null,
```

- [ ] **Step 8: 테스트 실행**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~WorkOrderRepositoryTests"
```

Expected: `Passed! - Failed: 0, Passed: 19`

---

### Task 7: `ConfirmByLotCode` · `RecordCycle` 을 단계 실적으로

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/InjLotRepository.cs:531-546, 595-614`
- Modify: `src/02_Data/AMES.Data/Repositories/ProductionRepository.cs:94-112`
- Test: `src/07_Etc/AMES.InjAgent.Tests/InjLotRepositoryTests.cs`, `ProductionRepositoryTests.cs`

- [ ] **Step 1: 기존 테스트 픽스처에 단계 행 추가**

세 곳의 WO INSERT 를 헤더 + 단계 1행으로 바꾼다. 단일 INJ(또는 IMG) 단계이므로 "라인 있는 마지막 단계" 가 되어 헤더 `CompletedQty` 동기화 단정은 그대로 유효하다.

`InjLotRepositoryTests.cs` `Confirm_raw_lot_creates_result_and_bumps_wo` 의 INSERT(라인 124-127)를:

```csharp
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-CONFIRM', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
```

로 바꾸고(`LineID` 컬럼 제거), `woId = (int)cmd.ExecuteScalar()!;` 바로 뒤에 추가:

```csharp
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
                VALUES (@W, 1, 'INJ', 'LINE-INJ-01', 'In Progress', 0, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }
```

같은 테스트의 검증 SELECT(라인 145-150)에 단계 수량 확인을 추가:

```csharp
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1) AS StepCompleted,
```

와 `Assert.Equal(1m, (decimal)rdr["StepCompleted"]);` 를 `Completed` 단정 뒤에 추가.

finally 의 DELETE 블록 첫 줄 앞에 `DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;` 추가.

`CreateManualRawLots…` 테스트(라인 223-226)도 같은 방식: INSERT 에서 `LineID` 컬럼·값 제거, 단계 행 INSERT 추가(`'WO-ITEST-MANUAL'`), finally 에 `DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;` 추가.

`ProductionRepositoryTests.cs` `RecordCycle_uses_9char_rule`(라인 36-40): INSERT 에서 `LineID` 제거 후 단계 행 추가:

```csharp
        using (var conn = f.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
            VALUES (@W, 1, 'IMG', 'LINE-IMG-01', 'In Progress', 0, 'ITEST');
            """, conn))
        { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }
```

finally 에 `DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;` 추가.

- [ ] **Step 2: 테스트 실행해 실패 확인**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj --filter "FullyQualifiedName~Confirm_raw_lot|FullyQualifiedName~CreateManualRawLots|FullyQualifiedName~RecordCycle_uses"
```

Expected: `Confirm_raw_lot…` 와 `CreateManualRawLots…` 는 `NoWoForItem`(헤더 LineID 가 없으므로 구 쿼리가 못 찾음)로 FAIL. `RecordCycle_uses_9char_rule` 은 아직 헤더 UPDATE 라 PASS 할 수 있다 — Step 4 뒤 단계 수량 단정으로 잡는다.

- [ ] **Step 3: `ConfirmByLotCode` WO 선택을 단계 기준으로**

`InjLotRepository.cs` 의 `int woId;` 블록(라인 531-546)을 교체:

```csharp
            int woId, stepId;
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 r.WoID, r.RoutingLineID
                FROM   dbo.PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK)
                JOIN   dbo.PP_WorkOrder        w WITH (UPDLOCK, ROWLOCK) ON w.WoID = r.WoID
                WHERE  r.LineID = @Line AND w.ItemNo = @Item
                  AND  r.Status IN ('Released','In Progress')
                  AND  w.Status IN ('Released','In Progress')
                ORDER  BY CASE WHEN r.Status = 'In Progress' THEN 0 ELSE 1 END,
                          ISNULL(w.Priority,5),
                          ISNULL(w.DueDate,'9999-12-31'),
                          w.WoID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@Line", SqlDbType.VarChar, 20).Value = lineId;
                cmd.Parameters.Add("@Item", SqlDbType.VarChar, 20).Value = itemNo;
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read()) { rdr.Close(); tx.Rollback(); return (InjConfirmOutcome.NoWoForItem, 0, itemNo, 0); }
                woId   = (int)rdr["WoID"];
                stepId = (int)rdr["RoutingLineID"];
            }
```

- [ ] **Step 4: `ConfirmByLotCode` 헤더 UPDATE 를 `BumpStepCompleted` 로**

라인 595-614 의 `using (var cmd = new SqlCommand("""UPDATE dbo.tbl_Lot … UPDATE dbo.PP_WorkOrder … """))` 블록에서 `UPDATE dbo.PP_WorkOrder … WHERE WoID = @WoID;` 문장을 제거해 아래로 만들고, 그 블록 뒤에 `BumpStepCompleted` 호출을 넣는다:

```csharp
            using (var cmd = new SqlCommand("""
                UPDATE dbo.tbl_Lot
                SET    Status = 'CONFIRMED', QualityFlag = 'OK', WoID = @WoID,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;

                UPDATE dbo.PR_InjLot
                SET    ConfirmStatus = 'CONFIRMED', ConfirmedAt = SYSDATETIME(),
                       ConfirmedBy = @Op, ConfirmedSessionID = @Sess,
                       ModifiedBy = @Op, ModifiedTS = SYSDATETIME()
                WHERE  LotID = @LotID;
                """, conn, tx))
            {
                cmd.Parameters.Add("@WoID",  SqlDbType.Int          ).Value = woId;
                cmd.Parameters.Add("@LotID", SqlDbType.Int          ).Value = lotId;
                cmd.Parameters.Add("@Op",    SqlDbType.NVarChar, 450).Value = operatorId;
                cmd.Parameters.Add("@Sess",  SqlDbType.Int          ).Value = (object?)sessionId ?? DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, 1m, operatorId);
```

메서드 주석의 "WO 수량 증가" 문구를 "단계 실적 +1(`WorkOrderRepository.BumpStepCompleted`)" 로 고친다.

- [ ] **Step 5: `RecordCycle` 3) 블록 교체**

`ProductionRepository.cs` 라인 94-112(`// 3) PP_WorkOrder.CompletedQty bump` 부터 `newCompleted = …;` 블록 끝까지)를:

```csharp
            // 3) 단계 실적 반영 (WoID + LineID 로 단계 행 특정)
            var stepId = WorkOrderRepository.FindStepId(conn, tx, woId, lineId)
                ?? throw new InvalidOperationException($"WO {woId} has no routing step on line {lineId}.");
            var newCompleted = WorkOrderRepository.BumpStepCompleted(conn, tx, stepId, goodQty, operatorId);
```

메서드 주석 "Increments WO CompletedQty" 를 "Increments the step CompletedQty on (WoID, LineID)" 로 고친다.

- [ ] **Step 6: RecordCycle 테스트에 단계 수량 단정 추가**

`ProductionRepositoryTests.cs` 의 `Assert.Equal(10m, newCompleted);` 뒤에:

```csharp
            using (var conn2 = f.OpenConnection())
            using (var cmd2 = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1;", conn2))
            {
                cmd2.Parameters.AddWithValue("@W", woId);
                Assert.Equal(10m, (decimal)cmd2.ExecuteScalar()!);
            }
```

- [ ] **Step 7: 전체 테스트 실행**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `Failed: 0`. (DB 미기동이면 Skipped 로 표시된다. 반드시 DB 가 붙은 상태에서 돌린다.)

---

### Task 8: 나머지 Data 소비자 — `PpRepository` · `LineScheduleRepository` · `OeeRepository`

**Files:**
- Modify: `src/02_Data/AMES.Data/Repositories/PpRepository.cs:61-63, 640-686, 706-726, 1047-1053`
- Modify: `src/02_Data/AMES.Data/Repositories/LineScheduleRepository.cs:368-378`
- Modify: `src/02_Data/AMES.Data/Repositories/OeeRepository.cs:97-104`

- [ ] **Step 1: `WoLite` 의 `LineId` → `RouteLines`**

```csharp
    public sealed record WoLite(int WoId, string? WoNumber, string ItemNo, string? ItemName,
        decimal OrderQty, decimal CompletedQty, string? RouteLines, DateTime? DueDate,
        string? Status, DateTime? ReleasedAt);
```

`MapWoLite`:

```csharp
    private static WoLite MapWoLite(IDataReader r) => new(
        (int)r["WoID"], r["WoNumber"] as string,
        r["ItemNo"] as string ?? "", r["ItemName"] as string,
        r.GetDecimal(r.GetOrdinal("OrderQty")),
        r.GetDecimal(r.GetOrdinal("CompletedQty")),
        r["RouteLines"] as string, r["DueDate"] as DateTime?,
        r["Status"] as string, r["ReleasedAt"] as DateTime?);
```

- [ ] **Step 2: `ListReleasable` · `ListAllWo` SELECT 의 `w.LineID` 를 서브쿼리로**

두 SQL 모두 `w.LineID, w.DueDate, …` 를:

```sql
                   (SELECT STRING_AGG(CAST(ISNULL(r.LineID, r.ProcessCode + N'(—)') AS nvarchar(40)), N' → ')
                               WITHIN GROUP (ORDER BY r.StepSeq)
                    FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID) AS RouteLines,
                   w.DueDate, ISNULL(w.Status,'Draft') AS Status, w.ReleasedAt
```

`ListAllWo` 의 WHERE 첫 줄을:

```sql
            WHERE  (@LineID IS NULL
                    OR EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID AND r.LineID = @LineID))
```

- [ ] **Step 3: `PpRepository.ReleaseWo` 삭제**

라인 706-726(주석 포함 `public int ReleaseWo(int woId, string lineId, string actor)` 메서드 전체)을 삭제한다.

- [ ] **Step 4: `LineScheduleRepository.ListLineWos` SQL 교체**

```sql
            SELECT w.WoID, w.WoNumber, w.ItemNo, i.ItemName,
                   ISNULL(w.OpenQty,0) AS OpenQty, r.Status
            FROM   dbo.PP_WorkOrderRouting r
            JOIN   dbo.PP_WorkOrder w ON w.WoID = r.WoID
            LEFT JOIN dbo.MD_Item i ON i.ItemNo = w.ItemNo
            WHERE  r.LineID = @LineId
              AND  r.Status IN ('Released','In Progress')
              AND  w.Status IN ('Released','In Progress')
            ORDER  BY CASE WHEN r.Status='In Progress' THEN 0 ELSE 1 END, w.WoID;
```

- [ ] **Step 5: `OeeRepository.ListLines` SQL 교체**

```sql
            SELECT DISTINCT LineId FROM (
                SELECT LineID AS LineId FROM dbo.PP_WorkOrderRouting WHERE LineID IS NOT NULL
                UNION
                SELECT LineId FROM dbo.PP_LineStateLog
            ) t ORDER BY LineId;
```

- [ ] **Step 6: Data 빌드**

```powershell
dotnet build src\02_Data\AMES.Data\AMES.Data.csproj
```

Expected: `Build succeeded.` (경고는 무시. 오류 0.)

- [ ] **Step 7: 헤더 `LineID` 를 읽거나 쓰는 코드가 남지 않았는지 확인**

```powershell
Select-String -Path src\02_Data\AMES.Data\Repositories\*.cs -Pattern "w\.LineID|PP_WorkOrder\.LineID|SET\s+LineID"
```

Expected: 결과 없음. 테스트 파일은 대상이 아니다.

---

### Task 9: Web — Release 다이얼로그 재작성 + resx

**Files:**
- Modify: `src/06_Web/AMES.Web/Components/Pages/Pp/WoReleaseLineDialog.razor` (전체 교체)
- Modify: `src/06_Web/AMES.Web/Resources/SharedResources.resx`, `SharedResources.en.resx`

- [ ] **Step 1: resx 키 6개 추가**

두 파일의 `</root>` 바로 앞에 삽입.

`SharedResources.resx`:

```xml
  <data name="PP.WoRelease.Dlg.Step" xml:space="preserve">
    <value>공정 단계</value>
  </data>
  <data name="PP.WoRelease.Badge.Bop" xml:space="preserve">
    <value>BOP</value>
  </data>
  <data name="PP.WoRelease.Badge.Changed" xml:space="preserve">
    <value>변경</value>
  </data>
  <data name="PP.WoRelease.Badge.NoBop" xml:space="preserve">
    <value>BOP 미등록</value>
  </data>
  <data name="PP.WoRelease.NoLineProcess" xml:space="preserve">
    <value>라인 없음 · 터미널 미배정</value>
  </data>
  <data name="PP.WoRelease.Err.NoSteps" xml:space="preserve">
    <value>라우팅 템플릿이 없어 발행할 수 없습니다. 품목의 라우팅 유형을 확인하세요.</value>
  </data>
```

`SharedResources.en.resx`:

```xml
  <data name="PP.WoRelease.Dlg.Step" xml:space="preserve">
    <value>Process step</value>
  </data>
  <data name="PP.WoRelease.Badge.Bop" xml:space="preserve">
    <value>BOP</value>
  </data>
  <data name="PP.WoRelease.Badge.Changed" xml:space="preserve">
    <value>Changed</value>
  </data>
  <data name="PP.WoRelease.Badge.NoBop" xml:space="preserve">
    <value>No BOP</value>
  </data>
  <data name="PP.WoRelease.NoLineProcess" xml:space="preserve">
    <value>No line · not assigned to a terminal</value>
  </data>
  <data name="PP.WoRelease.Err.NoSteps" xml:space="preserve">
    <value>No routing template; cannot release. Check the item's routing type.</value>
  </data>
```

- [ ] **Step 2: 다이얼로그 전체 교체**

`WoReleaseLineDialog.razor`:

```razor
@inject AMES.Data.Repositories.WorkOrderRepository Wos
@inject DialogService Dialog
@inject IStringLocalizer<SharedResources> L
@using AMES.Data.Repositories

<div style="display:flex;flex-direction:column;gap:10px;min-width:560px;">
    @if (Phase0Incomplete)
    {
        <div class="ames-alert bad">🚧 @L["PP.WoRelease.Dlg.Phase0Warn"]</div>
    }

    @if (_err is not null)
    {
        <div class="ames-alert bad">@_err</div>
    }
    else if (_steps.Count == 0)
    {
        <div class="ames-alert bad">@L["PP.WoRelease.Err.NoSteps"]</div>
    }
    else
    {
        <table class="ames-table">
            <thead>
                <tr>
                    <th style="width:44px;">#</th>
                    <th style="width:90px;">@L["PP.WoRelease.Dlg.Step"]</th>
                    <th>@L["Col.Line"]</th>
                    <th style="width:110px;"></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var vm in _steps)
                {
                    <tr>
                        <td class="code">@vm.Step.StepSeq</td>
                        <td class="code">@vm.Step.ProcessCode</td>
                        <td>
                            @if (vm.Step.LineRequired)
                            {
                                <RadzenDropDown TValue="string" Data="vm.Step.Candidates"
                                                TextProperty="Display" ValueProperty="LineId"
                                                @bind-Value="vm.LineId" AllowClear="true" AllowFiltering="true"
                                                Placeholder="—" Style="width:100%" />
                            }
                            else
                            {
                                <span class="dim">@L["PP.WoRelease.NoLineProcess"]</span>
                            }
                        </td>
                        <td>
                            @if (vm.Step.LineRequired)
                            {
                                var (cls, key) = Badge(vm);
                                <span class="pill @cls">@L[key]</span>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }

    <div class="ames-modal-footer">
        <RadzenButton Text="@L["Btn.Cancel"]" ButtonStyle="ButtonStyle.Light"
                      Click="@(() => Dialog.Close(null))" />
        <RadzenButton Text="@L["PP.WorkOrder.Btn.Release"].Value" Icon="play_arrow"
                      ButtonStyle="ButtonStyle.Success" Disabled="@(!CanRelease)"
                      Click="@Submit" />
    </div>
</div>

@code {
    [Parameter] public int  WoId             { get; set; }
    [Parameter] public bool Phase0Incomplete { get; set; }

    sealed class StepVm
    {
        public required WorkOrderRepository.RoutingStepPreview Step { get; init; }
        public string? LineId { get; set; }
    }

    List<StepVm> _steps = new();
    string? _err;

    protected override void OnInitialized()
    {
        try
        {
            _steps = Wos.PreviewRouting(WoId)
                .Select(s => new StepVm { Step = s, LineId = s.BopLineId })
                .ToList();
        }
        catch (Exception ex) { _err = ex.Message; _steps = new(); }
    }

    bool CanRelease =>
        _err is null && _steps.Count > 0 &&
        _steps.All(v => !v.Step.LineRequired || !string.IsNullOrEmpty(v.LineId));

    static (string Cls, string Key) Badge(StepVm vm) =>
        vm.Step.BopLineId is null       ? ("warn", "PP.WoRelease.Badge.NoBop")
        : vm.LineId == vm.Step.BopLineId ? ("info", "PP.WoRelease.Badge.Bop")
        :                                  ("dim",  "PP.WoRelease.Badge.Changed");

    void Submit()
    {
        var steps = _steps
            .Select(v => new WorkOrderRepository.StepLineChoice(v.Step.StepSeq, v.Step.LineRequired ? v.LineId : null))
            .ToList();
        Dialog.Close(new Result(steps));
    }

    public record Result(IReadOnlyList<WorkOrderRepository.StepLineChoice> Steps);
}
```

- [ ] **Step 3: 빌드는 Task 10 이후에**

이 시점에서는 `WorkOrder.razor`·`WoRelease.razor` 가 옛 파라미터(`CurrentLineId`)를 넘겨 오류가 난다. Task 10 에서 함께 고친다.

---

### Task 10: Web — PP-04 `WorkOrder.razor` · PP-07 `WoRelease.razor`

**Files:**
- Modify: `src/06_Web/AMES.Web/Components/Pages/Pp/WorkOrder.razor`
- Modify: `src/06_Web/AMES.Web/Components/Pages/Pp/WoRelease.razor`

- [ ] **Step 1: `WorkOrder.razor` LINE 컬럼**

라인 78-80 의 `LineId` 컬럼을:

```razor
            <RadzenDataGridColumn TItem="WorkOrderDto" Property="RouteLines" Title="@L["Col.Line"].Value" Width="260px">
                <Template Context="w"><span class="code dim">@(string.IsNullOrEmpty(w.RouteLines) ? "—" : w.RouteLines)</span></Template>
            </RadzenDataGridColumn>
```

- [ ] **Step 2: 그리드에 `RowExpand` 연결**

`<RadzenDataGrid Data="_view" …` 태그에 속성 추가:

```razor
    <RadzenDataGrid Data="_view" TItem="WorkOrderDto" AllowVirtualization="true" Style="height:100%"
                    AllowSorting="true" AllowColumnResize="true" Density="Density.Compact"
                    RowExpand="@LoadSteps"
                    class="ames-rz-grid">
```

- [ ] **Step 3: 상세 템플릿의 공정 칩을 단계 행 기준으로**

라인 134-147 의 `<Template Context="w">` 안 공정 칩 블록(`<div style="display:flex;align-items:center;gap:6px;…">` 전체)을:

```razor
                <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:12px;">
                    @if (_steps.TryGetValue(w.WoId, out var steps) && steps.Count > 0)
                    {
                        for (var i = 0; i < steps.Count; i++)
                        {
                            var s   = steps[i];
                            var cls = s.Status == "Closed" ? "done" : s.Status == "In Progress" ? "cur" : "todo";
                            <span class="wo-step @cls" title="@StepTitle(s, w)">@s.ProcessCode</span>
                            @if (i < steps.Count - 1) { <span class="dim">→</span> }
                        }
                    }
                    else
                    {
                        var flow = Flow(w.RoutingType);
                        for (var i = 0; i < flow.Length; i++)
                        {
                            <span class="wo-step todo">@flow[i]</span>
                            @if (i < flow.Length - 1) { <span class="dim">→</span> }
                        }
                    }
                </div>
```

- [ ] **Step 4: `@code` 에 단계 캐시·로더 추가, Release 수정, CSV 수정**

`List<WorkOrderDto> _view = new();` 뒤에:

```csharp
    readonly Dictionary<int, List<AMES.Data.Repositories.WorkOrderRepository.StepRow>> _steps = new();

    void LoadSteps(WorkOrderDto w)
    {
        if (_steps.ContainsKey(w.WoId)) return;
        try { _steps[w.WoId] = Wos.ListSteps(w.WoId); }
        catch { _steps[w.WoId] = new(); }
    }

    static string StepTitle(AMES.Data.Repositories.WorkOrderRepository.StepRow s, WorkOrderDto w) =>
        $"{s.LineId ?? "—"} · {s.CompletedQty:#,0}/{w.OrderQty:#,0}";
```

`Reload()` 의 `_rows = Wos.ListAll(_recentDays);` 뒤에 `_steps.Clear();` 추가.

`Release(WorkOrderDto w)` 를:

```csharp
    async Task Release(WorkOrderDto w)
    {
        var result = await Dialog.OpenAsync<WoReleaseLineDialog>(
            $"{L["PP.WorkOrder.Btn.Release"].Value} · {w.WoNumber}",
            new Dictionary<string, object?>
            {
                ["WoId"]             = w.WoId,
                ["Phase0Incomplete"] = !w.Phase0Complete,
            },
            new DialogOptions { Width = "640px" });
        if (result is not WoReleaseLineDialog.Result r) return;

        try
        {
            var n = Wos.ReleaseWo(w.WoId, r.Steps, await Actor());
            if (n > 0) ShowMsg(string.Format(L["PP.WorkOrder.Ok.Released"].Value, w.WoNumber), false);
            Reload();
        }
        catch (Exception ex) { ShowMsg(ex.Message, true); }
    }
```

`ExportCsv` 의 `w.LineId ?? ""` 를 `w.RouteLines ?? ""` 로.

- [ ] **Step 5: `WoRelease.razor`**

상단 inject 에 추가:

```razor
@inject AMES.Data.Repositories.WorkOrderRepository Wos
```

라인 106 `<td class="code dim">@w.LineId</td>` 를:

```razor
                        <td class="code dim">@(w.RouteLines ?? "—")</td>
```

`DoRelease` 를:

```csharp
    async Task DoRelease(AMES.Data.Repositories.PpRepository.WoLite w)
    {
        var result = await Dialog.OpenAsync<WoReleaseLineDialog>(
            $"{L["PP.WoRelease.Btn.Release"].Value} · {w.WoNumber}",
            new Dictionary<string, object?> { ["WoId"] = w.WoId },
            new DialogOptions { Width = "640px" });
        if (result is not WoReleaseLineDialog.Result r) return;

        try
        {
            var auth  = await AuthPrvd.GetAuthenticationStateAsync();
            var actor = auth.User.Identity?.Name ?? "system";
            var n = Wos.ReleaseWo(w.WoId, r.Steps, actor);
            if (n > 0) ShowMsg(string.Format(L["PP.WoRelease.Ok.Released"].Value, w.WoNumber), false);
            Reload();
        }
        catch (Exception ex) { ShowMsg(ex.Message, true); }
    }
```

- [ ] **Step 6: Web 빌드**

```powershell
dotnet build src\06_Web\AMES.Web\AMES.Web.csproj
```

Expected: `Build succeeded.` 오류가 `WoLite.LineId` 참조로 나오면 그 화면도 `RouteLines` 로 바꾼다(`Select-String -Path src\06_Web -Pattern "\.LineId\b" -Include *.razor` 로 찾는다. `Session.LineId` 류는 무관).

- [ ] **Step 7: 화면 검증 (dev 서버)**

`preview_start` 로 Web 을 띄우고 `admin@ames.local / Dev2026!` 로그인 후:

1. `/pp/work-order` 에서 Draft WO(85725-PI000NNB, WO-20260901-001)의 Release 클릭 → 다이얼로그에 `1 INJ LINE-INJ-01 [BOP]`, `2 IMG LINE-IMG-01 [BOP]` 가 기본 선택된 것을 확인.
2. INJ 라인을 `LINE-INJ-02` 로 바꾸면 배지가 `변경`, 비우면 Release 버튼 비활성.
3. Release 후 목록 LINE 컬럼이 `LINE-INJ-02 → LINE-IMG-01`, 상태 `Released`, 행 펼침에 `INJ → IMG` 칩(둘 다 todo).
4. `/pp/wo-release` 에서 같은 WO 가 LINE 컬럼에 같은 문자열로 보이는지.
5. B 라우팅 품목 WO 를 수동 생성해 Release → QC·FG 행이 `라인 없음 · 터미널 미배정` 으로 뜨고 INJ·PNT 만 골라도 Release 되는지.

스크린샷 1장(다이얼로그)을 남긴다.

---

### Task 11: Pop — 접수 호출을 단계 ID 로

**Files:**
- Modify: `src/03_Pop/AMES.Pop/Pages/InjPopups/WoConfirmPopup.razor:75-93`

- [ ] **Step 1: `Accept()` 수정**

```csharp
    private async Task Accept()
    {
        if (Wo is null || Session is null) return;
        if (Wo.RoutingLineId is not int routingLineId) return;   // 라인 범위 조회로 받은 WO 만 접수 가능
        try
        {
            var checks = System.Text.Json.JsonSerializer.Serialize(new
            {
                mold = _checks[0], material = _checks[1], recipe = _checks[2], safety = _checks[3], phase0 = _checks[4],
                at = DateTime.Now, by = Session.EmployeeNo,
            });
            PopServices.WorkOrders.AcceptWo(routingLineId, Session.TerminalId,
                Session.OperatorId, Session.EmployeeNo, checks);
            await OnAccepted.InvokeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WoConfirmPopup Accept] {ex.Message}");
        }
    }
```

- [ ] **Step 2: 다른 호출부가 없는지 확인**

```powershell
Select-String -Path src\03_Pop -Pattern "AcceptWo\(|AddCompletedQty\(" -Include *.cs,*.razor -Recurse
```

Expected: `WoConfirmPopup.razor` 1건만.

- [ ] **Step 3: 전체 솔루션 빌드**

```powershell
dotnet build src\AMES.sln
```

Expected: `Build succeeded.` 오류 0. (MAUI 워크로드 미설치로 `AMES.Pda` 가 실패하면 그건 기존 상태이므로 무시하고 나머지 프로젝트 오류만 본다.)

- [ ] **Step 4: Pop 수동 검증**

Pop 을 실행해 `LINE-IMG-01` 로 로그인 → Task 10 에서 Release 한 WO 가 IMG 화면 WO 목록에 뜨는지, 접수 후 `GetActiveForTerminal` 로 활성 WO 가 잡히는지 확인. `LINE-INJ-02` 로 로그인해 같은 WO 가 INJ-MAIN 에 뜨는지 확인. IMG-03 에서 실적 10 입력 후 Web PP-04 의 PROGRESS 가 오르고(마지막 라인 단계), INJ 쪽 실적은 헤더 PROGRESS 를 바꾸지 않는지 확인.

---

### Task 12: 최종 검증 + 커밋

- [ ] **Step 1: 전체 테스트**

```powershell
dotnet test src\07_Etc\AMES.InjAgent.Tests\AMES.InjAgent.Tests.csproj
```

Expected: `Failed: 0`, Skipped 0(DB 연결 상태에서).

- [ ] **Step 2: 헤더 LineID 잔존 참조 최종 점검**

```powershell
Select-String -Path src -Pattern "w\.LineID|PP_WorkOrder\.LineID|CurrentLineId|AddCompletedQty|GenerateWoRouting" -Include *.cs,*.razor -Recurse | Where-Object { $_.Path -notmatch "Tests" }
```

Expected: 결과 없음.

- [ ] **Step 3: 스테이징 대상 확인**

```powershell
git status --short
```

`appsettings*.json` 은 제외하고 나머지를 스테이징:

```powershell
git add dist/migrate_wo_step_line.sql dist/AMES_Schema.sql CLAUDE.md src/01_Shared/AMES.Contracts/Dto/WorkOrderDto.cs src/02_Data/AMES.Data src/03_Pop/AMES.Pop/Pages/InjPopups/WoConfirmPopup.razor src/06_Web/AMES.Web/Components/Pages/Pp/WoReleaseLineDialog.razor src/06_Web/AMES.Web/Components/Pages/Pp/WorkOrder.razor src/06_Web/AMES.Web/Components/Pages/Pp/WoRelease.razor src/06_Web/AMES.Web/Resources/SharedResources.resx src/06_Web/AMES.Web/Resources/SharedResources.en.resx src/07_Etc/AMES.InjAgent.Tests
git status --short
```

`M src/03_Pop/AMES.Pop/appsettings*.json`, `M src/06_Web/AMES.Web/appsettings.Development.json` 이 스테이징되지 않았는지 확인.

- [ ] **Step 4: 커밋 (1건)**

```powershell
git commit -m @'
feat(pp): WO 공정 단계별 라인 배정·실적 — PP_WorkOrderRouting 정본화

- Release: BOP 스테이션 라인 기본값 + 단계별 라인 다이얼로그, 라인 필수 단계 미지정 시 차단(폴백 없음)
- 활성 라인 없는 공정(QC·FG)은 LineID NULL 단계, 헤더 CompletedQty·Closed 는 라인 있는 마지막 단계와 동기화
- Pop 조회(ListForLine/GetActiveForTerminal)·접수(AcceptWo)·실적(ConfirmByLotCode/RecordCycle) 단계 기준, BumpStepCompleted 단일 진입점
- PP_WorkOrder.LineID 쓰기 중단(컬럼 잔존), PpRepository.ReleaseWo·AddCompletedQty 삭제
- dist/migrate_wo_step_line.sql: CompletedQty·인덱스·Pending 정리·백필

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
'@
```

- [ ] **Step 5: 다른 DB 에도 마이그레이션 적용**

테스트에 쓰지 않은 쪽(LAN `192.168.2.137` 또는 원격 `98.95.142.192`)에 Task 1 Step 4 를 반복한다. 두 DB 모두 적용됐음을 최종 보고에 적는다.

---

## 자체 점검 결과

- **스펙 커버리지:** §1 데이터 모델 → Task 1·2. §2 Release → Task 3·4. §3 다이얼로그 → Task 9. §4 Pop 조회·접수·실적 → Task 5·6·7·11. §4 기타 소비자(LineSchedule·ListAllWo·OEE) → Task 8. §5 PP-04 화면 → Task 10. §6 마이그레이션 → Task 1. §7 테스트 → Task 3~7. §8 영향 파일 표와 일치.
- **타입 일관성:** `RoutingStepPreview(StepSeq, ProcessCode, BopLineId, LineRequired, Candidates)`, `LineOption(LineId, LineName).Display`, `StepLineChoice(StepSeq, LineId)`, `StepRow(RoutingLineId, StepSeq, ProcessCode, LineId, Status, CompletedQty)` 가 Task 3 정의와 Task 4·5·6·9·10 사용처에서 동일. `AcceptWo(int routingLineId, string terminalId, string operatorId, string employeeNo, string checkResultsJson)` 가 Task 6·11 동일. `BumpStepCompleted(conn, tx, routingLineId, qty, actor)` 가 Task 5·7 동일.
- **내부 공개:** `BumpStepCompleted`·`FindStepId` 는 `internal static`. 테스트 접근을 위해 Task 5 Step 2 의 `InternalsVisibleTo` 가 필요하다.
