// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.AuditTrail.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Target = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChannelType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UiLocale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UiTimeZone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChannelDetails = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChannelInboundVerification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmationActionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmationSlotValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: true),
                    PreviousRecordHash = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_Timestamp",
                table: "AuditRecords",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords");
        }
    }
}
