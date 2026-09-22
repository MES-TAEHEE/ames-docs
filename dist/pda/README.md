# AMES PDA Database Scripts

This folder contains the consolidated WH/FG database contract used by the PDA
and its related current web screens.

For fresh databases, `../AMES_Schema.sql` includes the deployed WH/FG/SP tables,
columns, indexes and procedures as of 2026-09-23, including the inbound/release
schedule procedures. `PDA_SCHEMA.sql` remains a legacy incremental upgrade
script; it is no longer a separate step in the fresh-database rebuild. Use
`PDA_SEED.sql` for rerunnable development data and current menu cleanup. Do not
create separate migration or seed files per screen.

## Naming

- Procedures: use the main AMES Warehouse procedure style, `dbo.WH_PDA_<Workflow>_<Action>`.
- Tables: use the main AMES Warehouse table style, `dbo.WH_<Entity>`.
- Avoid screen-number-based procedure names for new work.

Examples:

- `dbo.WH_PDA_INBOUND_SCAN_LOT`
- `dbo.WH_PDA_INBOUND_RECEIVE_LOT`
- `dbo.WH_PDA_INBOUND_MOVE_LOCATION`
- `dbo.WH_PDA_INBOUND_CANCEL_RECEIPT`
- `dbo.WH_PDA_INVENTORY_STATUS_LIST`
- `dbo.WH_PDA_INVENTORY_LOCATION_LIST`
- `dbo.WH_PDA_INVENTORY_SCAN_LOOKUP`
- `dbo.WH_PDA_RELEASE_SLIP_STATUS`
- `dbo.WH_PDA_RELEASE_PICK_LINES`
- `dbo.WH_PDA_RELEASE_SCAN_LOT`
- `dbo.WH_PDA_RELEASE_PICK_LOT`
- `dbo.WH_PurchaseOrder`
- `dbo.WH_ReleaseSchedule`
- `dbo.WH_Receiving`
- `dbo.WH_Inventory`
- `dbo.WH_ReleasePicking`

## Apply

```powershell
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -b -d AMES_DEV -i dist\pda\PDA_SCHEMA.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -b -d AMES_DEV -i dist\pda\PDA_SEED.sql
```

The schema command above is for older databases only; skip it after the current
`dist/AMES_Schema.sql`. Keep future fresh-install schema changes in that file.
Both PDA scripts are rerunnable, but
`PDA_SEED.sql` resets its named test records and must not be run on production data.
