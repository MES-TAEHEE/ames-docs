# A-MES Solution

C# / .NET 9 source tree for the **A-MES (Automotive Manufacturing Execution System)** built for SEOYON E-HWA per the design docs in `../volumes/` (VOL01–VOL13).

This skeleton verifies the end-to-end stack (WinForms → DTO → ADO.NET repository → SQL Server) before screen development begins.

---

## Solution layout

```
src/
├── AMES.sln                              ← VS 2022 / dotnet CLI entry point
│
├── 01_Shared/
│   └── AMES.Contracts/                   ← Pure DTOs + enums. No I/O, no deps.
│       ├── Dto/ItemDto.cs                  (Sample: MD_Item)
│       └── Enums/ItemType.cs               (RAW / FABRIC / POWDER / PAINT / SEMI / FINISHED)
│
├── 02_Data/
│   └── AMES.Data/                        ← SP wrappers. Single owner of SqlConnection.
│       ├── Connection/AmesConnectionFactory.cs
│       └── Repositories/ItemRepository.cs   (GetAll / CountActive — sample)
│
└── 03_Pop/
    └── AMES.Pop/                         ← WinForms shop-floor terminal.
        ├── Program.cs                      (entry point, launches MainForm)
        ├── Common/AppConfig.cs              (appsettings.json singleton loader)
        ├── Forms/MainForm.cs                (placeholder — DB smoke test button)
        └── appsettings.json                 (connection string + station config)
```

### Project graph

```
AMES.Pop ──► AMES.Data ──► AMES.Contracts
   │              │
   └──────────────┴──► AMES.Contracts
```

- **AMES.Contracts** has zero dependencies → safe to share with future MAUI PDA and Blazor Web projects.
- **AMES.Data** is the **only** layer that knows about SQL — per VOL01 Tech Stack, all DB access goes through stored procedures (currently plain `SELECT` until SPs exist) invoked via ADO.NET.
- **AMES.Pop** is the WinForms presentation layer. It reads configuration once at startup, opens a connection through `AmesConnectionFactory`, and consumes repository methods.

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 9.0+ |
| Visual Studio 2022 | 17.10+ (with **.NET desktop development** workload) |
| SQL Server | 2022 / 2025 (local instance `localhost`, DB `AMES_DEV`) |
| Login | `ames_app` (Mixed Mode auth) |

> The full DDL (149 tables) is in `../dist/AMES_Schema.sql`. Apply it to `AMES_DEV` before running.

---

## Build & run

```powershell
# from repo root
dotnet build src\AMES.sln
dotnet run --project src\03_Pop\AMES.Pop\AMES.Pop.csproj
```

Or open `src\AMES.sln` in Visual Studio 2022, set **AMES.Pop** as startup, F5.

When the form opens, click **▶ TEST DB CONNECTION**. Expected output:

```
✓ Connection OK (NN ms)
  MD_Item.ActiveFlag=1 → N rows
```

---

## Configuration

`src/03_Pop/AMES.Pop/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "AMES": "Server=localhost;Database=AMES_DEV;User Id=ames_app;Password=...;TrustServerCertificate=True;Encrypt=True;Connect Timeout=5;"
  },
  "PopTerminal": {
    "StationId":    "POP-DEV-01",
    "LineId":       "LINE-INJ-01",
    "DefaultShift": "DAY"
  }
}
```

- File is copied to the output directory on build (`CopyToOutputDirectory=PreserveNewest`).
- `appsettings.Development.json` overrides are supported (optional).
- **Dev only** — production will use a config secret or Windows Auth + DPAPI.

---

## Coding conventions (per VOL01)

1. **Stored Procedure first.** Every new repository method should map to a `dbo.SP_*` call. Inline SQL in `ItemRepository` is a temporary scaffold.
2. **DTO = init-only properties.** Use `required` for natural keys.
3. **Connection lifetime = single method.** `using var conn = ...` inside each repository call; never store an open connection in a field.
4. **No business logic in WinForms.** Forms call repositories; complex flows live in a future `AMES.Application` service layer.

---

## Next steps

| Order | Project | Task |
|-------|---------|------|
| 1 | AMES.Pop | Build INJ-01 POP Login screen, then INJ-02 Lot Start, … (one screen per session). |
| 2 | _AMES.Pda_ (new) | MAUI Android project for handheld scanners. |
| 3 | _AMES.Web_ (new) | Blazor Server office portal. |
| – | AMES.Data | Replace inline SELECTs with `SP_*` calls as DBA delivers stored procedures. |
