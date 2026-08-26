using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIInterviewSimulator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueInterviewSessionUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InterviewSessions_UserId",
                table: "InterviewSessions");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSessions_UserId",
                table: "InterviewSessions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InterviewSessions_UserId",
                table: "InterviewSessions");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSessions_UserId",
                table: "InterviewSessions",
                column: "UserId");
        }
    }
}
