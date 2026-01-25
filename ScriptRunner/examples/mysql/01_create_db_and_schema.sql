-- MySQL/MariaDB: create database and table
-- Run as a user with privileges to create databases.

CREATE DATABASE IF NOT EXISTS `World` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `World`;

CREATE TABLE IF NOT EXISTS Countries (
  Id INT AUTO_INCREMENT PRIMARY KEY,
  Name VARCHAR(200) NOT NULL,
  Capital VARCHAR(200) NOT NULL,
  Population BIGINT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
