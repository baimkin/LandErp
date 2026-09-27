using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260927020000_CaseTasks")]
public partial class CaseTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("deleted", "work_tasks", "boolean", schema: "workflow", nullable: false, defaultValue: false,
            comment: "Задача убрана из рабочих списков; история и сама запись сохраняются.");
        migrationBuilder.AddColumn<bool>("is_user_task", "work_tasks", "boolean", schema: "workflow", nullable: false, defaultValue: false,
            comment: "Пользовательская или сохранённая прежняя задача; системные переходы не перезаписывают её.");
        migrationBuilder.AddColumn<bool>("due_has_time", "work_tasks", "boolean", schema: "workflow", nullable: false, defaultValue: true,
            comment: "True: точный срок UTC. False: due_at хранит начало даты Europe/Moscow; просрочка со следующего дня, время не показывается.");
        // Сохраняем прежние задачи, включая Первичный анализ, сроки, исполнителей и версии.
        migrationBuilder.Sql("UPDATE workflow.work_tasks SET is_user_task = true WHERE object_type = 'PropertyCase';");
        migrationBuilder.CreateIndex("ix_work_tasks_organization_id_object_type_object_id", "work_tasks",
            ["organization_id", "object_type", "object_id"], schema: "workflow");
        // Новых таблиц нет: существующие runtime SELECT/INSERT/UPDATE grants достаточны.
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_work_tasks_organization_id_object_type_object_id", "work_tasks", "workflow");
        migrationBuilder.DropColumn("deleted", "work_tasks", "workflow");
        migrationBuilder.DropColumn("is_user_task", "work_tasks", "workflow");
        migrationBuilder.DropColumn("due_has_time", "work_tasks", "workflow");
    }
}
