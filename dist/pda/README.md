# AMES PDA Database Scripts

This folder contains database scripts owned by the PDA application.

Use this folder for PDA-specific tables, procedures, and demo data instead of
creating one SQL file per PDA screen. Screen numbers can change, but the PDA
database contract should stay named by business workflow.

## Naming

- Procedures: use the main AMES Warehouse procedure style, `dbo.WH_PDA_<Workflow>_<Action>`.
- Tables: use the main AMES Warehouse table style, `dbo.WH_<Entity>`.
- Avoid screen-number-based procedure names for new work.

Examples:

- `dbo.WH_PDA_SCHEDULE_INBOUND_LIST`
- `dbo.WH_PDA_SCHEDULE_RELEASE_LIST`
- `dbo.WH_PDA_INBOUND_SCAN_LOT`
- `dbo.WH_PDA_INBOUND_RECEIVE_LOT`
- `dbo.WH_PDA_INBOUND_MOVE_LOCATION`
- `dbo.WH_PDA_INBOUND_CANCEL_RECEIPT`
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
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_schedule.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_demo_data.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_inbound.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_inbound_demo_data.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\migrate_pda_wh_release.sql
sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_release_demo_data.sql
```

The scripts should be idempotent where possible so they can be applied again
on a local Docker database or a shared development database.
