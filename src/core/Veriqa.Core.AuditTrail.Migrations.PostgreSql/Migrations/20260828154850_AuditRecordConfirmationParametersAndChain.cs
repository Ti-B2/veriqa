// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.AuditTrail.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AuditRecordConfirmationParametersAndChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmationActionType",
                table: "AuditRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationSlotValuesJson",
                table: "AuditRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousRecordHash",
                table: "AuditRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditRecords",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationActionType",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "ConfirmationSlotValuesJson",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "PreviousRecordHash",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditRecords");
        }
    }
}
