# Warehouse Sample Data

These scripts reproduce the PDA examples in a separate development database.
They are not production data and are not required when real operational data exists.
Committing a SQL file does not apply it to another developer's database.

## Before Running

- Apply the project's base schema and master-data setup first, including the part master.
- Apply `migrate_pda_wh_schedule.sql`, `migrate_pda_wh_inbound.sql`,
  `migrate_pda_wh_inventory.sql`, `migrate_pda_wh_release.sql`,
  `migrate_pda_wh_adjust.sql`, and `migrate_pda_wh_transactions.sql` from this folder.
- The legacy location sample also requires the warehouse/area master tables and
  the Pick Slip schema from `dist/migrate_wh_picking_slip.sql`.
- Use an explicit server/database and set `SQLCMDPASSWORD` outside source control.
- Do not run `dist/rebuild_db.sh` to update an existing database: it drops and recreates it.

## Sample Scripts

Run the following only against a disposable development database, in this order.

| Script | Data and screens |
| --- | --- |
| `dist/pda/seed_pda_wh_inbound_demo_data.sql` | Local/CKD POs, Delivery Note, Case, boxes, LOTs, and receiving locations for Inbound. |
| `dist/seed_wh_legacy_location_pick_data.sql` | Wiley W/H, B0 rack positions, multiple material LOTs and Pick Slip examples for Inventory, Adjust and location views. Requires existing parts `81710-PI000NNB`, `81710-PI000YGN`, `82301-PI000NNB`, `82301-PI000YGU`. |
| `dist/seed_pda_release_scan_test.sql` | Pick Slip `2026082801`, three FIFO LOTs and a standalone outgoing LOT for Release. |

**Reset warning:** the location script replaces earlier sample records and updates
the matching location definitions. The release script deletes and recreates its
sample LOTs, inventory and Pick Slip allocations. Re-running either can invalidate
previous tests and their history links. Do not run them against operational/shared
data without a backup and agreement from its users.

```powershell
# SQLCMDPASSWORD must already be set in this terminal.
sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\seed_pda_wh_inbound_demo_data.sql
sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\seed_wh_legacy_location_pick_data.sql
sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\seed_pda_release_scan_test.sql
```

## Barcode Examples

| Use | Barcode |
| --- | --- |
| Local Delivery Note | `5011202608280001` |
| Local boxes | `5011LL260828000001`, `5011LL260828000002`, `5011LL260828000003` |
| CKD Case | `CKD202608280001CASE00001` |
| CKD boxes | `CKD260828000000001`, `CKD260828000000002`, `CKD260828000000003` |
| Receiving location | `WH010201` |
| Inventory/Adjust LOT | `5011LL260804000001` |
| Inventory part/location | `81710-PI000NNB`, `B0-09-D2` |
| Release Pick Slip | `2026082801` |
| Release FIFO order | `5011LL260701000001`, `5011LL260715000002`, `5011LL260801000003` |
| Standalone outgoing LOT | `5011LL260820000010` |

Schedule reads released/in-progress `PP_WorkOrder` records, not these Pick Slips.
Use released work orders from the production-planning demo setup for Schedule.
Transactions shows actual saved IN/OUT/ADJ movements; perform Receive, Release or
Adjust after seeding to create history. A seeded LOT alone does not guarantee a
transaction record.
