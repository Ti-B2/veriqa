// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.AuditTrail.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AuditRecordChannelInboundVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelInboundVerification",
                table: "AuditRecords",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelInboundVerification",
                table: "AuditRecords");
        }
    }
}
