DECLARE
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE WORLD.SCHEMA_MARKER';
EXCEPTION
    WHEN OTHERS THEN
        -- ignore if table does not exist (ORA-00942)
        IF SQLCODE = -942 THEN
            NULL;
        ELSE
            RAISE;
        END IF;
END;