using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcxiomCRM.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-level guarantee that audit entries are append-only (in addition to the DbContext guard).
            migrationBuilder.Sql("CREATE TRIGGER TR_AuditLogs_NoUpdate BEFORE UPDATE ON AuditLogs BEGIN SELECT RAISE(ABORT, 'AuditLogs is append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER TR_AuditLogs_NoDelete BEFORE DELETE ON AuditLogs BEGIN SELECT RAISE(ABORT, 'AuditLogs is append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AuditLogs_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AuditLogs_NoDelete;");
        }
    }
}
