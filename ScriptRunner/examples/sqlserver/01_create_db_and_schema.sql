-- SQL Server: create database and schema
-- Requires sufficient permissions. Run with sqlcmd or SSMS as a user that can create databases.

CREATE DATABASE [World];
GO

USE [World];
GO

CREATE TABLE Countries (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    Capital NVARCHAR(200) NOT NULL,
    Languages NVARCHAR(MAX) NULL,
    Population BIGINT NOT NULL
);
GO
