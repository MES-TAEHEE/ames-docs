# Warehouse Sample Data

These scripts reproduce the PDA examples in a separate development database.
They are not production data and are not required when real operational data exists.
Committing a SQL file does not apply it to another developer's database.

## Before Running

- Apply the project's base schema and master-data setup first, including the part master.
- Apply the base `AMES_Schema.sql`, then `dist/pda/PDA_SCHEMA.sql`.
- Use an explicit server/database and set `SQLCMDPASSWORD` outside source control.
- Do not run `dist/rebuild_db.sh` to update an existing database: it drops and recreates it.

## Sample Scripts

Run the following only against a disposable development database, in this order.

| Script | Data and screens |
| --- | --- |
| `dist/pda/PDA_SEED.sql` | Consolidated WH/FG PDA test data for Inbound, Inventory, Adjust, Release, Put-Away, QC, Loading and Customer Return. |

**Reset warning:** the seed replaces named sample records and updates matching
location definitions. It deletes and recreates sample LOTs, inventory and allocations.
Re-running it can invalidate
previous tests and their history links. Do not run them against operational/shared
data without a backup and agreement from its users.

```powershell
# SQLCMDPASSWORD must already be set in this terminal.
sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\PDA_SEED.sql
```

## Barcode Examples

| Use | Barcode |
| --- | --- |
| Test-mode login | `TEST` / `0000` |
| Supervisor PIN | `admin` / `1234` |
| Local Delivery Note | `5011202608280001` |
| Local boxes | `5011LL260828000001`, `5011LL260828000002`, `5011LL260828000003` |
| Resettable LOCAL test document | `5011202609039001` |
| Resettable LOCAL test Box/LOT | `5011LL260903900001` |
| CKD Case | `CKD202608280001CASE00001` |
| CKD boxes | `CKD260828000000001`, `CKD260828000000002`, `CKD260828000000003` |
| Receiving location | `WH010201` |
| Blocked inbound location | `WH019901` |
| Capacity-exceeded inbound location (capacity 50) | `WH019902` |
| Inventory/Adjust LOT | `5011LL260804000001` |
| Zero-stock Adjust LOT | `5011LL260901000099` |
| Inventory part/location | `81710-PI000NNB`, `B0-09-D2` |
| Release Pick Slip | `2026082801` |
| Release FIFO order | `5011LL260701000001`, `5011LL260715000002`, `5011LL260801000003` |
| Release second-part / standalone LOT | `5011LL260820000010` |
| Unknown barcode | `WH-UNKNOWN-999999` |

Inbound starts without a selected receive type so the `Select LOCAL or CKD`
validation can be tested. Transactions includes repeatable recent IN/OUT/ADJ rows
created by `pda-scenario-seed`; normal Receive, Release and Adjust actions append
their actual transaction history.
