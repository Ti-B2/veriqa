// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.TransactionEngine.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIdempotencyIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_IdempotencyScope_IdempotencyKey",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IdempotencyScope_IdempotencyKey",
                table: "Transactions",
                columns: new[] { "IdempotencyScope", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_IdempotencyScope_IdempotencyKey",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IdempotencyScope_IdempotencyKey",
                table: "Transactions",
                columns: new[] { "IdempotencyScope", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyScope\" IS NOT NULL AND \"IdempotencyKey\" IS NOT NULL");
        }
    }
}
