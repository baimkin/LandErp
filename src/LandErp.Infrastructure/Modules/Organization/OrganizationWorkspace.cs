using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Application.Modules.Organization.Contracts;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LandErp.Infrastructure.Modules.Organization;

public sealed class OrganizationWorkspace(
    IAccessControl access,
    IEmployeeAccessService employeeAccess,
    IDbContextFactory<LandErpDbContext> factory,
    IServiceScopeFactory scopes) : IOrganizationWorkspace
{
    public OrganizationWorkspace(IAccessControl access, IDbContextFactory<LandErpDbContext> factory,
        IServiceScopeFactory scopes)
        : this(access, new EmployeeAccessService(factory), factory, scopes) { }

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
        Guid[] visibleEmployeeIds = visibleEmployees.Select(item => item.employee.Id).ToArray();
        EffectiveEmployeeAccess[] effectiveAccess = (await employeeAccess.ResolveEmployeesAsync(
            context.OrganizationId, visibleEmployeeIds, cancellationToken)).ToArray();
        Dictionary<Guid, EffectiveEmployeeAccess> accessByEmployee =
            effectiveAccess.ToDictionary(item => item.EmployeeId);
        Dictionary<Guid, long> accessVersions = visibleEmployeeIds.Length == 0
            ? []
            : await db.EmployeeAccessSettings.AsNoTracking()
                .Where(item => visibleEmployeeIds.Contains(item.EmployeeId))
                .ToDictionaryAsync(item => item.EmployeeId, item => item.Version, cancellationToken);
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
            visibleEmployees.Select(item =>
            {
                EffectiveEmployeeAccess effective = accessByEmployee[item.employee.Id];
                return new EmployeeView(item.employee.Id, item.employee.DisplayName, item.user.UserName!,
                    item.employee.Active ? EmployeeState.Active : item.user.PasswordHash == null ? EmployeeState.PendingActivation : EmployeeState.Disabled,
                    item.user.MustChangePassword, item.assignment.OrgUnitId, item.assignment.PositionId, item.assignment.TeamId,
                    item.assignment.ManagerEmployeeId, item.assignment.RoleId, item.role.Name!, item.assignment.Scope,
                    item.assignment.Version, item.employee.Version,
                    new EmployeeAccessView(item.employee.Id, effective.Settings, effective.Source,
                        accessVersions.TryGetValue(item.employee.Id, out long accessVersion) ? accessVersion : null,
                        effective.IsSystemOwner));
            }).ToArray());
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
        string role = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        EmployeeAccessConfiguration explicitAccess = command.Access ?? EmployeeAccessRules.NoAccess;
        employeeAccess.Validate(explicitAccess);
        if (!string.Equals(role, "Owner", StringComparison.Ordinal))
            ValidateAccessScopeReferences(command.DepartmentId, command.TeamId, explicitAccess);
        string login = ValidateLogin(command.Login); string password = GenerateTemporaryPassword();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, MustChangePassword = command.MustChangePassword, LockoutEnabled = true };
        EnsureIdentity(await users.CreateAsync(user, password));
        EnsureIdentity(await users.AddToRoleAsync(user, role));
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, UserId = user.Id, DisplayName = ValidateName(command.Name), Active = true };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, OrgUnitId = command.DepartmentId,
            PositionId = command.PositionId, TeamId = command.TeamId, ManagerEmployeeId = command.ManagerId, RoleId = command.RoleId, Scope = command.Scope });
        if (!string.Equals(role, "Owner", StringComparison.Ordinal))
            db.EmployeeAccessSettings.Add(ToAccessSettings(employee.Id, explicitAccess));
        AddAudit(db, context, subject, "EmployeeCreated", "Employee", employee.Id,
            new { employee.DisplayName, Login = login, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId,
                Role = role, command.Scope, Access = string.Equals(role, "Owner", StringComparison.Ordinal) ? (object)"SystemOwner" : explicitAccess,
                command.MustChangePassword }, correlationId);
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

    public async Task<EmployeeAccessView> ReadEmployeeAccessAsync(Subject subject, Guid employeeId,
        CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return await BuildEmployeeAccessViewAsync(db, context.OrganizationId, employeeId, cancellationToken);
    }

    public async Task<EmployeeAccessView> SaveEmployeeAccessAsync(Subject subject, SaveEmployeeAccess command,
        string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command.Settings);
        employeeAccess.Validate(command.Settings);
        AccessContext context = await RequireAccessAdminAsync(subject, cancellationToken);

        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);

        EffectiveEmployeeAccess before = (await employeeAccess.ResolveEmployeesAsync(
            context.OrganizationId, [command.EmployeeId], cancellationToken)).SingleOrDefault()
            ?? throw new AccessDeniedException();

        var target = await (from employee in db.Employees
                            join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
                            join role in db.Roles on assignment.RoleId equals role.Id
                            where employee.Id == command.EmployeeId && employee.OrganizationId == context.OrganizationId
                            select new { Employee = employee, Assignment = assignment, RoleName = role.Name! })
            .SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();

        if (string.Equals(target.RoleName, "Owner", StringComparison.Ordinal))
            throw new ArgumentException("Доступ Owner системный и не может быть ограничен настройками Access V1.");
        ValidateAccessScopeReferences(target.Assignment.OrgUnitId, target.Assignment.TeamId, command.Settings);

        EmployeeAccessSettings? row = await db.EmployeeAccessSettings
            .SingleOrDefaultAsync(item => item.EmployeeId == command.EmployeeId, cancellationToken);
        long? beforeExplicitVersion = row?.Version;
        if (row == null)
        {
            if (command.ExpectedVersion != null)
                throw new DbUpdateConcurrencyException("Настройки доступа уже изменились. Обновите карточку сотрудника.");
            row = new EmployeeAccessSettings { EmployeeId = command.EmployeeId };
            db.EmployeeAccessSettings.Add(row);
        }
        else if (command.ExpectedVersion != row.Version)
        {
            throw new DbUpdateConcurrencyException("Настройки доступа уже изменились. Обновите карточку сотрудника.");
        }

        await ValidateAccessChangeAgainstActiveWorkAsync(db, target.Employee,
            target.Assignment.OrgUnitId, target.Assignment.TeamId, command.Settings, cancellationToken);

        row.IncomingAccess = command.Settings.IncomingAccess;
        row.ProcurementAccess = command.Settings.ProcurementAccess;
        row.ProcurementReadScope = command.Settings.ProcurementReadScope;
        row.ProcurementWorkScope = command.Settings.ProcurementWorkScope;
        row.CollectionAccess = command.Settings.CollectionAccess;
        row.CanAssignInspections = command.Settings.CanAssignInspections;
        row.CanPerformInspections = command.Settings.CanPerformInspections;
        row.CanConfirmPurchase = command.Settings.CanConfirmPurchase;
        row.CanManageTemplates = command.Settings.CanManageTemplates;
        row.CanReadAudit = command.Settings.CanReadAudit;

        AddAudit(db, context, subject, "EmployeeAccessChanged", "Employee", command.EmployeeId,
            new
            {
                Before = before.Settings,
                After = command.Settings,
                BeforeSource = before.Source.ToString(),
                BeforeExplicitVersion = beforeExplicitVersion
            }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using LandErpDbContext readDb = await factory.CreateDbContextAsync(cancellationToken);
        return await BuildEmployeeAccessViewAsync(readDb, context.OrganizationId, command.EmployeeId, cancellationToken);
    }

    public async Task<EmployeeWorkImpact> ReadEmployeeWorkImpactAsync(Subject subject, Guid employeeId,
        CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        return (await BuildEmployeeWorkStateAsync(db, context.OrganizationId, employeeId, cancellationToken)).View;
    }

    public async Task TransferEmployeeWorkAsync(Subject subject, TransferEmployeeWork command, string correlationId,
        CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        EmployeeWorkState state = await BuildEmployeeWorkStateAsync(
            db, context.OrganizationId, command.EmployeeId, cancellationToken);
        await TransferEmployeeWorkInternalAsync(db, context, subject, state, command.RecipientEmployeeId,
            correlationId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetEmployeeActiveAsync(Subject subject, SetEmployeeActive command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await LockOwnerInvariantAsync(db, context.OrganizationId, cancellationToken);

        Employee employee = await db.Employees.SingleOrDefaultAsync(
            item => item.Id == command.EmployeeId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        if (employee.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Сотрудник уже изменён. Обновите страницу.");
        if (command.Active && (command.HandoverEmployeeId != null || command.EmergencyRevoke))
            throw new ArgumentException("Переназначение используется только при отключении сотрудника.");
        if (!command.Active && command.HandoverEmployeeId != null && command.EmergencyRevoke)
            throw new ArgumentException("Выберите либо переназначение, либо срочный отзыв доступа без переназначения.");
        if (!command.Active && employee.UserId == subject.UserId) throw new ArgumentException("Нельзя отключить собственную учётную запись.");
        if (!command.Active && await IsLastActiveOwnerAsync(db, employee.Id, context.OrganizationId, cancellationToken))
            throw new ArgumentException("Нельзя отключить последнего активного Owner.");

        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        if (command.Active && user.PasswordHash == null)
            throw new ArgumentException("Сначала завершите активацию приглашённого сотрудника или создайте ему прямую учётную запись.");

        EmployeeWorkImpact? impact = null;
        if (!command.Active)
        {
            EmployeeWorkState state = await BuildEmployeeWorkStateAsync(db, context.OrganizationId, employee.Id, cancellationToken);
            impact = state.View;
            if (impact.HasWork)
            {
                if (command.HandoverEmployeeId is Guid recipientId)
                {
                    await TransferEmployeeWorkInternalAsync(db, context, subject, state, recipientId,
                        correlationId, cancellationToken);
                }
                else if (!command.EmergencyRevoke)
                {
                    throw new ArgumentException("У сотрудника есть активная работа. Выберите получателя или используйте явный срочный отзыв доступа.");
                }
                else
                {
                    RecordHandoverPending(db, context, subject, state, correlationId);
                }
            }
        }

        employee.Active = command.Active;
        EnsureIdentity(await users.SetLockoutEndDateAsync(user, command.Active ? null : DateTimeOffset.MaxValue));
        EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        AddAudit(db, context, subject, command.Active ? "EmployeeRestored" : "EmployeeDeactivated", "Employee", employee.Id,
            new
            {
                employee.DisplayName,
                command.HandoverEmployeeId,
                command.EmergencyRevoke,
                AffectedCases = impact?.AffectedCases ?? 0,
                OpenTasks = impact?.OpenTasks ?? 0,
                OpenChecks = impact?.OpenChecks ?? 0,
                OpenInspections = impact?.OpenInspections ?? 0,
                PendingApprovals = impact?.PendingApprovals ?? 0
            }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InvitationResult> InviteAsync(Subject subject, InviteEmployee command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId, command.ManagerId, command.RoleId, command.Scope, subject, true, cancellationToken);
        string role = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        EmployeeAccessConfiguration explicitAccess = command.Access ?? EmployeeAccessRules.NoAccess;
        employeeAccess.Validate(explicitAccess);
        if (!string.Equals(role, "Owner", StringComparison.Ordinal))
            ValidateAccessScopeReferences(command.DepartmentId, command.TeamId, explicitAccess);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string login = ValidateLogin(command.Login);
        LandErpUser user = new() { Id = DataConventions.NewId(), UserName = login, Email = login, MustChangePassword = false };
        EnsureIdentity(await users.CreateAsync(user));
        EnsureIdentity(await users.AddToRoleAsync(user, role));
        Employee employee = new() { Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, UserId = user.Id, DisplayName = ValidateName(command.Name), Active = false };
        db.Employees.Add(employee);
        db.EmployeeAssignments.Add(new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, OrgUnitId = command.DepartmentId, PositionId = command.PositionId,
            TeamId = command.TeamId, ManagerEmployeeId = command.ManagerId, RoleId = command.RoleId, Scope = command.Scope });
        if (!string.Equals(role, "Owner", StringComparison.Ordinal))
            db.EmployeeAccessSettings.Add(ToAccessSettings(employee.Id, explicitAccess));
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        EmployeeInvitation invitation = new() { Id = DataConventions.NewId(), EmployeeId = employee.Id, TokenHash = HashToken(token), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) };
        db.EmployeeInvitations.Add(invitation);
        AddAudit(db, context, subject, "EmployeeInvited", "Employee", employee.Id,
            new { employee.DisplayName, command.DepartmentId, command.PositionId, Role = role, command.Scope,
                Access = string.Equals(role, "Owner", StringComparison.Ordinal) ? (object)"SystemOwner" : explicitAccess }, correlationId);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(invitation.Id, token);
    }

    public async Task ChangeAssignmentAsync(Subject subject, ChangeAssignment command, string correlationId, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LandErpDbContext db = scope.ServiceProvider.GetRequiredService<LandErpDbContext>();
        UserManager<LandErpUser> users = scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>();
        // The organization-scoped advisory lock is the serialization boundary. ReadCommitted
        // is intentional: after waiting for the lock this transaction must observe the winner's
        // committed Owner change instead of keeping a pre-lock Serializable snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await LockOwnerInvariantAsync(db, context.OrganizationId, cancellationToken);

        Employee employee = await db.Employees.SingleOrDefaultAsync(
            item => item.Id == command.EmployeeId && item.OrganizationId == context.OrganizationId, cancellationToken)
            ?? throw new AccessDeniedException();
        EmployeeAssignment assignment = await db.EmployeeAssignments.SingleAsync(item => item.EmployeeId == employee.Id, cancellationToken);
        if (assignment.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException("Назначение уже изменено. Обновите страницу.");
        if (assignment.RoleId != command.RoleId || assignment.Scope != command.Scope)
            await access.RequireAsync(subject, Permissions.RolesManage, cancellationToken);
        await ValidateAssignmentAsync(db, context, command.DepartmentId, command.PositionId, command.TeamId,
            command.ManagerId, command.RoleId, command.Scope, subject, employee.Active, cancellationToken);
        if (command.ManagerId == employee.Id) throw new ArgumentException("Сотрудник не может быть собственным руководителем.");

        string oldRole = await db.Roles.Where(item => item.Id == assignment.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        string newRole = await db.Roles.Where(item => item.Id == command.RoleId).Select(item => item.Name!).SingleAsync(cancellationToken);
        if (oldRole == "Owner" && newRole != "Owner"
            && await IsLastActiveOwnerAsync(db, employee.Id, context.OrganizationId, cancellationToken))
            throw new ArgumentException("Нельзя снять роль последнего активного Owner.");

        if (newRole != "Owner")
        {
            EmployeeAccessSettings? explicitSettings = await db.EmployeeAccessSettings
                .SingleOrDefaultAsync(item => item.EmployeeId == employee.Id, cancellationToken);
            EmployeeAccessConfiguration effectiveSettings = explicitSettings == null
                ? EmployeeAccessRules.NoAccess
                : ToConfiguration(explicitSettings);
            employeeAccess.Validate(effectiveSettings);
            ValidateAccessScopeReferences(command.DepartmentId, command.TeamId, effectiveSettings);
            await ValidateAccessChangeAgainstActiveWorkAsync(db, employee, command.DepartmentId, command.TeamId,
                effectiveSettings, cancellationToken);
            if (explicitSettings == null)
                db.EmployeeAccessSettings.Add(ToAccessSettings(employee.Id, effectiveSettings));
        }

        var before = new { assignment.OrgUnitId, assignment.PositionId, assignment.TeamId, assignment.ManagerEmployeeId, Role = oldRole, assignment.Scope };
        assignment.OrgUnitId = command.DepartmentId;
        assignment.PositionId = command.PositionId;
        assignment.TeamId = command.TeamId;
        assignment.ManagerEmployeeId = command.ManagerId;
        assignment.RoleId = command.RoleId;
        assignment.Scope = command.Scope;

        LandErpUser user = await users.FindByIdAsync(employee.UserId.ToString()) ?? throw new AccessDeniedException();
        if (oldRole != newRole)
        {
            EnsureIdentity(await users.RemoveFromRoleAsync(user, oldRole));
            EnsureIdentity(await users.AddToRoleAsync(user, newRole));
            EnsureIdentity(await users.UpdateSecurityStampAsync(user));
        }
        AddAudit(db, context, subject, "AssignmentChanged", "Employee", employee.Id,
            new { Before = before, After = command }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record CaseWorkImpact(PropertyCase Case, Guid CaseAssigneeEmployeeId,
        bool Manager, bool Assignment, bool OpenTask, int OpenChecks, bool PendingApproval);

    private sealed record EmployeeWorkState(Employee Source, CaseWorkImpact[] Cases,
        SiteInspection[] Inspections, EmployeeHandoverCandidate[] Candidates)
    {
        public EmployeeWorkImpact View => new(Source.Id, Source.DisplayName, Cases.Length,
            Cases.Count(item => item.Manager), Cases.Count(item => item.Assignment),
            Cases.Count(item => item.OpenTask), Cases.Sum(item => item.OpenChecks),
            Inspections.Length, Cases.Count(item => item.PendingApproval), Candidates);
    }

    private async Task<EmployeeWorkState> BuildEmployeeWorkStateAsync(LandErpDbContext db, Guid organizationId,
        Guid employeeId, CancellationToken cancellationToken)
    {
        Employee source = await db.Employees.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == employeeId && item.OrganizationId == organizationId, cancellationToken)
            ?? throw new AccessDeniedException();

        var caseRows = await (from propertyCase in db.PropertyCases.AsNoTracking()
                              join assignment in db.WorkAssignments.AsNoTracking() on propertyCase.AssignmentId equals assignment.Id
                              join task in db.WorkTasks.AsNoTracking() on propertyCase.WorkTaskId equals task.Id
                              where propertyCase.OrganizationId == organizationId
                                  && propertyCase.StageId != "acquired" && propertyCase.StageId != "rejected"
                              select new
                              {
                                  Case = propertyCase,
                                  AssigneeEmployeeId = assignment.EmployeeId,
                                  TaskEmployeeId = task.EmployeeId,
                                  task.Completed
                              }).ToArrayAsync(cancellationToken);

        var openCheckRows = await db.CaseChecks.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.ResponsibleEmployeeId == employeeId
                && item.Status != CaseCheckStatus.Passed && item.Status != CaseCheckStatus.Issue)
            .GroupBy(item => item.PropertyCaseId)
            .Select(group => new { CaseId = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, int> openChecks = openCheckRows.ToDictionary(item => item.CaseId, item => item.Count);

        CaseWorkImpact[] impacts = caseRows.Select(row =>
        {
            int checks = openChecks.GetValueOrDefault(row.Case.Id);
            bool assignment = row.AssigneeEmployeeId == employeeId;
            return new CaseWorkImpact(row.Case, row.AssigneeEmployeeId,
                row.Case.ManagerEmployeeId == employeeId, assignment,
                row.TaskEmployeeId == employeeId && !row.Completed, checks,
                row.Case.PendingApprovalId != null && assignment);
        }).Where(item => item.Manager || item.Assignment || item.OpenTask || item.OpenChecks > 0 || item.PendingApproval)
          .ToArray();

        SiteInspection[] inspections = await db.SiteInspections.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.InspectorEmployeeId == employeeId
                && item.Status != InspectionStatus.Completed)
            .ToArrayAsync(cancellationToken);
        if (impacts.Length == 0 && inspections.Length == 0) return new(source, impacts, inspections, []);

        EffectiveEmployeeAccess[] effectiveCandidates = (await employeeAccess.ResolveActiveEmployeesAsync(
            organizationId, cancellationToken))
            .Where(item => item.EmployeeId != employeeId)
            .Where(item => CanReceiveAllWork(item, impacts, inspections.Length > 0))
            .ToArray();
        Guid[] candidateIds = effectiveCandidates.Select(item => item.EmployeeId).ToArray();
        EmployeeHandoverCandidate[] candidates = await db.Employees.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && candidateIds.Contains(item.Id))
            .OrderBy(item => item.DisplayName)
            .Select(item => new EmployeeHandoverCandidate(item.Id, item.DisplayName))
            .ToArrayAsync(cancellationToken);
        return new(source, impacts, inspections, candidates);
    }

    private static bool CanReceiveAllWork(EffectiveEmployeeAccess candidate,
        CaseWorkImpact[] impacts, bool hasInspections)
    {
        if (hasInspections && !candidate.Settings.CanPerformInspections) return false;
        foreach (CaseWorkImpact impact in impacts)
        {
            if (!candidate.CanManageProcurement) return false;
            Guid finalManager = impact.Manager ? candidate.EmployeeId : impact.Case.ManagerEmployeeId;
            Guid finalAssignee = impact.Assignment ? candidate.EmployeeId : impact.CaseAssigneeEmployeeId;
            if (!ProcurementVisibility.CanSeeAfterResponsibility(
                impact.Case, candidate.ProcurementWorkContext, finalManager, finalAssignee))
                return false;
            if (impact.Assignment && impact.Case.StageId == "pending_head" && !candidate.CanHeadProcurement)
                return false;
            if (impact.PendingApproval && finalManager == candidate.EmployeeId) return false;
        }
        return true;
    }

    private static async Task TransferEmployeeWorkInternalAsync(LandErpDbContext db, AccessContext context, Subject subject,
        EmployeeWorkState state, Guid recipientEmployeeId, string correlationId, CancellationToken cancellationToken)
    {
        if (!state.View.HasWork) return;
        if (!state.Candidates.Any(item => item.EmployeeId == recipientEmployeeId))
            throw new ArgumentException("Выбранный сотрудник не может получить всю активную работу с учётом прав и области доступа.");

        Employee recipient = await db.Employees.SingleAsync(
            item => item.Id == recipientEmployeeId && item.OrganizationId == context.OrganizationId && item.Active,
            cancellationToken);
        Guid[] caseIds = state.Cases.Select(item => item.Case.Id).ToArray();
        PropertyCase[] cases = await db.PropertyCases.Where(item => caseIds.Contains(item.Id)).ToArrayAsync(cancellationToken);
        Guid[] assignmentIds = cases.Select(item => item.AssignmentId).ToArray();
        Guid[] taskIds = cases.Select(item => item.WorkTaskId).ToArray();
        Assignment[] assignments = await db.WorkAssignments.Where(item => assignmentIds.Contains(item.Id)).ToArrayAsync(cancellationToken);
        WorkTask[] tasks = await db.WorkTasks.Where(item => taskIds.Contains(item.Id)).ToArrayAsync(cancellationToken);
        CaseCheck[] checks = await db.CaseChecks.Where(item => caseIds.Contains(item.PropertyCaseId)
                && item.ResponsibleEmployeeId == state.Source.Id
                && item.Status != CaseCheckStatus.Passed && item.Status != CaseCheckStatus.Issue)
            .ToArrayAsync(cancellationToken);
        Dictionary<Guid, PropertyCase> caseMap = cases.ToDictionary(item => item.Id);
        Dictionary<Guid, Assignment> assignmentMap = assignments.ToDictionary(item => item.Id);
        Dictionary<Guid, WorkTask> taskMap = tasks.ToDictionary(item => item.Id);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CaseWorkImpact impact in state.Cases)
        {
            PropertyCase propertyCase = caseMap[impact.Case.Id];
            Assignment assignment = assignmentMap[propertyCase.AssignmentId];
            WorkTask task = taskMap[propertyCase.WorkTaskId];
            List<string> responsibilities = [];

            if (impact.Manager && propertyCase.ManagerEmployeeId == state.Source.Id)
            {
                propertyCase.ManagerEmployeeId = recipientEmployeeId;
                responsibilities.Add("менеджер объекта");
            }
            if (impact.Assignment && assignment.EmployeeId == state.Source.Id)
            {
                assignment.EmployeeId = recipientEmployeeId;
                responsibilities.Add(impact.PendingApproval ? "ожидающее решение руководителя" : "текущий исполнитель");
            }
            if (impact.OpenTask && task.EmployeeId == state.Source.Id && !task.Completed)
            {
                task.EmployeeId = recipientEmployeeId;
                responsibilities.Add("следующее действие");
            }

            CaseCheck[] caseChecks = checks.Where(item => item.PropertyCaseId == propertyCase.Id).ToArray();
            foreach (CaseCheck check in caseChecks) check.ResponsibleEmployeeId = recipientEmployeeId;
            if (caseChecks.Length > 0) responsibilities.Add($"открытые проверки: {caseChecks.Length}");

            db.Entry(propertyCase).Property(item => item.Version).IsModified = true;
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = propertyCase.Id, ActorEmployeeId = context.EmployeeId, Kind = "ResponsibilityTransferred",
                Title = "Ответственность передана",
                Body = $"{state.Source.DisplayName} → {recipient.DisplayName}. {string.Join(", ", responsibilities)}.",
                TargetEmployeeId = recipientEmployeeId, RecordedAt = now
            });
            db.Notifications.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, EmployeeId = recipientEmployeeId,
                ObjectType = "PropertyCase", ObjectId = propertyCase.Id,
                Title = $"{propertyCase.BusinessNumber}: передана активная работа", RecordedAt = now
            });
        }

        Guid[] inspectionIds = state.Inspections.Select(item => item.Id).ToArray();
        SiteInspection[] inspections = inspectionIds.Length == 0 ? [] : await db.SiteInspections
            .Where(item => inspectionIds.Contains(item.Id) && item.Status != InspectionStatus.Completed)
            .ToArrayAsync(cancellationToken);
        foreach (SiteInspection inspection in inspections)
        {
            inspection.InspectorEmployeeId = recipientEmployeeId;
            inspection.RequestedByEmployeeId = context.EmployeeId;
            inspection.RequestedAt = now;
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = inspection.PropertyCaseId, ActorEmployeeId = context.EmployeeId, Kind = "InspectionReassigned",
                Title = "Осмотр переназначен при передаче работы",
                Body = $"{state.Source.DisplayName} → {recipient.DisplayName}.",
                TargetEmployeeId = recipientEmployeeId, DueAt = inspection.DueAt, RecordedAt = now
            });
            db.Notifications.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, EmployeeId = recipientEmployeeId,
                ObjectType = "PropertyCase", ObjectId = inspection.PropertyCaseId,
                Title = "Назначен незавершённый осмотр после передачи работы", RecordedAt = now
            });
        }

        AddAudit(db, context, subject, "EmployeeWorkTransferred", "Employee", state.Source.Id,
            new
            {
                RecipientEmployeeId = recipientEmployeeId,
                Recipient = recipient.DisplayName,
                state.View.AffectedCases,
                state.View.ManagedCases,
                state.View.AssignedCases,
                state.View.OpenTasks,
                state.View.OpenChecks,
                state.View.OpenInspections,
                state.View.PendingApprovals
            }, correlationId);
    }

    private static void RecordHandoverPending(LandErpDbContext db, AccessContext context, Subject subject,
        EmployeeWorkState state, string correlationId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (CaseWorkImpact impact in state.Cases)
        {
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = impact.Case.Id, ActorEmployeeId = context.EmployeeId, Kind = "ResponsibilityHandoverPending",
                Title = "Доступ сотрудника отключён, работа требует переназначения",
                Body = $"Доступ {state.Source.DisplayName} отозван срочно. Текущие ссылки ответственности сохранены до явной передачи.",
                RecordedAt = now
            });
        }
        Guid[] caseIdsWithTimeline = state.Cases.Select(item => item.Case.Id).ToArray();
        foreach (Guid caseId in state.Inspections.Select(item => item.PropertyCaseId).Distinct()
            .Where(item => !caseIdsWithTimeline.Contains(item)))
        {
            db.BusinessTimeline.Add(new()
            {
                Id = DataConventions.NewId(), OrganizationId = context.OrganizationId, ObjectType = "PropertyCase",
                ObjectId = caseId, ActorEmployeeId = context.EmployeeId, Kind = "InspectionHandoverPending",
                Title = "Осмотр требует переназначения",
                Body = $"Доступ {state.Source.DisplayName} отозван срочно. Незавершённый осмотр остался без активного исполнителя.",
                RecordedAt = now
            });
        }
        AddAudit(db, context, subject, "EmployeeWorkHandoverPending", "Employee", state.Source.Id,
            new
            {
                state.View.AffectedCases,
                state.View.ManagedCases,
                state.View.AssignedCases,
                state.View.OpenTasks,
                state.View.OpenChecks,
                state.View.OpenInspections,
                state.View.PendingApprovals
            }, correlationId);
    }

    private async Task<EmployeeAccessView> BuildEmployeeAccessViewAsync(LandErpDbContext db, Guid organizationId,
        Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await db.Employees.AsNoTracking().AnyAsync(item => item.Id == employeeId
            && item.OrganizationId == organizationId, cancellationToken))
            throw new AccessDeniedException();

        EffectiveEmployeeAccess effective = (await employeeAccess.ResolveEmployeesAsync(
            organizationId, [employeeId], cancellationToken)).Single();
        long? version = await db.EmployeeAccessSettings.AsNoTracking()
            .Where(item => item.EmployeeId == employeeId)
            .Select(item => (long?)item.Version)
            .SingleOrDefaultAsync(cancellationToken);
        return new(employeeId, effective.Settings, effective.Source, version, effective.IsSystemOwner);
    }

    private static EmployeeAccessSettings ToAccessSettings(Guid employeeId, EmployeeAccessConfiguration settings) =>
        new()
        {
            EmployeeId = employeeId,
            IncomingAccess = settings.IncomingAccess,
            ProcurementAccess = settings.ProcurementAccess,
            ProcurementReadScope = settings.ProcurementReadScope,
            ProcurementWorkScope = settings.ProcurementWorkScope,
            CollectionAccess = settings.CollectionAccess,
            CanAssignInspections = settings.CanAssignInspections,
            CanPerformInspections = settings.CanPerformInspections,
            CanConfirmPurchase = settings.CanConfirmPurchase,
            CanManageTemplates = settings.CanManageTemplates,
            CanReadAudit = settings.CanReadAudit
        };

    private static EmployeeAccessConfiguration ToConfiguration(EmployeeAccessSettings value) =>
        new(value.IncomingAccess, value.ProcurementAccess, value.ProcurementReadScope, value.ProcurementWorkScope,
            value.CollectionAccess, value.CanAssignInspections, value.CanPerformInspections,
            value.CanConfirmPurchase, value.CanManageTemplates, value.CanReadAudit);

    private static void ValidateAccessScopeReferences(
        Guid? departmentId, Guid? teamId, EmployeeAccessConfiguration settings)
    {
        if ((settings.ProcurementReadScope == AccessScope.Team || settings.ProcurementWorkScope == AccessScope.Team)
            && teamId == null)
            throw new ArgumentException("Для области «Команда» сотрудник должен быть назначен в команду.");
        if ((settings.ProcurementReadScope == AccessScope.Department || settings.ProcurementWorkScope == AccessScope.Department)
            && departmentId == null)
            throw new ArgumentException("Для области «Отдел» сотрудник должен быть назначен в отдел.");
    }

    private async Task ValidateAccessChangeAgainstActiveWorkAsync(
        LandErpDbContext db,
        Employee employee,
        Guid? departmentId,
        Guid? teamId,
        EmployeeAccessConfiguration proposed,
        CancellationToken cancellationToken)
    {
        EmployeeWorkState state = await BuildEmployeeWorkStateAsync(
            db, employee.OrganizationId, employee.Id, cancellationToken);

        if (state.Inspections.Length > 0 && !proposed.CanPerformInspections)
            throw new ArgumentException(
                "У сотрудника есть незавершённые осмотры. Сначала переназначьте активную работу, затем отключайте право выполнять осмотры.");

        if (state.Cases.Length == 0) return;
        if (proposed.ProcurementAccess < ProcurementAccessLevel.Manager)
            throw new ArgumentException(
                "У сотрудника есть активная работа в закупке. Сначала переназначьте её, затем уменьшайте доступ к закупке.");

        AccessContext proposedWork = new(employee.Id, employee.OrganizationId, departmentId,
            teamId, proposed.ProcurementWorkScope);
        foreach (CaseWorkImpact impact in state.Cases)
        {
            if (!ProcurementVisibility.CanSeeAfterResponsibility(
                impact.Case, proposedWork, impact.Case.ManagerEmployeeId, impact.CaseAssigneeEmployeeId))
            {
                throw new ArgumentException(
                    "Новая область работы не включает один или несколько активных объектов сотрудника. Сначала переназначьте активную работу.");
            }

            if (impact.Assignment && impact.Case.StageId == "pending_head"
                && proposed.ProcurementAccess < ProcurementAccessLevel.Head)
            {
                throw new ArgumentException(
                    "У сотрудника есть объект, ожидающий решения руководителя. Сначала передайте его другому руководителю.");
            }
        }
    }

    private async Task<AccessContext> RequireAccessAdminAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await RequireOrganizationAdminAsync(subject, Permissions.UsersManage, cancellationToken);
        AccessContext roleContext = await access.RequireAsync(subject, Permissions.RolesManage, cancellationToken);
        if (roleContext.OrganizationId != context.OrganizationId || roleContext.Scope != AccessScope.Organization)
            throw new AccessDeniedException();
        return context;
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
        if ((roleName is "Owner" or "Administrator") && assignmentScope != AccessScope.Organization)
            throw new ArgumentException("Для роли Owner или Administrator требуется область доступа «Организация».");
        if (roleName == "Owner" && !await (from employee in db.Employees join assignment in db.EmployeeAssignments on employee.Id equals assignment.EmployeeId
            join currentRole in db.Roles on assignment.RoleId equals currentRole.Id where employee.UserId == subject.UserId && currentRole.Name == "Owner" select employee.Id).AnyAsync(cancellationToken))
            throw new AccessDeniedException();
    }

    private static Task<int> LockOwnerInvariantAsync(LandErpDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        string key = "LastActiveOwner:" + organizationId.ToString("N");
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", cancellationToken);
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
