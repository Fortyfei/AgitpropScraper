-- Runs once, on the first start of the postgres container, as the superuser the image
-- creates. Keep the app off that account: a compromised query could otherwise run programs on
-- the database host (COPY ... FROM PROGRAM).
\getenv app_password AGITPROP_DB_PASSWORD

CREATE ROLE agitprop LOGIN PASSWORD :'app_password';

-- The app owns its database, so EF Core migrations can create and alter tables.
CREATE DATABASE newsfeed OWNER agitprop;

\connect newsfeed

-- Extensions need superuser rights, which is why this runs here and not in the app.
CREATE EXTENSION IF NOT EXISTS pgmq;

-- pgmq keeps its state in its own schema. CREATE is needed because pgmq.create() makes the
-- queue table on the caller's behalf.
GRANT USAGE, CREATE ON SCHEMA pgmq TO agitprop;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA pgmq TO agitprop;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA pgmq TO agitprop;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA pgmq TO agitprop;
