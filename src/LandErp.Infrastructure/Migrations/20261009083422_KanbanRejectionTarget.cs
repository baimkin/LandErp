using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KanbanRejectionTarget : Migration
    {
        private static readonly string[] RejectionTargetColumns =
            ["organization_id", "pipeline_id", "is_rejection_target"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_rejection_target",
                schema: "procurement",
                table: "kanban_stages",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Канбан: IsRejectionTarget. Исторические факты не удаляются.");

            migrationBuilder.Sql(
                """
                WITH preferred AS (
                    SELECT DISTINCT ON (stage.organization_id, stage.pipeline_id)
                        stage.id
                    FROM procurement.kanban_stages AS stage
                    INNER JOIN procurement.kanban_pipelines AS pipeline
                        ON pipeline.organization_id = stage.organization_id
                        AND pipeline.id = stage.pipeline_id
                    WHERE pipeline.is_active
                        AND stage.is_active
                        AND stage.kind = 'NegativeFinal'
                    ORDER BY stage.organization_id, stage.pipeline_id, stage.sort_order, stage.id
                )
                UPDATE procurement.kanban_stages AS stage
                SET is_rejection_target = TRUE
                FROM preferred
                WHERE stage.id = preferred.id;

                INSERT INTO procurement.kanban_stages
                    (id, organization_id, pipeline_id, name, description, color_key, sort_order,
                     is_initial, kind, is_rejection_target, is_active, is_hidden_on_board, version)
                SELECT gen_random_uuid(), pipeline.organization_id, pipeline.id, 'Не подходит',
                       'Системная цель отклонения объекта', 'danger',
                       COALESCE(MAX(stage.sort_order), -1) + 1,
                       FALSE, 'NegativeFinal', TRUE, TRUE, FALSE, 1
                FROM procurement.kanban_pipelines AS pipeline
                LEFT JOIN procurement.kanban_stages AS stage
                    ON stage.organization_id = pipeline.organization_id
                    AND stage.pipeline_id = pipeline.id
                WHERE pipeline.is_active
                GROUP BY pipeline.organization_id, pipeline.id
                HAVING NOT COALESCE(BOOL_OR(stage.is_rejection_target), FALSE);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_kanban_stages_organization_id_pipeline_id_is_rejection_target",
                schema: "procurement",
                table: "kanban_stages",
                columns: RejectionTargetColumns,
                unique: true,
                filter: "is_rejection_target AND is_active");

            migrationBuilder.AddCheckConstraint(
                name: "ck_kanban_stage_rejection_target",
                schema: "procurement",
                table: "kanban_stages",
                sql: "NOT is_rejection_target OR (is_active AND kind = 'NegativeFinal')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_kanban_stages_organization_id_pipeline_id_is_rejection_target",
                schema: "procurement",
                table: "kanban_stages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_kanban_stage_rejection_target",
                schema: "procurement",
                table: "kanban_stages");

            migrationBuilder.DropColumn(
                name: "is_rejection_target",
                schema: "procurement",
                table: "kanban_stages");
        }
    }
}
