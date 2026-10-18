# MssqlToSqlite — تصدير بيانات SQL Server

A small Windows tool that copies a shop's old **SQL Server 2005+** database
into a single **SQLite** file. You can then upload that file in دفتر (Pointy)
through *ترحيل البيانات*. Pointy's importer only reads Access and SQLite files,
and it refuses `.bak` and `.mdf` because it can't open them without a running
SQL Server. This tool is the bridge: run it on the shop PC, where SQL Server is
already installed.

It's a separate repo from Pointy on purpose. It is maintained lightly, so keep
changes small.

## Using it

1. Download the latest `MssqlToSqlite-x.y.z.zip` from **Releases** and unzip it
   anywhere. Keep the `x86`/`x64` folders next to the exe.
2. Run `MssqlToSqlite.exe` and pick a source:
   - **خادم**: a database on a running server (the local instances are listed
     automatically; *بحث في الشبكة* browses the LAN).
   - **نسخة ‎.bak**: a backup file. It is restored under a temporary name,
     exported, then dropped.
   - **ملف ‎.mdf**: a detached data file. A *copy* is attached, so the
     original is never touched. It is detached and deleted afterwards.
3. Press **اتصال**, untick any tables you don't want, choose where to save, and
   press **تصدير**.

Close the old POS before exporting so the data doesn't change mid-copy.
Windows authentication works on most shop PCs, because the local administrator
is usually sysadmin on SQL Server 2005 Express.

Requirements: Windows 7 SP1 or newer with .NET Framework 4.0+ (built into
Windows 8 and later). For `.bak`/`.mdf`, the server must be on the same PC, and
the account needs permission to restore or attach databases. A `.bak` restores
only onto a server of the same or a newer version, and SQL Server 2016+ cannot
restore 2005 backups, so use the shop's own server.

## What the output looks like

| SQL Server | SQLite | Value |
|---|---|---|
| int, bigint, smallint, tinyint, bit | INTEGER | as is; bit → 0/1 |
| decimal, numeric, money, smallmoney | NUMERIC | exact text, which SQLite keeps as a number (values with more than 15 significant digits get rounded and logged as a warning) |
| float, real | REAL | real values are rounded to their shortest form (1.1, not 1.100000023…) |
| datetime, smalldatetime | TEXT | `yyyy-MM-dd HH:mm:ss[.fff]`, to the millisecond as SQL Server shows it |
| datetime2, date, time, datetimeoffset | TEXT | ISO, up to microseconds |
| char, nchar | TEXT | trailing padding removed (option) |
| varchar, nvarchar, text, ntext, xml | TEXT | Arabic code pages decoded correctly |
| uniqueidentifier | TEXT | upper-case, like SSMS |
| binary, varbinary, image, rowversion | BLOB | |
| sql_variant | (none) | each value keeps its own type |

- `dbo` tables keep their names, because Pointy's connectors look tables up by
  name. Other schemas become `schema_table`.
- Primary keys and indexes are recreated as indexes after the data is loaded.
- Two extra tables record what was exported: `_export_info` (tool version,
  server version, time) and `_export_tables` (row count per table).
- The file is written as `name.db.partial` and renamed only once everything
  succeeded, so a cancelled or failed run never leaves a half-finished file.

## Command line

Any export flag runs it headless (CI uses this):

```
MssqlToSqlite.exe --server .\SQLEXPRESS --database Shop --out C:\export\shop.db
MssqlToSqlite.exe --server .\SQLEXPRESS --bak D:\shop.bak --out shop.db --log export.log
MssqlToSqlite.exe --server . --list
MssqlToSqlite.exe --help
```

## Development

- Targets .NET Framework 4.0 so it runs on the oldest shop PCs. It builds with
  the .NET 8 SDK on any OS (`dotnet build src/MssqlToSqlite -c Release`), but it
  only runs on Windows.
- The UI follows Pointy's design language: its colour tokens, radii and the
  IBM Plex Sans Arabic font (embedded) are copied from
  `frontend/lib/src/shared/design/`. It is Arabic, right-to-left, and every
  string lives in `Core/Text.cs`.
- `MssqlToSqlite.exe --screenshot out.png --demo idle|server|export|file`
  renders a fake state and saves a screenshot, the same idea as Pointy's
  preview harness.
- CI (`.github/workflows/ci.yml`) builds on Windows and runs
  `ci/smoke-test.ps1` against SQL Server LocalDB. It exports a database that
  covers every type three ways (live, `.bak`, `.mdf`) and checks each value
  with `ci/verify.py`. It also uploads the UI screenshots as an artifact.

## Releasing

```
git tag v1.0.1 && git push origin v1.0.1
```

The tag builds with that version, runs the smoke test, and publishes
`MssqlToSqlite-1.0.1.zip` as a GitHub release.
