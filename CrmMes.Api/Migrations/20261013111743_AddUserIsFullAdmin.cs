using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsFullAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFullAdmin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Un ambiente già esistente ha già un primo Admin (chi l'ha installato/configurato): senza
            // questo aggiornamento resterebbe "Admin cliente" per sempre col solo default, perdendo
            // l'accesso alle aree riservate al fornitore sul suo stesso account. Stesso criterio del
            // bootstrap per un ambiente nuovo: il primo Admin creato è l'Admin pieno.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "IsFullAdmin" = true
                WHERE "Id" = (SELECT "Id" FROM "Users" WHERE "Role" = 'Admin' ORDER BY "CreatedAt" ASC LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFullAdmin",
                table: "Users");
        }
    }
}
