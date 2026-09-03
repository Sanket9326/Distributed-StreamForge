using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamForge.Transcoding.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddHlsPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hls_packages",
                schema: "transcoding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    asset_prefix = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    master_playlist_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    master_playlist_etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    segment_format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_segment_duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                    total_size_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hls_packages", x => x.id);
                    table.UniqueConstraint("AK_hls_packages_job_event_id", x => x.job_event_id);
                    table.ForeignKey(
                        name: "FK_hls_packages_jobs_job_event_id",
                        column: x => x.job_event_id,
                        principalSchema: "transcoding",
                        principalTable: "jobs",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hls_variants",
                schema: "transcoding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    frame_rate = table.Column<double>(type: "double precision", nullable: false),
                    video_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    audio_codec = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    codecs = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    bandwidth_bits_per_second = table.Column<long>(type: "bigint", nullable: false),
                    average_bandwidth_bits_per_second = table.Column<long>(type: "bigint", nullable: false),
                    playlist_object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    playlist_etag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    segment_count = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hls_variants", x => x.id);
                    table.ForeignKey(
                        name: "FK_hls_variants_hls_packages_job_event_id",
                        column: x => x.job_event_id,
                        principalSchema: "transcoding",
                        principalTable: "hls_packages",
                        principalColumn: "job_event_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hls_packages_job_event_id",
                schema: "transcoding",
                table: "hls_packages",
                column: "job_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hls_variants_job_tier",
                schema: "transcoding",
                table: "hls_variants",
                columns: new[] { "job_event_id", "tier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hls_variants",
                schema: "transcoding");

            migrationBuilder.DropTable(
                name: "hls_packages",
                schema: "transcoding");
        }
    }
}
