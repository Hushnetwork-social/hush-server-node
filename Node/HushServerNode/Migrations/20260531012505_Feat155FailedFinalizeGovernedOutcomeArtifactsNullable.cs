using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat155FailedFinalizeGovernedOutcomeArtifactsNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in new[]
                     {
                         "OfficialResultArtifactId",
                         "OfficialResultSourceArtifactId",
                         "TallyReadyArtifactId",
                         "UnofficialResultArtifactId",
                     })
            {
                migrationBuilder.AlterColumn<Guid>(
                    name: column,
                    schema: "Elections",
                    table: "ElectionGovernedOutcomeDecisionRecord",
                    type: "uuid",
                    nullable: true,
                    oldClrType: typeof(Guid),
                    oldType: "uuid");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var column in new[]
                     {
                         "OfficialResultArtifactId",
                         "OfficialResultSourceArtifactId",
                         "TallyReadyArtifactId",
                         "UnofficialResultArtifactId",
                     })
            {
                migrationBuilder.AlterColumn<Guid>(
                    name: column,
                    schema: "Elections",
                    table: "ElectionGovernedOutcomeDecisionRecord",
                    type: "uuid",
                    nullable: false,
                    defaultValue: Guid.Empty,
                    oldClrType: typeof(Guid),
                    oldType: "uuid",
                    oldNullable: true);
            }
        }
    }
}
