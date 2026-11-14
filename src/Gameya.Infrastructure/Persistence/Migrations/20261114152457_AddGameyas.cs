using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gameya.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameyas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_user_id",
                table: "users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gameyas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    terms_version = table.Column<int>(type: "int", nullable: false),
                    invite_code = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    cancel_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    contribution_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    share_count = table.Column<int>(type: "int", nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    grace_days = table.Column<int>(type: "int", nullable: false),
                    payout_timing = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    payout_offset_days = table.Column<int>(type: "int", nullable: true),
                    order_method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    max_shares_per_member = table.Column<int>(type: "int", nullable: false),
                    organizer_participates = table.Column<bool>(type: "bit", nullable: false),
                    rules_text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gameyas", x => x.id);
                    table.ForeignKey(
                        name: "fk_gameyas_users_organizer_id",
                        column: x => x.organizer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "join_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gameya_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_terms_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    reject_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_join_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_join_requests_gameyas_gameya_id",
                        column: x => x.gameya_id,
                        principalTable: "gameyas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_join_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gameya_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    accepted_terms_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    removal_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_gameyas_gameya_id",
                        column: x => x.gameya_id,
                        principalTable: "gameyas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pending_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    gameya_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    share_count = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_invites_gameyas_gameya_id",
                        column: x => x.gameya_id,
                        principalTable: "gameyas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pending_invites_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "join_request_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preferences = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    join_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_join_request_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_join_request_shares_join_requests_join_request_id",
                        column: x => x.join_request_id,
                        principalTable: "join_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "member_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    preferences = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    membership_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_shares", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_shares_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gameyas_invite_code",
                table: "gameyas",
                column: "invite_code",
                unique: true,
                filter: "invite_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gameyas_organizer_id_status",
                table: "gameyas",
                columns: new[] { "organizer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_join_request_shares_join_request_id",
                table: "join_request_shares",
                column: "join_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_join_requests_gameya_id_user_id",
                table: "join_requests",
                columns: new[] { "gameya_id", "user_id" },
                unique: true,
                filter: "[status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_join_requests_user_id",
                table: "join_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_shares_membership_id",
                table: "member_shares",
                column: "membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_gameya_id_user_id",
                table: "memberships",
                columns: new[] { "gameya_id", "user_id" },
                unique: true,
                filter: "[status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_invites_gameya_id_phone",
                table: "pending_invites",
                columns: new[] { "gameya_id", "phone" },
                unique: true,
                filter: "[status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_pending_invites_phone",
                table: "pending_invites",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_pending_invites_user_id",
                table: "pending_invites",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "join_request_shares");

            migrationBuilder.DropTable(
                name: "member_shares");

            migrationBuilder.DropTable(
                name: "pending_invites");

            migrationBuilder.DropTable(
                name: "join_requests");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "gameyas");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "updated_by_user_id",
                table: "users");
        }
    }
}
