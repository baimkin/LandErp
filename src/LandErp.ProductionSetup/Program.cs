using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    if (args is not ["database"] and not ["owner"])
        throw new InvalidOperationException("Use exactly one production setup command: database or owner.");

    string migratorConnection = Required("LANDERP_MIGRATOR_CONNECTION");
    if (args[0] == "database")
    {
        await ProductionDatabaseInitializer.InitializeAsync(
            Required("LANDERP_PROVISIONER_CONNECTION"), migratorConnection,
            Required("LANDERP_RUNTIME_CONNECTION"));
        await SynchronizeBuiltInAccessAsync(migratorConnection);
        Console.WriteLine("Production database, migrations, built-in access catalog and restricted runtime grants are ready.");
        return;
    }

    string login = Required("LANDERP_INITIAL_OWNER_LOGIN");
    string password = Required("LANDERP_INITIAL_OWNER_PASSWORD");
    string organizationName = Required("LANDERP_INITIAL_ORGANIZATION_NAME");
    using IHost host = BuildSetupHost(migratorConnection);
    await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
    FirstOwnerBootstrapResult result = await OwnerBootstrap.CreateFirstOwnerAsync(
        scope.ServiceProvider.GetRequiredService<LandErpDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<LandErpUser>>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
        login, password, organizationName);
    Console.WriteLine(result == FirstOwnerBootstrapResult.Created
        ? "Initial production Owner created; password change and MFA enrollment are required at first login."
        : "Initial production Owner already exists; no changes were made.");
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    Console.Error.WriteLine("Production setup failed safely. Check connection boundaries, role/database ownership, empty first-Owner state and password policy; private provider details were suppressed.");
    Environment.ExitCode = 1;
}

static async Task SynchronizeBuiltInAccessAsync(string connectionString)
{
    using IHost host = BuildSetupHost(connectionString);
    await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
    await BuiltInAccessCatalog.SynchronizeAsync(
        scope.ServiceProvider.GetRequiredService<LandErpDbContext>(),
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
}

static IHost BuildSetupHost(string connectionString)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
    builder.Logging.ClearProviders();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Database:ConnectionString"] = connectionString
    });
    builder.Services.AddLandErpPersistence(builder.Configuration);
    builder.Services.AddLandErpIdentity();
    return builder.Build();
}

static string Required(string name)
{
    string value = Environment.GetEnvironmentVariable(name) ?? "";
    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Required secret input {name} is missing.");
    return value;
}
