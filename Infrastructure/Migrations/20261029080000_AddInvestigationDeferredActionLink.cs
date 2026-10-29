using System;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20261029080000_AddInvestigationDeferredActionLink")]
public partial class AddInvestigationDeferredActionLink : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "deferredActionId",
            schema: "medical",
            table: "investigation",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_investigation_deferredActionId",
            schema: "medical",
            table: "investigation",
            column: "deferredActionId");

        migrationBuilder.AddForeignKey(
            name: "FK_investigation_deferredAction_deferredActionId",
            schema: "medical",
            table: "investigation",
            column: "deferredActionId",
            principalSchema: "clinical",
            principalTable: "deferredAction",
            principalColumn: "deferredActionId",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_investigation_deferredAction_deferredActionId",
            schema: "medical",
            table: "investigation");

        migrationBuilder.DropIndex(
            name: "IX_investigation_deferredActionId",
            schema: "medical",
            table: "investigation");

        migrationBuilder.DropColumn(
            name: "deferredActionId",
            schema: "medical",
            table: "investigation");
    }
}
