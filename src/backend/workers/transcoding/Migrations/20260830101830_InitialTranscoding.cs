using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Transcoding.Worker.Migrations
{
    /// <inheritdoc />
    public partial class InitialTranscoding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "transcoding");

            migrationBuilder.CreateTable(
                name: "consumed_messages",
                schema: "transcoding",
                columns: table => new
                {
                    topic = table.Column<string>(type: "character varying(249)", maxLength: 249, nullable: false),
                    partition = table.Column<int>(type: "integer", nullable: false),
                    offset = table.Column<long>(type: "bigint", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumed_messages", x => new { x.topic, x.partition, x.offset });
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "transcoding",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    source_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    source_etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "transcoding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    topic = table.Column<string>(type: "character varying(249)", maxLength: 249, nullable: false),
                    partition_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "renditions",
                schema: "transcoding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    video_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    audio_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renditions", x => x.id);
                    table.ForeignKey(
                        name: "fk_renditions_jobs",
                        column: x => x.job_event_id,
                        principalSchema: "transcoding",
                        principalTable: "jobs",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consumed_messages_event_id",
                schema: "transcoding",
                table: "consumed_messages",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_available",
                schema: "transcoding",
                table: "jobs",
                columns: new[] { "status", "next_attempt_at_utc", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_video_id",
                schema: "transcoding",
                table: "jobs",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "transcoding",
                table: "outbox_messages",
                columns: new[] { "next_attempt_at_utc", "id" },
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_renditions_job_tier",
                schema: "transcoding",
                table: "renditions",
                columns: new[] { "job_event_id", "tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_renditions_object_key",
                schema: "transcoding",
                table: "renditions",
                column: "object_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumed_messages",
                schema: "transcoding");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "transcoding");

            migrationBuilder.DropTable(
                name: "renditions",
                schema: "transcoding");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "transcoding");
        }
    }
}
