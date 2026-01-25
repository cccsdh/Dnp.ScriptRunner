-- IBM Db2: create database and schema
-- Note: Creating a Db2 database is done from the instance owner (CLI) rather than in a SQL script.
-- Example (run as instance owner):
-- db2 create database WORLD
-- db2 connect to WORLD
-- db2 "CREATE SCHEMA WORLD"
-- Then run the table creation below connected to the WORLD database.

CREATE TABLE Countries (
    Id INTEGER NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 INCREMENT BY 1) PRIMARY KEY,
    Name VARCHAR(200) NOT NULL,
    Capital VARCHAR(200) NOT NULL,
    Population BIGINT NOT NULL
);

COMMIT;
