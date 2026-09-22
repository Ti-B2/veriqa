// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.AuditTrail.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AuditRecordTenantLocaleAndTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UiLocale",
                table: "AuditRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UiTimeZone",
                table: "AuditRecords",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "UiLocale",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "UiTimeZone",
                table: "AuditRecords");
        }
    }
}
