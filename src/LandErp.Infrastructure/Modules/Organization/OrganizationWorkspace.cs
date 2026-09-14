using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandErp.Infrastructure.Modules.Organization;

public sealed class OrganizationWorkspace(IAccessControl access, IDbContextFactory<LandErpDbContext> factory,
    IServiceScopeFactory scopes) : IOrganizationWorkspace
{
    public async Task<OrganizationView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.UsersRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var query = from employee in db.Employees.AsNoTracking()
                    join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                    join user in db.Users on employee.UserId equals user.Id
                    join role in db.Roles on assignment.RoleId equals role.Id
                    where employee.OrganizationId == context.OrganizationId
                    select new { employee, assignment, user, role };
        query = context.Scope switch
        {
            AccessScope.Organization => query,
            AccessScope.Department => query.Where(item => context.DepartmentId != null && item.assignment.OrgUnitId == context.DepartmentId),
            AccessScope.Team => query.Where(item => context.TeamId != null && item.assignment.TeamId == context.TeamId),
            _ => query.Where(item => item.employee.Id == context.EmployeeId)
        };
        var rows = await query.OrderBy(item => item.employee.DisplayName).ToListAsync(cancellationToken);
        return new(await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.Name).SingleAsync(cancellationToken),
            await db.OrgUnits.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).Select(item => new NamedItem(item.Id, item.Name)).ToListAsync(cancellationToken),
            await db.Positions.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).Select(item => new NamedItem(item.Id, item.Name)).ToListAsync(cancellationToken),
            await db.Teams.Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).Select(item => new NamedItem(item.Id, item.Name)).ToListAsync(cancellationToken),
            await db.Roles.OrderBy(item => item.Name).Select(item => new NamedItem(item.Id, item.Name!)).ToListAsync(cancellationToken),
            rows.Select(item => new EmployeeView(item.employee.Id, item.employee.DisplayName, item.user.Email!, item.employee.Active,
                item.assignment.OrgUnitId, item.assignment.PositionId, item.assignment.TeamId, item.assignment.ManagerEmployeeId,
                item.assignment.RoleId, item.role.Name!, item.assignment.Scope, item.assignment.Version)).ToArray());
    }

    public Task CreateDepartmentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken) =>
        CreateNamedAsync(subject, name, "DepartmentCreated", correlationId, true, cancellationToken);

    public Task CreatePositionAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken) =>
        CreateNamedAsync(subject, name, "PositionCreated", correlationId, false, cancellationToken);

    private async Task CreateNamedAsync(Subject subject, string name, string action, string correlationId,
        bool department, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        name = ValidateName(name);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Guid id = DataConventions.NewId();
        if (department)
        {
            db.OrgUnits.Add(new() { Id = id, OrganizationId = context.OrganizationId, Name = name });
        }
        else
        {
            db.Positions.Add(new() { Id = id, OrganizationId = context.OrganizationId, Name = name });
        }

        AddAudit(db, context, subject, action, department ? "OrgUnit" : "Position", id, new { Name = name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateTeamAsync(Subject subject, Guid departmentId, string name, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.OrgUnits.AnyAsync(item => item.Id == departmentId && item.OrganizationId == context.OrganizationId, cancellationToken))
        {
            throw new AccessDeniedException();
        }

        Team team = new() { Id = DataConventions.NewId(), OrgUnitId = departmentId, OrganizationId = context.OrganizationId, Name = ValidateName(name) };
        db.Teams.Add(team);
        AddAudit(db, context, subject, "TeamCreated", "Team", team.Id, new { team.Name, departmentId }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvitationResult> InviteAsync(Subject subject, InviteEmployee command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId,
            command.ManagerId, command.RoleId, command.Scope, subject, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = command.Login.Trim(), Email = command.Login.Trim() };
        EnsureIdentity(await users.CreateAsync(user));
        string role = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        EnsureIdentity(await users.AddToRoleAsync(user, role));
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            UserId = user.Id, DisplayName = ValidateName(command.Name), Active = false };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id,
            OrgUnitId = command.DepartmentId, PositionId = command.PositionId, TeamId = command.TeamId,
            ManagerEmployeeId = command.ManagerId, RoleId = command.RoleId, Scope = command.Scope });
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        EmployeeInvitation invitation = new() { Id = DataConventions.NewId(), EmployeeId = employee.Id,
            TokenHash = HashToken(token), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        db.EmployeeInvitations.Add(invitation);
        AddAudit(db, context, subject, "EmployeeInvited", "Employee", employee.Id,
            new { employee.DisplayName, command.DepartmentId, command.PositionId, Role = role, command.Scope }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(invitation.Id, token);
    }

    public async Task ChangeAssignmentAsync(Subject subject, ChangeAssignment command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.RolesManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        Employee employee = await db.Employees.SingleOrDefaultAsync(item => item.Id == command.EmployeeId
            && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        EmployeeAssignment assignment = await db.EmployeeAssignments.SingleAsync(item => item.EmployeeId == employee.Id, cancellationToken);
        if (assignment.Version != command.ExpectedVersion)
        {
            throw new DbUpdateConcurrencyException("Назначение уже изменено. Обновите страницу.");
        }

        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId,
            command.ManagerId, command.RoleId, command.Scope, subject, cancellationToken);
        if (command.ManagerId == employee.Id)
        {
            throw new ArgumentException("Сотрудник не может быть собственным руководителем.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        string oldRole = await db.Roles.Where(item => item.Id == assignment.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        string newRole = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        if (oldRole == "Owner" && newRole != "Owner" && await (from item in db.Employees
            join current in db.EmployeeAssignments on item.Id equals current.EmployeeId
            join role in db.Roles on current.RoleId equals role.Id
            where item.OrganizationId == context.OrganizationId && item.Active && role.Name == "Owner"
            select item.Id).CountAsync(cancellationToken) <= 1)
        {
            throw new ArgumentException("Нельзя снять роль последнего активного Owner.");
        }

        var before = new { assignment.OrgUnitId, assignment.PositionId, assignment.TeamId,
            assignment.ManagerEmployeeId, Role = oldRole, assignment.Scope };
        assignment.OrgUnitId = command.DepartmentId;
        assignment.PositionId = command.PositionId;
        assignment.TeamId = command.TeamId;
        assignment.ManagerEmployeeId = command.ManagerId;
        assignment.RoleId = command.RoleId;
        assignment.Scope = command.Scope;
        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        EnsureIdentity(await users.RemoveFromRoleAsync(user, oldRole));
        EnsureIdentity(await users.AddToRoleAsync(user, newRole));
        EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        AddAudit(db, context, subject, "AssignmentChanged", "Employee", employee.Id,
            new { Before = before, After = command }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditView>> ReadAuditAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.AuditRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.AuditEvents.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId)
            .OrderByDescending(item => item.RecordedAt).Take(100).Select(item => new AuditView(item.RecordedAt,
                item.Action, item.ObjectType, item.ObjectId, item.ActorId, item.Changes)).ToListAsync(cancellationToken);
    }

    private async Task<AccessContext> RequireOrganizationAdminAsync(Subject subject, string permission, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, permission, cancellationToken);
        return context.Scope == AccessScope.Organization ? context : throw new AccessDeniedException();
    }

    private static async Task ValidateAssignmentAsync(LandErpDbContext db, AccessContext context,
        Guid? department, Guid? position, Guid? team, Guid? manager, Guid role, AccessScope assignmentScope,
        Subject subject, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(assignmentScope)
            || (department != null && !await db.OrgUnits.AnyAsync(item => item.Id == department && item.OrganizationId == context.OrganizationId, cancellationToken))
            || (position != null && !await db.Positions.AnyAsync(item => item.Id == position && item.OrganizationId == context.OrganizationId, cancellationToken))
            || (team != null && !await db.Teams.AnyAsync(item => item.Id == team && item.OrganizationId == context.OrganizationId && item.OrgUnitId == department, cancellationToken))
            || (manager != null && !await db.Employees.AnyAsync(item => item.Id == manager && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken))
            || !await db.Roles.AnyAsync(item => item.Id == role, cancellationToken))
        {
            throw new AccessDeniedException();
        }

        if (assignmentScope == AccessScope.Department && department == null || assignmentScope == AccessScope.Team && team == null)
        {
            throw new ArgumentException("Для выбранной области требуется подразделение или команда.");
        }

        string roleName = await db.Roles.Where(item => item.Id == role).Select(item => item.Name!).SingleAsync(cancellationToken);
        if (roleName == "Owner" && !await (from employee in db.Employees
            join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
            join currentRole in db.Roles on assignment.RoleId equals currentRole.Id
            where employee.UserId == subject.UserId && currentRole.Name == "Owner"
            select employee.Id).AnyAsync(cancellationToken))
        {
            throw new AccessDeniedException();
        }
    }

    internal static string ValidateName(string name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200
        ? throw new ArgumentException("Укажите название длиной от 1 до 200 символов.") : name.Trim();

    internal static void EnsureIdentity(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ArgumentException("Не удалось сохранить учётную запись. Проверьте адрес, уникальность и требования пароля.");
        }
    }

    internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static void AddAudit(LandErpDbContext db, AccessContext context, Subject subject,
        string action, string type, Guid id, object changes, string correlationId) =>
        db.AuditEvents.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            ActorId = subject.UserId, Action = action, ObjectType = type, ObjectId = id,
            Changes = JsonSerializer.Serialize(changes), RecordedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId.Length <= 64 ? correlationId : DataConventions.NewId().ToString() });
}
