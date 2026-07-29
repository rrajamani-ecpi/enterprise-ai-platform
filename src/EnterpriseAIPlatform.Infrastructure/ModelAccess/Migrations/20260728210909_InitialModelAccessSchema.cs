using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseAIPlatform.Infrastructure.ModelAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialModelAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageLimitConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PerMessageCharacterCap = table.Column<int>(type: "int", nullable: true),
                    DailyMessageCap = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageLimitConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelAliases",
                columns: table => new
                {
                    RetiredModelId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReplacementModelId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelAliases", x => x.RetiredModelId);
                });

            migrationBuilder.CreateTable(
                name: "ModelConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequiresAdvancedModelAccess = table.Column<bool>(type: "bit", nullable: false),
                    SupportsToolCalling = table.Column<bool>(type: "bit", nullable: true),
                    SupportsVision = table.Column<bool>(type: "bit", nullable: true),
                    SupportsReasoning = table.Column<bool>(type: "bit", nullable: true),
                    AccessTier = table.Column<int>(type: "int", nullable: false),
                    ContextWindowSize = table.Column<int>(type: "int", nullable: false),
                    PricingInputPerMillionTokens = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PricingOutputPerMillionTokens = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonaGenerationModelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllowedModelIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonaGenerationModelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemModelConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleModelAccess = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmbeddingModelId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageModelId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ArtifactModelId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FallbackModelId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemModelConfigs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ModelConfigs",
                columns: new[] { "Id", "AccessTier", "ContextWindowSize", "DisplayName", "IsDeleted", "IsEnabled", "PricingInputPerMillionTokens", "PricingOutputPerMillionTokens", "Provider", "RequiresAdvancedModelAccess", "SupportsReasoning", "SupportsToolCalling", "SupportsVision" },
                values: new object[] { "azure-foundry:gpt-5", 0, 272000, "GPT-5 (Azure Foundry)", false, true, 0m, 0m, "azure-foundry", false, true, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageLimitConfigs");

            migrationBuilder.DropTable(
                name: "ModelAliases");

            migrationBuilder.DropTable(
                name: "ModelConfigs");

            migrationBuilder.DropTable(
                name: "PersonaGenerationModelConfigs");

            migrationBuilder.DropTable(
                name: "SystemModelConfigs");
        }
    }
}
