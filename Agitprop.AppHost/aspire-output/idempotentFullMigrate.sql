CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE TABLE articles (
        "Id" uuid NOT NULL,
        "Title" text NOT NULL,
        "Url" text NOT NULL,
        "PublishedTime" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_articles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE TABLE entities (
        "Id" uuid NOT NULL,
        "Name" text NOT NULL,
        "Type" text NOT NULL,
        CONSTRAINT "PK_entities" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE TABLE mentions (
        "ArticleId" uuid NOT NULL,
        "EntityId" uuid NOT NULL,
        "Id" uuid NOT NULL,
        CONSTRAINT "PK_mentions" PRIMARY KEY ("ArticleId", "EntityId"),
        CONSTRAINT "FK_mentions_articles_ArticleId" FOREIGN KEY ("ArticleId") REFERENCES articles ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_mentions_entities_EntityId" FOREIGN KEY ("EntityId") REFERENCES entities ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE UNIQUE INDEX "IX_articles_Url" ON articles ("Url");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE INDEX "IX_entities_Name" ON entities ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    CREATE INDEX "IX_mentions_EntityId" ON mentions ("EntityId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251017152901_AddCompositeConstaints') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251017152901_AddCompositeConstaints', '10.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251030200700_RemoveMentionsId') THEN
    ALTER TABLE mentions DROP COLUMN "Id";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251030200700_RemoveMentionsId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251030200700_RemoveMentionsId', '10.0.0');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251030231717_EntityNameIsUnique') THEN
    DROP INDEX "IX_entities_Name";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251030231717_EntityNameIsUnique') THEN
    CREATE UNIQUE INDEX "IX_entities_Name" ON entities ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251030231717_EntityNameIsUnique') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251030231717_EntityNameIsUnique', '10.0.0');
    END IF;
END $EF$;
COMMIT;

