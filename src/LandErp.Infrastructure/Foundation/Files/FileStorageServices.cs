using LandErp.Application.Foundation.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LandErp.Infrastructure.Foundation.Files;

public static class FileStorageServices
{
    public static IServiceCollection AddLandErpFileStorage(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        bool localEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Local") || environment.IsEnvironment("Test");
        string provider = configuration["Storage:Provider"] ?? (localEnvironment ? "Local" : "");
        if (provider is not ("Local" or "YandexDisk")) throw new InvalidOperationException("Выберите файловое хранилище: Local или YandexDisk.");
        if (provider == "Local" && !localEnvironment) throw new InvalidOperationException("Локальное файловое хранилище допустимо только для разработки и тестов.");
        string? localRoot = configuration["Storage:Root"];
        if (localEnvironment && string.IsNullOrWhiteSpace(localRoot)) localRoot = Path.Combine(environment.ContentRootPath, "local-data", "stage1", "files");
        FileSystemFileStorage? local = string.IsNullOrWhiteSpace(localRoot) ? null : new(localRoot);
        if (provider == "YandexDisk")
        {
            YandexDiskOptions options = new();
            configuration.GetSection("Storage:YandexDisk").Bind(options);
            options.Validate();
            services.AddSingleton(_ => new YandexDiskFileStorage(options));
            services.AddSingleton<RoutedFileStorage>(sp =>
            {
                YandexDiskFileStorage cloud = sp.GetRequiredService<YandexDiskFileStorage>();
                return new RoutedFileStorage(cloud, cloud, local);
            });
        }
        else services.AddSingleton(new RoutedFileStorage(local!, null, local));
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<RoutedFileStorage>());
        services.AddSingleton<IFileStorageHealth>(sp => sp.GetRequiredService<RoutedFileStorage>());
        return services;
    }
}
