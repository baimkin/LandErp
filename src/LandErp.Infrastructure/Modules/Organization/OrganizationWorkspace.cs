using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Organization;

public sealed class OrganizationWorkspace(IAccessControl access, IDbContextFactory<LandErpDbContext> factory,
    IServiceScopeFactory scopes) : IOrganizationWorkspace
{
    public Task CreateDepartmentAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken) =>
        SaveDepartmentAsync(subject, new(null, null, name, string.Empty, null), correlationId, cancellationToken);

    public Task CreateTeamAsync(Subject subject, Guid departmentId, string name, string correlationId, CancellationToken cancellationToken) =>
        SaveTeamAsync(subject, new(null, null, departmentId, name, string.Empty, null), correlationId, cancellationToken);

    public Task CreatePositionAsync(Subject subject, string name, string correlationId, CancellationToken cancellationToken) =>
        SavePositionAsync(subject, new(null, null, name, string.Empty), correlationId, cancellationToken);

    public async Task<OrganizationView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.UsersRead, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        var employeeQuery = from employee in db.Employees.AsNoTracking()
                            join assignment in db.EmployeeAssignments.AsNoTracking() on employee.Id equals assignment.EmployeeId
                            join user in db.Users.AsNoTracking() on employee.UserId equals user.Id
                            join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                            where employee.OrganizationId == context.OrganizationId
                            select new { employee, assignment, user, role };
        employeeQuery = context.Scope switch
        {
            AccessScope.Organization => employeeQuery,
            AccessScope.Department => employeeQuery.Where(item => context.DepartmentId != null && item.assignment.OrgUnitId == context.DepartmentId),
            AccessScope.Team => employeeQuery.Where(item => context.TeamId != null && item.assignment.TeamId == context.TeamId),
            _ => employeeQuery.Where(item => item.employee.Id == context.EmployeeId)
        };
        var visibleEmployees = await employeeQuery.OrderBy(item => item.employee.DisplayName).ToArrayAsync(cancellationToken);
        Employee[] allEmployees = context.Scope == AccessScope.Organization
            ? visibleEmployees.Select(item => item.employee).ToArray()
            : await db.Employees.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId).ToArrayAsync(cancellationToken);
        EmployeeAssignment[] allAssignments = context.Scope == AccessScope.Organization
            ? await db.EmployeeAssignments.AsNoTracking().Where(item => allEmployees.Select(value => value.Id).Contains(item.EmployeeId)).ToArrayAsync(cancellationToken)
            : visibleEmployees.Select(item => item.assignment).ToArray();
        Dictionary<Guid, string> names = allEmployees.ToDictionary(item => item.Id, item => item.DisplayName);
        OrgUnit[] departments = await db.OrgUnits.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        Team[] teams = await db.Teams.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        Position[] positions = await db.Positions.AsNoTracking().Where(item => item.OrganizationId == context.OrganizationId).OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        Dictionary<Guid, string> departmentNames = departments.ToDictionary(item => item.Id, item => item.Name);
        return new(
            await db.Organizations.Where(item => item.Id == context.OrganizationId).Select(item => item.Name).SingleAsync(cancellationToken),
            departments.Select(item => new DepartmentView(item.Id, item.Name, item.Description, item.ManagerEmployeeId,
                item.ManagerEmployeeId == null ? null : names.GetValueOrDefault(item.ManagerEmployeeId.Value, "Сотрудник"), item.Active,
                teams.Count(value => value.OrgUnitId == item.Id), allAssignments.Count(value => value.OrgUnitId == item.Id), item.Version)).ToArray(),
            positions.Select(item => new PositionView(item.Id, item.Name, item.Description, item.Active,
                allAssignments.Count(value => value.PositionId == item.Id), allAssignments.Where(value => value.PositionId == item.Id && value.OrgUnitId != null)
                    .Select(value => departmentNames.GetValueOrDefault(value.OrgUnitId!.Value)).OfType<string>().Distinct().Order().ToArray(), item.Version)).ToArray(),
            teams.Select(item => new TeamView(item.Id, item.OrgUnitId, item.Name, item.Description, item.ManagerEmployeeId,
                item.ManagerEmployeeId == null ? null : names.GetValueOrDefault(item.ManagerEmployeeId.Value, "Сотрудник"), item.Active,
                allAssignments.Count(value => value.TeamId == item.Id), item.Version)).ToArray(),
            await db.Roles.OrderBy(item => item.Name).Select(item => new NamedItem(item.Id, item.Name!)).ToArrayAsync(cancellationToken),
            visibleEmployees.Select(item => new EmployeeView(item.employee.Id, item.employee.DisplayName, item.user.UserName!,
                item.employee.Active ? EmployeeState.Active : item.user.PasswordHash == null ? EmployeeState.PendingActivation : EmployeeState.Disabled,
                item.user.MustChangePassword, item.assignment.OrgUnitId, item.assignment.PositionId, item.assignment.TeamId,
                item.assignment.ManagerEmployeeId, item.assignment.RoleId, item.role.Name!, item.assignment.Scope, item.assignment.Version, item.employee.Version)).ToArray());
    }

    public async Task SaveDepartmentAsync(Subject subject, SaveDepartment command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await ValidateManagerAsync(db, context, command.ManagerId, cancellationToken);
        OrgUnit? item = null;
        if (command.Id != null)
            item = await db.OrgUnits.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item != null && item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Отдел уже изменён. Обновите страницу.");
        object? before = item == null ? null : new { item.Name, item.Description, item.ManagerEmployeeId };
        item ??= new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId };
        if (command.Id == null) db.OrgUnits.Add(item);
        item.Name = ValidateName(command.Name); item.Description = Optional(command.Description, 1000); item.ManagerEmployeeId = command.ManagerId;
        AddAudit(db, context, subject, command.Id == null ? "DepartmentCreated" : "DepartmentUpdated", "OrgUnit", item.Id,
            new { Before = before, After = new { item.Name, item.Description, item.ManagerEmployeeId } }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDepartmentActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        OrgUnit item = await db.OrgUnits.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Отдел уже изменён. Обновите страницу.");
        if (!command.Active && (await ActiveAssignments(db, context.OrganizationId).AnyAsync(value => value.OrgUnitId == item.Id, cancellationToken)
            || await db.Teams.AnyAsync(value => value.OrgUnitId == item.Id && value.Active, cancellationToken)))
            throw new ArgumentException("Сначала переназначьте активных сотрудников и архивируйте команды отдела.");
        item.Active = command.Active;
        AddAudit(db, context, subject, command.Active ? "DepartmentRestored" : "DepartmentArchived", "OrgUnit", item.Id, new { item.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveTeamAsync(Subject subject, SaveTeam command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.OrgUnits.AnyAsync(item => item.Id == command.DepartmentId && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)) throw new AccessDeniedException();
        await ValidateManagerAsync(db, context, command.ManagerId, cancellationToken);
        Team? item = null;
        if (command.Id != null)
            item = await db.Teams.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item != null && item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Команда уже изменена. Обновите страницу.");
        if (item != null && item.OrgUnitId != command.DepartmentId && await ActiveAssignments(db, context.OrganizationId).AnyAsync(value => value.TeamId == item.Id, cancellationToken))
            throw new ArgumentException("Перед переносом команды переназначьте её активных сотрудников.");
        object? before = item == null ? null : new { item.Name, item.Description, item.OrgUnitId, item.ManagerEmployeeId };
        item ??= new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId };
        if (command.Id == null) db.Teams.Add(item);
        item.OrgUnitId = command.DepartmentId; item.Name = ValidateName(command.Name); item.Description = Optional(command.Description, 1000); item.ManagerEmployeeId = command.ManagerId;
        AddAudit(db, context, subject, command.Id == null ? "TeamCreated" : "TeamUpdated", "Team", item.Id,
            new { Before = before, After = new { item.Name, item.Description, item.OrgUnitId, item.ManagerEmployeeId } }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTeamActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Team item = await db.Teams.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Команда уже изменена. Обновите страницу.");
        if (command.Active && !await db.OrgUnits.AnyAsync(value => value.Id == item.OrgUnitId && value.Active, cancellationToken)) throw new ArgumentException("Сначала восстановите отдел команды.");
        if (!command.Active && await ActiveAssignments(db, context.OrganizationId).AnyAsync(value => value.TeamId == item.Id, cancellationToken)) throw new ArgumentException("Сначала переназначьте активных сотрудников команды.");
        item.Active = command.Active;
        AddAudit(db, context, subject, command.Active ? "TeamRestored" : "TeamArchived", "Team", item.Id, new { item.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SavePositionAsync(Subject subject, SavePosition command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Position? item = null;
        if (command.Id != null)
            item = await db.Positions.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item != null && item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Должность уже изменена. Обновите страницу.");
        object? before = item == null ? null : new { item.Name, item.Description };
        item ??= new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId };
        if (command.Id == null) db.Positions.Add(item);
        item.Name = ValidateName(command.Name); item.Description = Optional(command.Description, 1000);
        AddAudit(db, context, subject, command.Id == null ? "PositionCreated" : "PositionUpdated", "Position", item.Id,
            new { Before = before, After = new { item.Name, item.Description } }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetPositionActiveAsync(Subject subject, SetOrganizationItemActive command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Position item = await db.Positions.SingleOrDefaultAsync(value => value.Id == command.Id && value.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Должность уже изменена. Обновите страницу.");
        if (!command.Active && await ActiveAssignments(db, context.OrganizationId).AnyAsync(value => value.PositionId == item.Id, cancellationToken)) throw new ArgumentException("Сначала измените должность активных сотрудников.");
        item.Active = command.Active;
        AddAudit(db, context, subject, command.Active ? "PositionRestored" : "PositionArchived", "Position", item.Id, new { item.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TemporaryCredential> CreateEmployeeAsync(Subject subject, CreateEmployee command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await access.RequireAsync(subject, Permissions.RolesManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId, command.RoleId, command.Scope, subject, true, cancellationToken);
        string login = ValidateLogin(command.Login); string password = GenerateTemporaryPassword();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, MustChangePassword = command.MustChangePassword, LockoutEnabled = true };
        EnsureIdentity(await users.CreateAsync(user, password));
        string role = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        EnsureIdentity(await users.AddToRoleAsync(user, role));
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, UserId = user.Id, DisplayName = ValidateName(command.Name), Active = true };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, OrgUnitId = command.DepartmentId,
            PositionId = command.PositionId, TeamId = command.TeamId, ManagerEmployeeId = command.ManagerId, RoleId = command.RoleId, Scope = command.Scope });
        AddAudit(db, context, subject, "EmployeeCreated", "Employee", employee.Id,
            new { employee.DisplayName, Login = login, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId, Role = role, command.Scope, command.MustChangePassword }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(employee.Id, login, password);
    }

    public async Task<TemporaryCredential> ResetTemporaryPasswordAsync(Subject subject, Guid employeeId, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        Employee employee = await db.Employees.SingleOrDefaultAsync(item => item.Id == employeeId && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string password = GenerateTemporaryPassword(); string token = await users.GeneratePasswordResetTokenAsync(user);
        EnsureIdentity(await users.ResetPasswordAsync(user, token, password));
        user.MustChangePassword = true; EnsureIdentity(await users.UpdateAsync(user)); EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        AddAudit(db, context, subject, "EmployeePasswordReset", "Employee", employee.Id, new { Login = user.UserName, MustChangePassword = true }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(employee.Id, user.UserName!, password);
    }

    public async Task SetEmployeeActiveAsync(Subject subject, SetEmployeeActive command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        Employee employee = await db.Employees.SingleOrDefaultAsync(item => item.Id == command.EmployeeId && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (employee.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Сотрудник уже изменён. Обновите страницу.");
        if (!command.Active && employee.UserId == subject.UserId) throw new ArgumentException("Нельзя отключить собственную учётную запись.");
        if (!command.Active && await IsLastActiveOwnerAsync(db, employee.Id, context.OrganizationId, cancellationToken)) throw new ArgumentException("Нельзя отключить последнего активного Owner.");
        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        if (command.Active && user.PasswordHash == null) throw new ArgumentException("Сначала завершите активацию приглашённого сотрудника или создайте ему прямую учётную запись.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        employee.Active = command.Active;
        EnsureIdentity(await users.SetLockoutEndDateAsync(user, command.Active ? null : DateTimeOffset.MaxValue));
        EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        AddAudit(db, context, subject, command.Active ? "EmployeeRestored" : "EmployeeDeactivated", "Employee", employee.Id, new { employee.DisplayName }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InvitationResult> InviteAsync(Subject subject, InviteEmployee command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId, command.RoleId, command.Scope, subject, true, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string login = ValidateLogin(command.Login);
        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, Email = login, MustChangePassword = false };
        EnsureIdentity(await users.CreateAsync(user));
        string role = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        EnsureIdentity(await users.AddToRoleAsync(user, role));
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, UserId = user.Id, DisplayName = ValidateName(command.Name), Active = false };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, OrgUnitId = command.DepartmentId, PositionId = command.PositionId,
            TeamId = command.TeamId, ManagerEmployeeId = command.ManagerId, RoleId = command.RoleId, Scope = command.Scope });
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        EmployeeInvitation invitation = new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, TokenHash = HashToken(token), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        db.EmployeeInvitations.Add(invitation);
        AddAudit(db, context, subject, "EmployeeInvited", "Employee", employee.Id,
            new { employee.DisplayName, command.DepartmentId, command.PositionId, Role = role, command.Scope }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(invitation.Id, token);
    }

    public async Task ChangeAssignmentAsync(Subject subject, ChangeAssignment command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        Employee employee = await db.Employees.SingleOrDefaultAsync(item => item.Id == command.EmployeeId && item.OrganizationId == context.OrganizationId, cancellationToken) ?? throw new AccessDeniedException();
        EmployeeAssignment assignment = await db.EmployeeAssignments.SingleAsync(item => item.EmployeeId == employee.Id, cancellationToken);
        if (assignment.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Назначение уже изменено. Обновите страницу.");
        if (assignment.RoleId != command.RoleId || assignment.Scope != command.Scope) await access.RequireAsync(subject, Permissions.RolesManage, cancellationToken);
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId, command.RoleId, command.Scope, subject, employee.Active, cancellationToken);
        if (command.ManagerId == employee.Id) throw new ArgumentException("Сотрудник не может быть собственным руководителем.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        string oldRole = await db.Roles.Where(item => item.Id == assignment.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        string newRole = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        if (oldRole == "Owner" && newRole != "Owner" && await IsLastActiveOwnerAsync(db, employee.Id, context.OrganizationId, cancellationToken)) throw new ArgumentException("Нельзя снять роль последнего активного Owner.");
        var before = new { assignment.OrgUnitId, assignment.PositionId, assignment.TeamId, assignment.ManagerEmployeeId, Role = oldRole, assignment.Scope };
        assignment.OrgUnitId = command.DepartmentId; assignment.PositionId = command.PositionId; assignment.TeamId = command.TeamId;
        assignment.ManagerEmployeeId = command.ManagerId; assignment.RoleId = command.RoleId; assignment.Scope = command.Scope;
        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        if (oldRole != newRole)
        {
            EnsureIdentity(await users.RemoveFromRoleAsync(user, oldRole)); EnsureIdentity(await users.AddToRoleAsync(user, newRole));
            EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        }
        AddAudit(db, context, subject, "AssignmentChanged", "Employee", employee.Id, new { Before = before, After = command }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AccessContext> RequireOrganizationAdminAsync(Subject subject, string permission, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, permission, cancellationToken);
        return context.Scope == AccessScope.Organization ? context : throw new AccessDeniedException();
    }

    private static IQueryable<EmployeeAssignment> ActiveAssignments(LandErpDbContext db, Guid organizationId) =>
        from assignment in db.EmployeeAssignments
        join employee in db.Employees on assignment.EmployeeId equals employee.Id
        where employee.OrganizationId == organizationId && employee.Active
        select assignment;

    private static async Task ValidateManagerAsync(LandErpDbContext db, AccessContext context, Guid? manager, CancellationToken cancellationToken)
    {
        if (manager != null && !await db.Employees.AnyAsync(item => item.Id == manager && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)) throw new AccessDeniedException();
    }

    private static async Task ValidateAssignmentAsync(LandErpDbContext db, AccessContext context,
        Guid? department, Guid? position, Guid? team, Guid? manager, Guid role, AccessScope assignmentScope,
        Subject subject, bool requireActiveReferences, CancellationToken cancellationToken)
    {
        bool validDepartment = department == null || await db.OrgUnits.AnyAsync(item => item.Id == department && item.OrganizationId == context.OrganizationId && (!requireActiveReferences || item.Active), cancellationToken);
        bool validPosition = position == null || await db.Positions.AnyAsync(item => item.Id == position && item.OrganizationId == context.OrganizationId && (!requireActiveReferences || item.Active), cancellationToken);
        bool validTeam = team == null || await db.Teams.AnyAsync(item => item.Id == team && item.OrganizationId == context.OrganizationId && item.OrgUnitId == department && (!requireActiveReferences || item.Active), cancellationToken);
        if (!Enum.IsDefined(assignmentScope) || !validDepartment || !validPosition || !validTeam
            || manager != null && !await db.Employees.AnyAsync(item => item.Id == manager && item.OrganizationId == context.OrganizationId && item.Active, cancellationToken)
            || !await db.Roles.AnyAsync(item => item.Id == role, cancellationToken)) throw new AccessDeniedException();
        if (assignmentScope == AccessScope.Department && department == null || assignmentScope == AccessScope.Team && team == null)
            throw new ArgumentException("Для выбранной области требуется подразделение или команда.");
        string roleName = await db.Roles.Where(item => item.Id == role).Select(item => item.Name!).SingleAsync(cancellationToken);
        if (roleName == "Owner" && !await (from employee in db.Employees join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
            join currentRole in db.Roles on assignment.RoleId equals currentRole.Id where employee.UserId == subject.UserId && currentRole.Name == "Owner" select employee.Id).AnyAsync(cancellationToken))
            throw new AccessDeniedException();
    }

    private static async Task<bool> IsLastActiveOwnerAsync(LandErpDbContext db, Guid employeeId, Guid organizationId, CancellationToken cancellationToken)
    {
        bool targetOwner = await (from employee in db.Employees join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
            join role in db.Roles on assignment.RoleId equals role.Id where employee.Id == employeeId && role.Name == "Owner" select employee.Id).AnyAsync(cancellationToken);
        if (!targetOwner) return false;
        int owners = await (from employee in db.Employees join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
            join role in db.Roles on assignment.RoleId equals role.Id where employee.OrganizationId == organizationId && employee.Active && role.Name == "Owner" select employee.Id).CountAsync(cancellationToken);
        return owners <= 1;
    }

    internal static string ValidateName(string name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200
        ? throw new ArgumentException("Укажите название длиной от 1 до 200 символов.") : name.Trim();
    internal static string ValidateLogin(string login) => string.IsNullOrWhiteSpace(login) || login.Trim().Length is < 3 or > 256
        ? throw new ArgumentException("Укажите уникальный логин длиной от 3 до 256 символов.") : login.Trim();
    private static string Optional(string value, int max) => value?.Trim() is { } text && text.Length <= max ? text : throw new ArgumentException($"Текст не должен превышать {max} символов.");

    internal static string GenerateTemporaryPassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz", upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", digits = "23456789", symbols = "!@$%*-_";
        char[] result = new char[16]; result[0] = Pick(upper); result[1] = Pick(lower); result[2] = Pick(digits); result[3] = Pick(symbols);
        string all = lower + upper + digits + symbols;
        for (int index = 4; index < result.Length; index++) result[index] = Pick(all);
        for (int index = result.Length - 1; index > 0; index--)
        {
            int target = RandomNumberGenerator.GetInt32(index + 1);
            (result[index], result[target]) = (result[target], result[index]);
        }
        return new string(result);
        static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
    }

    internal static void EnsureIdentity(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException("Не удалось сохранить учётную запись. Проверьте логин, уникальность и требования пароля.");
    }

    internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    internal static void AddAudit(LandErpDbContext db, AccessContext context, Subject subject,
        string action, string type, Guid id, object changes, string correlationId) =>
        db.AuditEvents.Add(new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            ActorId = subject.UserId, Action = action, ObjectType = type, ObjectId = id,
            Changes = JsonSerializer.Serialize(changes), RecordedAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId.Length <= 64 ? correlationId : DataConventions.NewId().ToString() });
}
