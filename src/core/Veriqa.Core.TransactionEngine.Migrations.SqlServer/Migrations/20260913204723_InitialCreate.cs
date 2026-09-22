// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.TransactionEngine.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StateReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IdempotencyScope = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestedChannelType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientContext = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowedChannelTypesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfirmationSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChannelIdentitySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedIdentitySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletionSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OidcContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdentityMatchJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InitiatorContextSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IdempotencyScope_IdempotencyKey",
                table: "Transactions",
                columns: new[] { "IdempotencyScope", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyScope] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
