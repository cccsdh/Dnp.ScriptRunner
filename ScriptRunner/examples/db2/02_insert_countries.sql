-- Connect to the WORLD database before running this script
-- db2 connect to WORLD

-- If not connected to the desired schema, qualify table with schema name, e.g. WORLD.Countries
-- If connected to the WORLD database but not the WORLD schema, you can use:
-- INSERT INTO WORLD.Countries (Name, Capital, Population) VALUES
INSERT INTO World.Countries (Name, Capital, Population) VALUES
('China','Beijing',1402112000),
('India','New Delhi',1366417754),
('United States','Washington, D.C.',331002651),
('Indonesia','Jakarta',273523615),
('Pakistan','Islamabad',220892340),
('Brazil','Brasilia',212559417),
('Nigeria','Abuja',206139589),
('Bangladesh','Dhaka',164689383),
('Russia','Moscow',145934462),
('Mexico','Mexico City',128932753),
('Japan','Tokyo',126476461),
('Ethiopia','Addis Ababa',114963588),
('Philippines','Manila',109581078),
('Egypt','Cairo',102334404),
('Vietnam','Hanoi',97338579),
('DR Congo','Kinshasa',89561403),
('Turkey','Ankara',84339067),
('Iran','Tehran',83992953),
('Germany','Berlin',83783942),
('Thailand','Bangkok',69799978);

COMMIT;
