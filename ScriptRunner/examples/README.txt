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
  db2/ (eample has not been verified)
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

Scripts index files:

Each provider folder contains a `_Scripts.txt` file (for example `sqlserver_Scripts.txt`) that lists the provider's SQL files in the intended execution order. The files contain one relative path per line. These index files are intended for automation or for manually iterating and executing each script with the appropriate database client.

.txt file Notes:

.\Script1.sql   - refers to Script1.sql in the current folder
.\folder\Script2.sql  - refers to Script2.sql in the subfolder "folder"
>>.\MoreScripts.txt  - refers to another index file "MoreScripts.txt" in the current folder, allowing nested script execution.

Results:
After executing Script_Runner with the provided example scripts, you will have a "World" database populated with a "Countries" table containing 20 country records, and a results log file documenting the execution in the script folder.
