using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec65LeadsCalendarAndPendingPersons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "leadid",
                schema: "admin",
                table: "personpendingconfirmations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "nextactioneventid",
                schema: "customer",
                table: "leadinteractions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leadid",
                schema: "calendar",
                table: "events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "calendaractivitycompanyid",
                schema: "customer",
                table: "contacttypecompanies",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_leadid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "leadid");

            migrationBuilder.CreateIndex(
                name: "ix_leadinteractions_nextactioneventid",
                schema: "customer",
                table: "leadinteractions",
                column: "nextactioneventid");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_company_lead",
                schema: "calendar",
                table: "events",
                columns: new[] { "companyid", "leadid" },
                filter: "\"leadid\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_events_leadid",
                schema: "calendar",
                table: "events",
                column: "leadid");

            migrationBuilder.CreateIndex(
                name: "ix_contacttypecompanies_calendaractivitycompanyid",
                schema: "customer",
                table: "contacttypecompanies",
                column: "calendaractivitycompanyid");

            migrationBuilder.AddForeignKey(
                name: "fk_contacttypecompanies_activitycompanies_calendaractivitycomp~",
                schema: "customer",
                table: "contacttypecompanies",
                column: "calendaractivitycompanyid",
                principalSchema: "calendar",
                principalTable: "activitycompanies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_events_leads_leadid",
                schema: "calendar",
                table: "events",
                column: "leadid",
                principalSchema: "customer",
                principalTable: "leads",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_leadinteractions_events_nextactioneventid",
                schema: "customer",
                table: "leadinteractions",
                column: "nextactioneventid",
                principalSchema: "calendar",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_personpendingconfirmations_leads_leadid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "leadid",
                principalSchema: "customer",
                principalTable: "leads",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contacttypecompanies_activitycompanies_calendaractivitycomp~",
                schema: "customer",
                table: "contacttypecompanies");

            migrationBuilder.DropForeignKey(
                name: "fk_events_leads_leadid",
                schema: "calendar",
                table: "events");

            migrationBuilder.DropForeignKey(
                name: "fk_leadinteractions_events_nextactioneventid",
                schema: "customer",
                table: "leadinteractions");

            migrationBuilder.DropForeignKey(
                name: "fk_personpendingconfirmations_leads_leadid",
                schema: "admin",
                table: "personpendingconfirmations");

            migrationBuilder.DropIndex(
                name: "ix_personpendingconfirmations_leadid",
                schema: "admin",
                table: "personpendingconfirmations");

            migrationBuilder.DropIndex(
                name: "ix_leadinteractions_nextactioneventid",
                schema: "customer",
                table: "leadinteractions");

            migrationBuilder.DropIndex(
                name: "ix_calendarevents_company_lead",
                schema: "calendar",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_events_leadid",
                schema: "calendar",
                table: "events");

            migrationBuilder.DropIndex(
                name: "ix_contacttypecompanies_calendaractivitycompanyid",
                schema: "customer",
                table: "contacttypecompanies");

            migrationBuilder.DropColumn(
                name: "leadid",
                schema: "admin",
                table: "personpendingconfirmations");

            migrationBuilder.DropColumn(
                name: "nextactioneventid",
                schema: "customer",
                table: "leadinteractions");

            migrationBuilder.DropColumn(
                name: "leadid",
                schema: "calendar",
                table: "events");

            migrationBuilder.DropColumn(
                name: "calendaractivitycompanyid",
                schema: "customer",
                table: "contacttypecompanies");
        }
    }
}
