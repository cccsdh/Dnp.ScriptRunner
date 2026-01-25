-- Connect as the target schema/user (for example: CONNECT world/YourStrongPassword@your_tns)

-- Use fully qualified schema.tablename if not connected as the target schema/user, for example: WORLD.Countries
-- If connected as the schema user (for example "world"), the unqualified table name will be used. The block below will
-- detect the schema owner of the COUNTRIES table (if it exists) and insert example rows if they are not already present.

DECLARE
    cnt NUMBER;
    tbl_cnt NUMBER;
    v_owner VARCHAR2(128) := USER; -- runtime schema owner to qualify objects
    v_owner_up VARCHAR2(128) := UPPER(v_owner);
BEGIN
    -- If the COUNTRIES table exists in any schema, prefer that schema as the target owner
    BEGIN
        SELECT owner INTO v_owner_up FROM all_tables WHERE table_name = 'COUNTRIES' AND ROWNUM = 1;
        v_owner := v_owner_up; -- owner values in ALL_TABLES are uppercase
    EXCEPTION
        WHEN NO_DATA_FOUND THEN
            -- keep current USER as owner
            v_owner := USER;
            v_owner_up := UPPER(v_owner);
    END;

    -- Countries: check presence of table in the chosen schema
    SELECT COUNT(*) INTO tbl_cnt FROM all_tables WHERE owner = v_owner_up AND table_name = 'COUNTRIES';
    IF tbl_cnt > 0 THEN
        -- iterate country rows and insert if missing (schema-qualified)
        FOR c IN (
            SELECT 'China' AS name, 'Beijing' AS capital, 1402112000 AS population FROM dual
            UNION ALL SELECT 'India', 'New Delhi', 1366417754 FROM dual
            UNION ALL SELECT 'United States', 'Washington, D.C.', 331002651 FROM dual
            UNION ALL SELECT 'Indonesia', 'Jakarta', 273523615 FROM dual
            UNION ALL SELECT 'Pakistan', 'Islamabad', 220892340 FROM dual
            UNION ALL SELECT 'Brazil', 'Brasilia', 212559417 FROM dual
            UNION ALL SELECT 'Nigeria', 'Abuja', 206139589 FROM dual
            UNION ALL SELECT 'Bangladesh', 'Dhaka', 164689383 FROM dual
            UNION ALL SELECT 'Russia', 'Moscow', 145934462 FROM dual
            UNION ALL SELECT 'Mexico', 'Mexico City', 128932753 FROM dual
            UNION ALL SELECT 'Japan', 'Tokyo', 126476461 FROM dual
            UNION ALL SELECT 'Ethiopia', 'Addis Ababa', 114963588 FROM dual
            UNION ALL SELECT 'Philippines', 'Manila', 109581078 FROM dual
            UNION ALL SELECT 'Egypt', 'Cairo', 102334404 FROM dual
            UNION ALL SELECT 'Vietnam', 'Hanoi', 97338579 FROM dual
            UNION ALL SELECT 'DR Congo', 'Kinshasa', 89561403 FROM dual
            UNION ALL SELECT 'Turkey', 'Ankara', 84339067 FROM dual
            UNION ALL SELECT 'Iran', 'Tehran', 83992953 FROM dual
            UNION ALL SELECT 'Germany', 'Berlin', 83783942 FROM dual
            UNION ALL SELECT 'Thailand', 'Bangkok', 69799978 FROM dual
        ) LOOP
            -- check existence fully qualified
            EXECUTE IMMEDIATE 'SELECT COUNT(*) FROM '||v_owner||'.COUNTRIES WHERE NAME = :1' INTO cnt USING c.name;
            IF cnt = 0 THEN
                EXECUTE IMMEDIATE 'INSERT INTO '||v_owner||'.COUNTRIES (Name, Capital, Population) VALUES (:1,:2,:3)'
                USING c.name, c.capital, c.population;
            END IF;
        END LOOP;
    END IF; -- countries table exists

    COMMIT;
END;
