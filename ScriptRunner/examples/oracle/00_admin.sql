-- Admin setup for Oracle (DBA required)
-- Run this script as SYSDBA to ensure the WORKFLOWMONITOR user/schema exists and has quota on the USERS tablespace.
-- Note: In Oracle a schema is the same as a user. Creating the user WORKFLOWMONITOR creates the WorkFlowMonitor schema.

DECLARE
    cnt NUMBER;
    tbl_cnt NUMBER;
BEGIN
    SELECT COUNT(*) INTO cnt FROM dba_users WHERE username = 'WORLD';
    IF cnt = 0 THEN
        -- Create the user (change the password securely before running in production)
        EXECUTE IMMEDIATE 'CREATE USER WORLD IDENTIFIED BY "ChangeMe123" DEFAULT TABLESPACE USERS';
        EXECUTE IMMEDIATE 'GRANT CONNECT, RESOURCE TO WORLD';
        EXECUTE IMMEDIATE 'ALTER USER WORLD QUOTA UNLIMITED ON USERS';
    ELSE
        EXECUTE IMMEDIATE 'ALTER USER WORLD QUOTA UNLIMITED ON USERS';
    END IF;

    -- Ensure the schema (user) has at least one object to materialize the schema namespace if desired
    SELECT COUNT(*) INTO tbl_cnt FROM all_tables WHERE owner = 'WORLD' AND table_name = 'SCHEMA_MARKER';
    IF tbl_cnt = 0 THEN
        BEGIN
            EXECUTE IMMEDIATE 'CREATE TABLE WORLD.SCHEMA_MARKER (Id NUMBER)';
        EXCEPTION
            WHEN OTHERS THEN
                -- ignore if cannot create marker (permissions or already exists)
                NULL;
        END;
    END IF;
END;