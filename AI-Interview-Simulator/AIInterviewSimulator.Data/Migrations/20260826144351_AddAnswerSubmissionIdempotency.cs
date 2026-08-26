using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInterviewSimulator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerSubmissionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubmissionId",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "UserAnswers"
                SET "SubmissionId" = printf(
                    '00000000-0000-0000-0001-%012x',
                    "Id")
                WHERE "SubmissionId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SubmissionId",
                table: "UserAnswers",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAnswers_SubmissionId",
                table: "UserAnswers",
                column: "SubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAnswers_SubmissionId",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "UserAnswers");
        }
    }
}
