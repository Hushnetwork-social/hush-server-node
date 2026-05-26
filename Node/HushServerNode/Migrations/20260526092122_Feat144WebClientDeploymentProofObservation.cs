using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat144WebClientDeploymentProofObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionWebClientDeploymentProofObservationRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: true),
                    ObservationScope = table.Column<string>(type: "varchar(96)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(128)", nullable: true),
                    ComponentId = table.Column<string>(type: "varchar(64)", nullable: true),
                    DeploymentProofId = table.Column<string>(type: "varchar(256)", nullable: true),
                    DeploymentTarget = table.Column<string>(type: "varchar(128)", nullable: true),
                    SourceRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    WebArtifactHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ClientBundleHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    PackageHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    PublicPackageRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    DeploymentProtocolVersion = table.Column<string>(type: "varchar(128)", nullable: true),
                    MismatchCode = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionWebClientDeploymentProofObservationRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionWebClientDeploymentProofObservationRecord_Deploymen~",
                schema: "Elections",
                table: "ElectionWebClientDeploymentProofObservationRecord",
                columns: new[] { "DeploymentProofId", "ClientBundleHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionWebClientDeploymentProofObservationRecord_ElectionI~",
                schema: "Elections",
                table: "ElectionWebClientDeploymentProofObservationRecord",
                columns: new[] { "ElectionId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionWebClientDeploymentProofObservationRecord_MismatchC~",
                schema: "Elections",
                table: "ElectionWebClientDeploymentProofObservationRecord",
                column: "MismatchCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionWebClientDeploymentProofObservationRecord",
                schema: "Elections");
        }
    }
}
