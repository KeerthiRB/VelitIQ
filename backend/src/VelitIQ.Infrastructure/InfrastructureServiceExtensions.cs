using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.Interfaces;
using VelitIQ.Infrastructure.Ai;
using VelitIQ.Infrastructure.Data;
using VelitIQ.Infrastructure.Ingestion;
using VelitIQ.Infrastructure.Parsers;
using VelitIQ.Infrastructure.Security;
using VelitIQ.Infrastructure.Storage;

namespace VelitIQ.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddVelitIQInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.SectionName));
        services.Configure<AzureFoundryOptions>(config.GetSection(AzureFoundryOptions.SectionName));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));

        services.AddSingleton<PostgresKnowledgeStore>();
        services.AddSingleton<SecretGuardrailScanner>();

        // Pluggable File Storage (LocalDisk for Windows Server / UNC share -> AzureBlob for Azure cloud cutover)
        var storageProvider = config.GetValue<string>("Storage:Provider") ?? "LocalDisk";
        if (storageProvider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorageService, AzureBlobFileStorageService>();
        }
        else
        {
            services.AddSingleton<IFileStorageService, LocalDiskFileStorageService>();
        }

        // Multi-Format Document Parsers (CSV, Excel + Formulas, Word OpenXML, PDF + OneNote Exports)
        services.AddSingleton<IDocumentFormatParser, CsvDocumentParser>();
        services.AddSingleton<IDocumentFormatParser, ExcelDocumentParser>();
        services.AddSingleton<IDocumentFormatParser, WordDocumentParser>();
        services.AddSingleton<IDocumentFormatParser, PdfDocumentParser>();

        // Pluggable AI Service (Azure AI Foundry via AzureOpenAiUrl + ApiKey, with seamless local laptop fallback)
        services.AddHttpClient<AzureFoundryAiService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90);
        });
        services.AddSingleton<LocalDeterministicAiService>();
        services.AddSingleton<IAiService, CompositeAiService>();

        // Auth & Ingestion Services
        services.AddSingleton<IAuthService, BankAuthService>();
        services.AddSingleton<KnowledgeIngestionService>();
        services.AddSingleton<BankDemoDataSeeder>();

        return services;
    }
}
