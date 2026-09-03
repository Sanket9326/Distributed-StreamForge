using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Feed.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHlsAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_hls",
                schema: "feed",
                table: "videos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_hls",
                schema: "feed",
                table: "videos");
        }
    }
}
