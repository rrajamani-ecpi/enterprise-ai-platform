using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseAIPlatform.Infrastructure.Prompts.Migrations
{
    /// <inheritdoc />
    public partial class InitialPromptSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Prompts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerPartitionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollaboratorPartitionKeys = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SharedWith = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptFavorites",
                columns: table => new
                {
                    UserPartitionKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PromptId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FavoritedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptFavorites", x => new { x.UserPartitionKey, x.PromptId });
                    table.ForeignKey(
                        name: "FK_PromptFavorites_Prompts_PromptId",
                        column: x => x.PromptId,
                        principalTable: "Prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromptFavorites_PromptId",
                table: "PromptFavorites",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_Prompts_OwnerPartitionKey",
                table: "Prompts",
                column: "OwnerPartitionKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromptFavorites");

            migrationBuilder.DropTable(
                name: "Prompts");
        }
    }
}
