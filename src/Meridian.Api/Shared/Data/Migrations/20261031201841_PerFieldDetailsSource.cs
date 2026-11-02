using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Api.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerFieldDetailsSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_votes_imports_details_source_import_id",
                table: "votes");

            migrationBuilder.RenameColumn(
                name: "details_source_import_id",
                table: "votes",
                newName: "vote_date_source_import_id");

            migrationBuilder.AddColumn<Guid>(
                name: "session_source_import_id",
                table: "votes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "title_source_import_id",
                table: "votes",
                type: "uuid",
                nullable: true);

            // The one source held so far is the best known answer for all three fields
            migrationBuilder.Sql("""
                UPDATE votes
                SET session_source_import_id = vote_date_source_import_id,
                    title_source_import_id = vote_date_source_import_id
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "session_source_import_id",
                table: "votes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "title_source_import_id",
                table: "votes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_votes_imports_session_source_import_id",
                table: "votes",
                column: "session_source_import_id",
                principalTable: "imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_votes_imports_title_source_import_id",
                table: "votes",
                column: "title_source_import_id",
                principalTable: "imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_votes_imports_vote_date_source_import_id",
                table: "votes",
                column: "vote_date_source_import_id",
                principalTable: "imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_votes_imports_session_source_import_id",
                table: "votes");

            migrationBuilder.DropForeignKey(
                name: "fk_votes_imports_title_source_import_id",
                table: "votes");

            migrationBuilder.DropForeignKey(
                name: "fk_votes_imports_vote_date_source_import_id",
                table: "votes");

            migrationBuilder.DropColumn(
                name: "session_source_import_id",
                table: "votes");

            migrationBuilder.DropColumn(
                name: "title_source_import_id",
                table: "votes");

            migrationBuilder.RenameColumn(
                name: "vote_date_source_import_id",
                table: "votes",
                newName: "details_source_import_id");

            migrationBuilder.AddForeignKey(
                name: "fk_votes_imports_details_source_import_id",
                table: "votes",
                column: "details_source_import_id",
                principalTable: "imports",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
