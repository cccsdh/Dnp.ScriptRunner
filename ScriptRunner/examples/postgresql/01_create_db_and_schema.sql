-- PostgreSQL: create schema and table inside the existing `postgres` database
-- This script creates a schema named `world` (if it does not exist) and a
-- `Countries` table inside that schema. Run this script against the `postgres`
-- database (or another existing database) using psql. The workflow is:
-- 1) Ensure the target database exists (e.g., the cluster's `postgres` database)
-- 2) Run this script to create the `world` schema and the `Countries` table in it
--
-- Example:
-- psql -d postgres -f 01_create_db_and_schema.sql

CREATE SCHEMA IF NOT EXISTS world AUTHORIZATION current_user;

CREATE TABLE IF NOT EXISTS world.Countries (
    Id SERIAL PRIMARY KEY,
    Name TEXT NOT NULL,
    Capital TEXT NOT NULL,
    Languages JSONB NULL,
    Population BIGINT NOT NULL
);
