"""Asserts the exact table list of a SQLite file: tables.py <db> <name>..."""

import sqlite3
import sys

names = sorted(r[0] for r in sqlite3.connect(sys.argv[1]).execute("SELECT name FROM sqlite_master WHERE type = 'table'"))
expected = sorted(sys.argv[2:])
print(names)
sys.exit(0 if names == expected else 1)
