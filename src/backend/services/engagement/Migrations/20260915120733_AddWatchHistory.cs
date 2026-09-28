using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Engagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "watch_history",
                schema: "engagement",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position_ms = table.Column<long>(type: "bigint", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_partition = table.Column<int>(type: "integer", nullable: false),
                    source_offset = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watch_history", x => new { x.user_id, x.video_id });
                    table.CheckConstraint("ck_watch_history_progress", "position_ms >= 0 AND duration_ms > 0 AND position_ms <= duration_ms AND duration_ms <= 9007199254740991");
                });

            migrationBuilder.CreateIndex(
                name: "ix_watch_history_user_updated",
                schema: "engagement",
                table: "watch_history",
                columns: new[] { "user_id", "updated_at_utc", "video_id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watch_history",
                schema: "engagement");
        }
    }
}
