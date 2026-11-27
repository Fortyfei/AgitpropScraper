using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agitprop.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entities_Name",
                table: "entities");

            migrationBuilder.AddColumn<string>(
                name: "MatchKind",
                table: "mentions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SurfaceForm",
                table: "mentions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "entities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "entities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikidataId",
                table: "entities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "entity_aliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    NormalizedAlias = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_entity_aliases_entities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill: every existing entity keeps its name as canonical form and its own alias.
            migrationBuilder.Sql("""
                UPDATE entities
                SET "NormalizedName" = lower(btrim("Name")),
                    "Source" = 'observed'
                WHERE "NormalizedName" IS NULL OR "Source" IS NULL;
                """);

            // The new unique key is (NormalizedName, Type). Rows that only differ in casing collapse
            // to the lowest id so the index can be created; mentions are re-pointed first and the
            // (ArticleId, EntityId) collisions dropped.
            migrationBuilder.Sql("""
                INSERT INTO mentions ("ArticleId", "EntityId", "SurfaceForm", "MatchKind")
                SELECT mention."ArticleId", ranked.keeper, mention."SurfaceForm", mention."MatchKind"
                FROM mentions mention
                JOIN (
                    SELECT "Id",
                           first_value("Id") OVER (PARTITION BY "NormalizedName", "Type" ORDER BY "Id") AS keeper
                    FROM entities
                ) ranked ON ranked."Id" = mention."EntityId"
                WHERE ranked."Id" <> ranked.keeper
                ON CONFLICT ("ArticleId", "EntityId") DO NOTHING;
                """);

            migrationBuilder.Sql("""
                DELETE FROM mentions mention
                USING (
                    SELECT "Id",
                           first_value("Id") OVER (PARTITION BY "NormalizedName", "Type" ORDER BY "Id") AS keeper
                    FROM entities
                ) ranked
                WHERE mention."EntityId" = ranked."Id" AND ranked."Id" <> ranked.keeper;
                """);

            migrationBuilder.Sql("""
                DELETE FROM entities entity
                USING (
                    SELECT "Id",
                           first_value("Id") OVER (PARTITION BY "NormalizedName", "Type" ORDER BY "Id") AS keeper
                    FROM entities
                ) ranked
                WHERE entity."Id" = ranked."Id" AND ranked."Id" <> ranked.keeper;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "entities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "entities",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql("""
                INSERT INTO entity_aliases ("Id", "EntityId", "Alias", "NormalizedAlias", "Type", "Priority")
                SELECT gen_random_uuid(), entity."Id", entity."Name", entity."NormalizedName", entity."Type", 0
                FROM entities entity;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_entities_NormalizedName_Type",
                table: "entities",
                columns: new[] { "NormalizedName", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entities_Source",
                table: "entities",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_entity_aliases_EntityId",
                table: "entity_aliases",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_entity_aliases_NormalizedAlias_Type_EntityId",
                table: "entity_aliases",
                columns: new[] { "NormalizedAlias", "Type", "EntityId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_aliases");

            migrationBuilder.DropIndex(
                name: "IX_entities_NormalizedName_Type",
                table: "entities");

            migrationBuilder.DropIndex(
                name: "IX_entities_Source",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "MatchKind",
                table: "mentions");

            migrationBuilder.DropColumn(
                name: "SurfaceForm",
                table: "mentions");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "WikidataId",
                table: "entities");

            migrationBuilder.CreateIndex(
                name: "IX_entities_Name",
                table: "entities",
                column: "Name",
                unique: true);
        }
    }
}
