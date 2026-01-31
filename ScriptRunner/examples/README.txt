Examples directory

This folder contains example SQL scripts for creating a simple database called "World" with a single table "Countries" and inserting 20 country rows.

Structure:

examples/
  sqlserver/(Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    sqlserver_Scripts.txt
  postgresql/(Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    postgresql_Scripts.txt
  sqlite/(Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    sqlite_Scripts.txt
  mysql/(Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    mysql_Scripts.txt
  oracle/(Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    oracle_Scripts.txt
  db2/ (Verified)
    01_create_db_and_schema.sql
    02_insert_countries.sql
    db2_Scripts.txt


Instructions:

Script_Runner will show 3 selection options when the example\{provider} folder is selected:
[] 01_create_db_and_schema.sql
[] 02_insert_countries.sql
[] {provider}_Scripts.txt

you can choose to run individual scripts or the _Scripts.txt file to execute all scripts in order.

Notes:
- For providers that cannot create databases within a script or require special privileges, create the database manually before running the insert scripts.

Embedded file markers for large values

This runner supports embedding file contents into INSERT statements using configurable delimiters. The default delimiters were chosen to be unlikely to collide with application text: they start with the characters `DnP`.

Default markers (open -> close -> type):
- `<DnPJson>` ... `</DnPJson>` -> json (the file content is parsed and normalized as JSON)
- `<DnPXml>`  ... `</DnPXml>`  -> xml  (the file content is parsed/validated as XML)
- `<DnPTxt>`  ... `</DnPTxt>`  -> txt  (the file content is used as-is, trimmed)

Example usage in a .sql file (MySQL example):

-- Place `China.json` next to the SQL script and reference it:
--
-- INSERT INTO `World`.`Countries` (Name, Capital, Languages, Population) VALUES
-- ('China','Beijing', <DnPTxt>../languages/China.json</DnPTxt>,1402112000),
-- ('India','New Delhi', <DnPTxt>../languages/India.json</DnPTxt>,1366417754),
-- ('United States','Washington, D.C.', <DnPTxt>../languages/United States.json</DnPTxt>,331002651),
-- ('Indonesia','Jakarta', <DnPTxt>../languages/Indonesia.json</DnPTxt>,273523615),
-- ('Pakistan','Islamabad', <DnPTxt>../languages/Pakistan.json</DnPTxt>,220892340),
-- ('Brazil','Brasilia', <DnPTxt>../languages/Brazil.json</DnPTxt>,212559417),
-- ('Nigeria','Abuja', <DnPTxt>../languages/Nigeria.json</DnPTxt>,206139589),
-- ('Bangladesh','Dhaka', <DnPTxt>../languages/Bangladesh.json</DnPTxt>,164689383),
-- ('Russia','Moscow', <DnPTxt>../languages/Russia.json</DnPTxt>,145934462),
-- ('Mexico','Mexico City', <DnPTxt>../languages/Mexico.json</DnPTxt>,128932753),
-- ('Japan','Tokyo', <DnPTxt>../languages/Japan.json</DnPTxt>,126476461),
-- ('Ethiopia','Addis Ababa', <DnPTxt>../languages/Ethiopia.json</DnPTxt>,114963588),
-- ('Philippines','Manila', <DnPTxt>../languages/Philippines.json</DnPTxt>,109581078),
-- ('Egypt','Cairo', <DnPTxt>../languages/Egypt.json</DnPTxt>,102334404),
-- ('Vietnam','Hanoi', <DnPTxt>../languages/Vietnam.json</DnPTxt>,97338579),
-- ('DR Congo','Kinshasa', <DnPTxt>../languages/DR Congo.json</DnPTxt>,89561403),
-- ('Turkey','Ankara', <DnPTxt>../languages/Turkey.json</DnPTxt>,84339067),
-- ('Iran','Tehran', <DnPTxt>../languages/Iran.json</DnPTxt>,83992953),
-- ('Germany','Berlin', <DnPTxt>../languages/Germany.json</DnPTxt>,83783942),
-- ('Thailand','Bangkok', <DnPTxt>../languages/Thailand.json</DnPTxt>,69799978);

Relative paths and parent directory references

- The runner resolves embedded file paths relative to the SQL file being executed. You may use `../` style paths to reference files in sibling folders (for example `../languages/China.json`).

Type detection for `<DnPTxt>` markers

- When the open/close marker maps to `txt` (by default `<DnPTxt>`), the runner can optionally try to detect whether the referenced file actually contains JSON or XML; when detected it will run the corresponding JSON/XML sanitization logic (validate and normalize) before inlining.
- This detection is enabled by default but can be turned off in settings (see below). When disabled, the runner treats the file content strictly as text (trimmed) and inlines it as a SQL literal.

Settings (script-runner-settings.json)

You can customize embedded markers and detection via a `script-runner-settings.json` file placed in the application folder. Example settings:

{
  "EmbeddedFileMarkers": {
    "<DnPJson>": "json",
    "</DnPJson>": "json_close",
    "<DnPXml>": "xml",
    "</DnPXml>": "xml_close",
    "<DnPTxt>": "txt",
    "</DnPTxt>": "txt_close"
  },
  "EnableEmbeddedTypeDetection": true
}

- `EmbeddedFileMarkers` lets you change the token names (open tag -> type). If omitted, the defaults above are used.
- `EnableEmbeddedTypeDetection` (boolean): when `true` the runner will attempt to detect JSON or XML inside `<DnPTxt>`-style files and process accordingly. Set to `false` to force raw text handling.

Behavior notes

- When JSON/XML detection causes parsing errors, the runner logs an error and skips the upsert for that statement.
- For JSON columns (Postgres `jsonb`, MySQL `JSON`) the runner normalizes the JSON before inlining; for text/clob columns the JSON string is inserted as-is (but validated if detection/marker indicates JSON).

Scripts index files:

Each provider folder contains a `_Scripts.txt` file (for example `sqlserver_Scripts.txt`) that lists the provider's SQL files in the intended execution order. The files contain one relative path per line. These index files are intended for automation or for manually iterating and executing each script with the appropriate database client.

.txt file Notes:

.\Script1.sql   - refers to Script1.sql in the current folder
.\folder\Script2.sql  - refers to Script2.sql in the subfolder "folder"
>>.\MoreScripts.txt  - refers to another index file "MoreScripts.txt" in the current folder, allowing nested script execution.

Results:
After executing Script_Runner with the provided example scripts, you will have a "World" database populated with a "Countries" table containing 20 country records, and a results log file documenting the execution in the script folder.
