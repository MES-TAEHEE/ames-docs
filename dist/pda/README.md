# AMES PDA Database Scripts

## Current contract

PDA WH/FG/SP business operations execute parameterized SQL owned by the API:

- `src/02_Data/AMES.Data/Repositories/PdaWarehouseSql.cs`
- `src/02_Data/AMES.Data/Repositories/PdaFinishedGoodsSql.cs`
- `src/02_Data/AMES.Data/Repositories/PdaSparePartsSql.cs`

Inbound, cancellation, put-away, inventory, adjustment, release, outbound,
customer return and spare-part movement no longer require application stored
procedures. Other modules and SQL Server system procedures are unchanged.

`../AMES_Schema.sql` remains the fresh-install schema. PDA business procedure
definitions were removed from it and from legacy migrations so they cannot
overwrite the current API logic or recreate retired procedures.
`PDA_SCHEMA.sql` is a legacy upgrade and refuses to run after the actual
`FG_Shipment` migration; do not include it in a fresh-database rebuild.

## Retained development resets

These are still called by guarded development-only API endpoints:

- `FG_PDA_HISTORY_TEST_RESET`
- `FG_PDA_OUTBOUND_TEST_RESET`
- `FG_PDA_PPT_TEST_RESET`
- `SP_PDA_SIMPLE_TEST_RESET`
- `WH_PDA_INBOUND_SIMPLE_TEST_RESET`
- `WH_PDA_PPT_TEST_RESET`

`PDA_SEED.sql` and reset procedures may overwrite their named development test
records. Never run them against production data.

## Existing databases

Removing a definition from a source file does not drop an installed procedure.
Deploy the latest API **and web** to every instance sharing the database before
retiring old PDA business procedures. Older binaries may still call them.
Check external callers, back up the definitions and approve the exact removal
list first. Do not run a rebuild or historical migration just to remove procedures.

Source-only regression checks (no database writes):

```powershell
dotnet test src/02_Data/AMES.Data.Tests/AMES.Data.Tests.csproj --filter FullyQualifiedName~PdaDirectSqlTests
```

Database integration tests require separate approval even if they roll back.
