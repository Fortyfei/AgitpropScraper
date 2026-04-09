using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agitprop.Sinks.Newsfeed.Database.Migrations
{
    /// <inheritdoc />
    public partial class EntityNameIsUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entities_Name",
                table: "entities");

            migrationBuilder.CreateIndex(
                name: "IX_entities_Name",
                table: "entities",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entities_Name",
                table: "entities");

            migrationBuilder.CreateIndex(
                name: "IX_entities_Name",
                table: "entities",
                column: "Name");
        }
    }
}
