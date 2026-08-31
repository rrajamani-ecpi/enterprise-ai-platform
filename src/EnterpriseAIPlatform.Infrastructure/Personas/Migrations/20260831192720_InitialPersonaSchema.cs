using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnterpriseAIPlatform.Infrastructure.Personas.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersonaSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Personas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerPartitionKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonaMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Extensions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataProducts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollaboratorPartitionKeys = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SharedWith = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    A2aEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsLessonPersona = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personas", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Personas");
        }
    }
}
