-- IBM Db2: create database and schema
-- Note: Creating a Db2 database is normally done from the instance owner (CLI) rather than in a SQL script.
-- Example (run as instance owner):
-- db2 create database WORLD
-- db2 connect to WORLD
-- db2 "CREATE SCHEMA WORLD"
-- After connecting to the WORLD database you can set the current schema in the SQL script itself
-- so subsequent unqualified object names are created in that schema without qualifying the name.
-- NOTE:  The connection string user must have the necessary privileges to create a database and schema.

-- Connect to the WORLD database first (run in CLI):
-- db2 connect to WORLD

-- Set the current schema for this session so the following CREATE TABLE will create the table
-- in the WORLD schema without qualifying the name.
SET CURRENT SCHEMA = 'WORLD';

CREATE TABLE COUNTRIES (
    ID INTEGER NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 INCREMENT BY 1) PRIMARY KEY,
    NAME VARCHAR(200) NOT NULL,
    CAPITAL VARCHAR(200) NOT NULL,
    LANGUAGES CLOB NULL,
    POPULATION BIGINT NOT NULL
);

