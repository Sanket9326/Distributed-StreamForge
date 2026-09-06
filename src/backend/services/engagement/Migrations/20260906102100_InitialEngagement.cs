using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Engagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "engagement");

            migrationBuilder.CreateTable(
                name: "consumed_messages",
                schema: "engagement",
                columns: table => new
                {
                    topic = table.Column<string>(type: "character varying(249)", maxLength: 249, nullable: false),
                    partition = table.Column<int>(type: "integer", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumed_messages", x => new { x.topic, x.partition, x.offset });
                });

            migrationBuilder.CreateTable(
                name: "videos",
                schema: "engagement",
                columns: table => new
                {
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_videos", x => x.video_id);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "engagement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_comments_videos",
                        column: x => x.video_id,
                        principalSchema: "engagement",
                        principalTable: "videos",
                        principalColumn: "video_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                schema: "engagement",
                columns: table => new
                {
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_partition = table.Column<int>(type: "integer", nullable: false),
                    source_offset = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reactions", x => new { x.video_id, x.user_id });
                    table.CheckConstraint("ck_reactions_value", "value IN ('like', 'dislike')");
                    table.ForeignKey(
                        name: "fk_reactions_videos",
                        column: x => x.video_id,
                        principalSchema: "engagement",
                        principalTable: "videos",
                        principalColumn: "video_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_views",
                schema: "engagement",
                columns: table => new
                {
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_video_views", x => x.video_id);
                    table.CheckConstraint("ck_video_views_count", "count >= 0");
                    table.ForeignKey(
                        name: "fk_video_views_videos",
                        column: x => x.video_id,
                        principalSchema: "engagement",
                        principalTable: "videos",
                        principalColumn: "video_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comments_video_created",
                schema: "engagement",
                table: "comments",
                columns: new[] { "video_id", "created_at_utc", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_consumed_messages_event_id",
                schema: "engagement",
                table: "consumed_messages",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comments",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "consumed_messages",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "reactions",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "video_views",
                schema: "engagement");

            migrationBuilder.DropTable(
                name: "videos",
                schema: "engagement");
        }
    }
}
