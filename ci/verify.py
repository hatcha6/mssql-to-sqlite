"""Checks a SQLite export of ci/smoke-schema.sql value by value."""

import sqlite3
import sys

db = sqlite3.connect(sys.argv[1])
failures = []


def check(label, actual, expected):
    if actual != expected:
        failures.append(f"{label}: expected {expected!r}, got {actual!r}")


def one(sql, *args):
    return db.execute(sql, args).fetchone()


check("integrity", one("PRAGMA integrity_check")[0], "ok")
check("journal mode", one("PRAGMA journal_mode")[0], "delete")

tables = sorted(r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type = 'table'"))
check("tables", tables, sorted(["BULK_ROWS", "CAR_PART", "ORDERS", 'Odd "Name', "_export_info", "_export_tables", "sales_ORDERS"]))

columns = {r[1]: (r[2], r[3]) for r in db.execute('PRAGMA table_info("CAR_PART")')}
check("ID type", columns["ID"], ("INTEGER", 1))
check("CODE type", columns["CODE"], ("TEXT", 1))
check("PRICE type", columns["PRICE"], ("NUMERIC", 1))
check("RATIO type", columns["RATIO"], ("REAL", 0))
check("PIC type", columns["PIC"], ("BLOB", 0))
check("ANYTHING type", columns["ANYTHING"], ("", 0))
check("CREATED type", columns["CREATED"], ("TEXT", 0))

db.row_factory = sqlite3.Row
a = db.execute("SELECT * FROM CAR_PART WHERE ID = 1").fetchone()
expected_a = {
    "CODE": "A1",
    "NAME_A": "فلتر زيت",
    "NAME_N": "فلتر زيت تويوتا",
    "PRICE": 12.5,
    "COST": 10.25,
    "RATIO": 0.1,
    "WEIGHT": 1.1,
    "ACTIVE": 1,
    "CREATED": "2024-03-05 14:30:15.123",
    "SHORT": "2024-03-05 14:30:00",
    "UID": "A0B1C2D3-E4F5-4678-9ABC-DEF012345678",
    "PIC": b"\x00\xff\x10",
    "OLD_PIC": b"\x01\x02",
    "NOTE": "ملاحظة",
    "NOTE_N": "ملاحظة طويلة",
    "ANYTHING": 42,
    "ONLY_DATE": "2024-03-05",
    "ONLY_TIME": "08:15:30.5",
    "OFFSET": "2024-03-05 14:30:15.123456+02:00",
    "TINY": 255,
    "HUGE": 9007199254740993,
    "TOTAL": 25,
}
for key, value in expected_a.items():
    check(f"A1.{key}", a[key], value)
check("A1.BIG", round(a["BIG"] / 1e27, 12), 1.234567890123)
check("A1.STAMP length", len(a["STAMP"]), 8)

b = db.execute("SELECT * FROM CAR_PART WHERE ID = 2").fetchone()
check("B2.CODE", b["CODE"], "B2")
check("B2.NAME_A", b["NAME_A"], None)
check("B2.PRICE", b["PRICE"], 0)
check("B2.ACTIVE", b["ACTIVE"], 0)
check("B2.ANYTHING", b["ANYTHING"], "نص")

check("ORDERS", [tuple(r) for r in db.execute("SELECT * FROM ORDERS")], [(1, "زبون نقدي")])
check("sales_ORDERS", [tuple(r) for r in db.execute("SELECT * FROM sales_ORDERS ORDER BY LINE")], [(1, 1, 2.5), (1, 2, 1)])
check("odd table", [tuple(r) for r in db.execute('SELECT "weird] col", "space col" FROM "Odd ""Name"')], [(7, "x")])
check("bulk count", one("SELECT COUNT(*) FROM BULK_ROWS")[0], 120000)
check("bulk last", one("SELECT LABEL FROM BULK_ROWS WHERE N = 120000")[0], "صف 120000")

indexes = {r[0]: r[1] for r in db.execute("SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL")}
for name, unique in [("CAR_PART__pk", True), ("CAR_PART__UX_CAR_PART_CODE", True), ("CAR_PART__IX_CAR_PART_NAME", False), ("sales_ORDERS__pk", True)]:
    sql = indexes.get(name)
    if sql is None:
        failures.append(f"missing index {name}")
    else:
        check(f"{name} unique", sql.startswith("CREATE UNIQUE"), unique)

info = {r[0]: r[1] for r in db.execute('SELECT key, value FROM "_export_info"')}
check("info.tool", info.get("tool"), "MssqlToSqlite")
check("info.tables", info.get("tables"), "5")
check("info.rows", info.get("rows"), str(120000 + 2 + 1 + 2 + 1))

if failures:
    print("\n".join(failures))
    sys.exit(1)
print(f"verified {sys.argv[1]}")
