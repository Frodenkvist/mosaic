using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mosaic.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DateAdded = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    WatchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResumePositionSeconds = table.Column<double>(type: "REAL", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_MediaItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaArtwork",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsManualOverride = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaArtwork", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaArtwork_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WatchSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchSessions_MediaItems_MediaItemId",
                        column: x => x.MediaItemId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaArtwork_MediaItemId_Kind",
                table: "MediaArtwork",
                columns: new[] { "MediaItemId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_FilePath",
                table: "MediaItems",
                column: "FilePath",
                unique: true,
                filter: "\"FilePath\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_ParentId",
                table: "MediaItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_EndedAt",
                table: "WatchSessions",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_MediaItemId",
                table: "WatchSessions",
                column: "MediaItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaArtwork");

            migrationBuilder.DropTable(
                name: "WatchSessions");

            migrationBuilder.DropTable(
                name: "MediaItems");
        }
    }
}
