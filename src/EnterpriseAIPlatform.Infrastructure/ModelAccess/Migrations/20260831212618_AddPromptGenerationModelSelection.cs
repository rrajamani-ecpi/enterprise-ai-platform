using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptGenerationModelSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FallbackModelId",
                table: "PersonaGenerationModelConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryModelId",
                table: "PersonaGenerationModelConfigs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FallbackModelId",
                table: "PersonaGenerationModelConfigs");

            migrationBuilder.DropColumn(
                name: "PrimaryModelId",
                table: "PersonaGenerationModelConfigs");
        }
    }
}
