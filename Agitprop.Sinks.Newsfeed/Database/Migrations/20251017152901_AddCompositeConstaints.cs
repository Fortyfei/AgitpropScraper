using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agitprop.Sinks.Newsfeed.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeConstaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    PublishedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "entities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mentions",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentions", x => new { x.ArticleId, x.EntityId });
                    table.ForeignKey(
                        name: "FK_mentions_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mentions_entities_EntityId",
                        column: x => x.EntityId,
                        principalTable: "entities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_articles_Url",
                table: "articles",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_entities_Name",
                table: "entities",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_mentions_EntityId",
                table: "mentions",
                column: "EntityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mentions");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "entities");
        }
    }
}
