using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Meridian.Api.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_states",
                columns: table => new
                {
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    member_from = table.Column<DateOnly>(type: "date", nullable: false),
                    country_name = table.Column<string>(type: "text", nullable: false),
                    member_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_states", x => new { x.country_code, x.member_from });
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    blob_key = table.Column<string>(type: "text", nullable: false),
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<int>(type: "integer", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    status = table.Column<string>(type: "text", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: false),
                    processed_rows = table.Column<int>(type: "integer", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: true),
                    report = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_imports_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    filter = table.Column<string>(type: "jsonb", nullable: false),
                    rules = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    pending_conflicts_at_creation = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_snapshots_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "votes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vote_ref = table.Column<string>(type: "text", nullable: false),
                    session = table.Column<int>(type: "integer", nullable: false),
                    vote_date = table.Column<DateOnly>(type: "date", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    details_source_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_votes", x => x.id);
                    table.CheckConstraint("ck_position", "position IS NULL OR position IN ('Y','N')");
                    table.CheckConstraint("ck_weight", "weight IS NULL OR weight BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_votes_imports_details_source_import_id",
                        column: x => x.details_source_import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "snapshot_publications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    quarter = table.Column<string>(type: "text", nullable: false),
                    published_by = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    reason = table.Column<string>(type: "text", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshot_publications", x => x.id);
                    table.ForeignKey(
                        name: "fk_snapshot_publications_snapshot_publications_superseded_by",
                        column: x => x.superseded_by,
                        principalTable: "snapshot_publications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_snapshot_publications_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_snapshot_publications_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "snapshot_scores",
                columns: table => new
                {
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    counted_votes = table.Column<int>(type: "integer", nullable: false),
                    counted_weight = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    earned_weight = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    score = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshot_scores", x => new { x.snapshot_id, x.country_code });
                    table.ForeignKey(
                        name: "fk_snapshot_scores_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conflicts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    approved_value = table.Column<string>(type: "text", nullable: false),
                    approved_source_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_source_line = table.Column<int>(type: "integer", nullable: true),
                    proposed_value = table.Column<string>(type: "text", nullable: false),
                    proposed_source_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_source_line = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    decided_by = table.Column<int>(type: "integer", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_conflicts_imports_approved_source_import_id",
                        column: x => x.approved_source_import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conflicts_imports_proposed_source_import_id",
                        column: x => x.proposed_source_import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conflicts_users_decided_by",
                        column: x => x.decided_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conflicts_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "country_votes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ballot = table.Column<char>(type: "character(1)", nullable: false),
                    source_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_line = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_country_votes", x => x.id);
                    table.CheckConstraint("ck_ballot", "ballot IN ('Y','N','A','X')");
                    table.ForeignKey(
                        name: "fk_country_votes_imports_source_import_id",
                        column: x => x.source_import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_country_votes_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "snapshot_contributions",
                columns: table => new
                {
                    snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    country_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    ballot = table.Column<char>(type: "character(1)", nullable: false),
                    position = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    source_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted = table.Column<bool>(type: "boolean", nullable: false),
                    points = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshot_contributions", x => new { x.snapshot_id, x.country_code, x.vote_id });
                    table.CheckConstraint("ck_ballot", "ballot IN ('Y','N','A','X')");
                    table.ForeignKey(
                        name: "fk_snapshot_contributions_imports_source_import_id",
                        column: x => x.source_import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_snapshot_contributions_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_snapshot_contributions_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "import_rows",
                columns: table => new
                {
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: false),
                    vote_ref = table.Column<string>(type: "text", nullable: true),
                    session = table.Column<int>(type: "integer", nullable: true),
                    vote_date = table.Column<DateOnly>(type: "date", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    country_code = table.Column<string>(type: "text", nullable: true),
                    country_name = table.Column<string>(type: "text", nullable: true),
                    ballot = table.Column<char>(type: "character(1)", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalizations = table.Column<string>(type: "jsonb", nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    conflict_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_rows", x => new { x.import_id, x.line_no });
                    table.ForeignKey(
                        name: "fk_import_rows_conflicts_conflict_id",
                        column: x => x.conflict_id,
                        principalTable: "conflicts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_import_rows_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vote_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: true),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    actor_id = table.Column<int>(type: "integer", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: true),
                    conflict_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vote_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_vote_history_conflicts_conflict_id",
                        column: x => x.conflict_id,
                        principalTable: "conflicts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_history_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_history_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_history_votes_vote_id",
                        column: x => x.vote_id,
                        principalTable: "votes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conflicts_vote_id_target_proposed_value",
                table: "conflicts",
                columns: new[] { "vote_id", "target", "proposed_value" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_country_votes_vote_id_country_code",
                table: "country_votes",
                columns: new[] { "vote_id", "country_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_snapshot_publications_quarter",
                table: "snapshot_publications",
                column: "quarter",
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vote_history_vote_id",
                table: "vote_history",
                column: "vote_id");

            migrationBuilder.CreateIndex(
                name: "ix_votes_vote_ref",
                table: "votes",
                column: "vote_ref",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "country_votes");

            migrationBuilder.DropTable(
                name: "import_rows");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "member_states");

            migrationBuilder.DropTable(
                name: "snapshot_contributions");

            migrationBuilder.DropTable(
                name: "snapshot_publications");

            migrationBuilder.DropTable(
                name: "snapshot_scores");

            migrationBuilder.DropTable(
                name: "vote_history");

            migrationBuilder.DropTable(
                name: "snapshots");

            migrationBuilder.DropTable(
                name: "conflicts");

            migrationBuilder.DropTable(
                name: "votes");

            migrationBuilder.DropTable(
                name: "imports");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
