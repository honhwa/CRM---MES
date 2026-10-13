-- Migrazione AddStoredFilesAndAttachments, da incollare nella SQL Editor della console Neon
-- (https://console.neon.tech), sul progetto MES, branch "production".
-- Additiva: nessuna tabella o colonna esistente viene toccata o persa.

START TRANSACTION;

ALTER TABLE "CompanyProfiles" ADD "LogoFileId" uuid;

CREATE TABLE "StoredFiles" (
    "Id" uuid NOT NULL,
    "ContentType" character varying(120) NOT NULL,
    "Content" bytea NOT NULL,
    "SizeBytes" integer NOT NULL,
    "UploadedBy" text,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_StoredFiles" PRIMARY KEY ("Id")
);

CREATE TABLE "WorkOrderAttachments" (
    "Id" uuid NOT NULL,
    "WorkOrderId" uuid NOT NULL,
    "Kind" character varying(20) NOT NULL,
    "Caption" text,
    "FileId" uuid NOT NULL,
    "UploadedBy" text,
    "UploadedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_WorkOrderAttachments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_WorkOrderAttachments_StoredFiles_FileId" FOREIGN KEY ("FileId") REFERENCES "StoredFiles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_WorkOrderAttachments_WorkOrders_WorkOrderId" FOREIGN KEY ("WorkOrderId") REFERENCES "WorkOrders" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_WorkOrderAttachments_FileId" ON "WorkOrderAttachments" ("FileId");

CREATE INDEX "IX_WorkOrderAttachments_WorkOrderId" ON "WorkOrderAttachments" ("WorkOrderId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261013124623_AddStoredFilesAndAttachments', '9.0.0');

COMMIT;
