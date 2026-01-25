-- SQLite: create database and table
-- For SQLite the database is a file. Create/open with sqlite3 World.db

CREATE TABLE IF NOT EXISTS Countries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Capital TEXT NOT NULL,
    Population INTEGER NOT NULL
);
