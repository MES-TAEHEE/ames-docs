# AMES PDA Database Scripts

This folder contains the consolidated WH/FG database contract used by the PDA
and its related current web screens.

Use `PDA_SCHEMA.sql` for WH/FG tables, columns, indexes and procedures. Use
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

Apply the base `dist/AMES_Schema.sql` first. Both PDA scripts are rerunnable, but
`PDA_SEED.sql` resets its named test records and must not be run on production data.
