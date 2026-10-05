using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// يعيد إنشاء جدول Attachments إن كان قد حُذف خطأً في بيئات طبّقت النسخة السابقة
    /// من RemoveDocumentsAndAttachments التي أسقطت Attachments مع Documents.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261005121500_RestoreAttachmentsTable")]
    public class RestoreAttachmentsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Attachments" (
                    "AttachmentId" uuid NOT NULL,
                    "ContentType" character varying(150) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "CreatedBy" text NULL,
                    "Description" character varying(1000) NULL,
                    "FileExtension" character varying(20) NOT NULL,
                    "FileName" character varying(260) NOT NULL,
                    "FilePath" character varying(1024) NOT NULL,
                    "FileSize" bigint NOT NULL,
                    "IsProfilePhoto" boolean NOT NULL DEFAULT FALSE,
                    "OwnerId" bigint NOT NULL,
                    "OwnerType" character varying(100) NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "UpdatedBy" text NULL,
                    "UploadedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT "PK_Attachments" PRIMARY KEY ("AttachmentId")
                );

                CREATE INDEX IF NOT EXISTS "IX_Attachments_OwnerType_OwnerId"
                    ON "Attachments" ("OwnerType", "OwnerId");

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Attachments_OwnerType_OwnerId_IsProfilePhoto"
                    ON "Attachments" ("OwnerType", "OwnerId", "IsProfilePhoto")
                    WHERE "IsProfilePhoto" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // لا نسقط الجدول في Down — نظام المرفقات مطلوب.
        }
    }
}
