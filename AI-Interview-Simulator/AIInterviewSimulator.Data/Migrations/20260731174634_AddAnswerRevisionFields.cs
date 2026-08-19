using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInterviewSimulator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerRevisionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChoseToRevise",
                table: "UserAnswers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedbackShownAt",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackText",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisedAnswerText",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisionDecisionAt",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisionSubmittedAt",
                table: "UserAnswers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChoseToRevise",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "FeedbackShownAt",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "FeedbackText",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "RevisedAnswerText",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "RevisionDecisionAt",
                table: "UserAnswers");

            migrationBuilder.DropColumn(
                name: "RevisionSubmittedAt",
                table: "UserAnswers");
        }
    }
}
