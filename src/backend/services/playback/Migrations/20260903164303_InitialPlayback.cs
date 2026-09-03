using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Playback.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlayback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "playback");

            migrationBuilder.CreateTable(
                name: "consumed_messages",
                schema: "playback",
                columns: table => new
                {
                    topic = table.Column<string>(type: "character varying(249)", maxLength: 249, nullable: false),
                    partition = table.Column<int>(type: "integer", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumed_messages", x => new { x.topic, x.partition, x.offset });
                });

            migrationBuilder.CreateTable(
                name: "packages",
                schema: "playback",
                columns: table => new
                {
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    asset_prefix = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    master_playlist_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    master_playlist_etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    projected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_packages", x => x.video_id);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "playback",
                columns: table => new
                {
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    bandwidth_bits_per_second = table.Column<long>(type: "bigint", nullable: false),
                    playlist_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    playlist_etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_variants", x => new { x.video_id, x.tier });
                    table.ForeignKey(
                        name: "FK_variants_packages_video_id",
                        column: x => x.video_id,
                        principalSchema: "playback",
                        principalTable: "packages",
                        principalColumn: "video_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consumed_messages_event_id",
                schema: "playback",
                table: "consumed_messages",
                column: "event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumed_messages",
                schema: "playback");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "playback");

            migrationBuilder.DropTable(
                name: "packages",
                schema: "playback");
        }
    }
}
