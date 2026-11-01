using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SanlamIHubAssessment.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    intermediary_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    activity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity", x => new { x.source, x.external_id });
                    table.CheckConstraint("ck_activity_currency_format", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_activity_version_positive", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_intermediary_occurred",
                table: "activity",
                columns: new[] { "intermediary_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity");
        }
    }
}
