using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Api.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRowVoteRefIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_import_rows_import_id_vote_ref",
                table: "import_rows",
                columns: new[] { "import_id", "vote_ref" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_import_rows_import_id_vote_ref",
                table: "import_rows");
        }
    }
}
