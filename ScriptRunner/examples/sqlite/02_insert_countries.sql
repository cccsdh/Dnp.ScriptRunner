-- SQLite does not support multiple schemas in the same way as other RDBMS.
-- Use the main database file; table is assumed to be in the main schema.
INSERT INTO Countries (Name, Capital, Languages, Population) VALUES
('China','Beijing', <DnPTxt>../languages/China.json</DnPTxt>,1402112000),
('India','New Delhi', <DnPTxt>../languages/India.json</DnPTxt>,1366417754),
('United States','Washington, D.C.', <DnPTxt>../languages/United States.json</DnPTxt>,331002651),
('Indonesia','Jakarta', <DnPTxt>../languages/Indonesia.json</DnPTxt>,273523615),
('Pakistan','Islamabad', <DnPTxt>../languages/Pakistan.json</DnPTxt>,220892340),
('Brazil','Brasilia', <DnPTxt>../languages/Brazil.json</DnPTxt>,212559417),
('Nigeria','Abuja', <DnPTxt>../languages/Nigeria.json</DnPTxt>,206139589),
('Bangladesh','Dhaka', <DnPTxt>../languages/Bangladesh.json</DnPTxt>,164689383),
('Russia','Moscow', <DnPTxt>../languages/Russia.json</DnPTxt>,145934462),
('Mexico','Mexico City', <DnPTxt>../languages/Mexico.json</DnPTxt>,128932753),
('Japan','Tokyo', <DnPTxt>../languages/Japan.json</DnPTxt>,126476461),
('Ethiopia','Addis Ababa', <DnPTxt>../languages/Ethiopia.json</DnPTxt>,114963588),
('Philippines','Manila', <DnPTxt>../languages/Philippines.json</DnPTxt>,109581078),
('Egypt','Cairo', <DnPTxt>../languages/Egypt.json</DnPTxt>,102334404),
('Vietnam','Hanoi', <DnPTxt>../languages/Vietnam.json</DnPTxt>,97338579),
('DR Congo','Kinshasa', <DnPTxt>../languages/DR Congo.json</DnPTxt>,89561403),
('Turkey','Ankara', <DnPTxt>../languages/Turkey.json</DnPTxt>,84339067),
('Iran','Tehran', <DnPTxt>../languages/Iran.json</DnPTxt>,83992953),
('Germany','Berlin', <DnPTxt>../languages/Germany.json</DnPTxt>,83783942),
('Thailand','Bangkok', <DnPTxt>../languages/Thailand.json</DnPTxt>,69799978);
