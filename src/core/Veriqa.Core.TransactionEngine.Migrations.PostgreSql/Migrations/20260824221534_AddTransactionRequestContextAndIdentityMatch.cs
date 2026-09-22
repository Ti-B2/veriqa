// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Veriqa.Core.TransactionEngine.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionRequestContextAndIdentityMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentityMatchJson",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestContextJson",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdentityMatchJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RequestContextJson",
                table: "Transactions");
        }
    }
}
