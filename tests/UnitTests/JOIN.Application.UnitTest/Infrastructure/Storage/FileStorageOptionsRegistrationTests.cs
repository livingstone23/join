using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using JOIN.Infrastructure;
using JOIN.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Verifies how <c>AddInfrastructureServices</c> resolves
/// <see cref="LocalStorageOptions.RootPath"/>: relative paths are anchored to the
/// host content root, absolute paths are left untouched. SPEC 49-post: the
/// shape of <see cref="FileStorageOptions"/> changed — root path now lives
/// under <c>FileStorage:Local:RootPath</c> instead of <c>FileStorage:RootPath</c>.
/// </summary>
public sealed class FileStorageOptionsRegistrationTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "join-content-root");

    [Fact]
    public void RootPath_WhenRelative_ShouldBeAnchoredToContentRoot()
    {
        var options = ResolveOptions("App_Data/ticket-attachments");

        options.Local.RootPath.Should().Be(Path.Combine(ContentRoot, "App_Data/ticket-attachments"));
    }

    [Fact]
    public void RootPath_WhenAbsolute_ShouldBeKeptAsConfigured()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "shared-volume", "attachments");

        var options = ResolveOptions(absolute);

        options.Local.RootPath.Should().Be(absolute);
    }

    [Fact]
    public void Registration_WithOnlyLocal_ShouldResolveLocalAndLeaveCloudProvidersUnregistered()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileStorageResolver>();

        resolver.Current.Kind.Should().Be(StorageProviderKind.Local);
        resolver.Resolve(StorageProviderKind.Local).Should().NotBeNull();
        resolver.Resolve(StorageProviderKind.AzureBlob).Should().BeNull();
        resolver.Resolve(StorageProviderKind.S3).Should().BeNull();
        resolver.Resolve(StorageProviderKind.CloudflareR2).Should().BeNull();
    }

    [Fact]
    public void Registration_WithCompleteCloudSections_ShouldResolveEachAdapterWithItsKind()
    {
        // Las secciones completas se registran aunque el activo sea otro:
        // así los adjuntos viejos de cada proveedor siguen descargándose.
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FileStorage:Provider"] = "CloudflareR2",
            ["FileStorage:AzureBlob:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=eQ==;EndpointSuffix=core.windows.net",
            ["FileStorage:AzureBlob:ContainerName"] = "ticket-attachments",
            ["FileStorage:S3:BucketName"] = "join-s3",
            ["FileStorage:S3:Region"] = "us-east-1",
            ["FileStorage:S3:AccessKeyId"] = "ak",
            ["FileStorage:S3:SecretAccessKey"] = "sk",
            ["FileStorage:CloudflareR2:AccountId"] = "acc",
            ["FileStorage:CloudflareR2:BucketName"] = "join-r2",
            ["FileStorage:CloudflareR2:AccessKeyId"] = "ak",
            ["FileStorage:CloudflareR2:SecretAccessKey"] = "sk"
        });
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileStorageResolver>();

        resolver.Current.Kind.Should().Be(StorageProviderKind.CloudflareR2);
        resolver.Resolve(StorageProviderKind.Local)!.Kind.Should().Be(StorageProviderKind.Local);
        resolver.Resolve(StorageProviderKind.AzureBlob)!.Kind.Should().Be(StorageProviderKind.AzureBlob);
        resolver.Resolve(StorageProviderKind.S3)!.Kind.Should().Be(StorageProviderKind.S3);
        resolver.Resolve(StorageProviderKind.CloudflareR2)!.Kind.Should().Be(StorageProviderKind.CloudflareR2);
    }

    [Fact]
    public void Registration_WithIncompleteCloudSection_ShouldNotRegisterThatAdapter()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FileStorage:S3:BucketName"] = "join-s3",
            ["FileStorage:S3:Region"] = "us-east-1"
        });
        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IFileStorageResolver>();

        resolver.Resolve(StorageProviderKind.S3).Should().BeNull();
    }

    [Fact]
    public void Startup_WhenProviderPointsToIncompleteSection_ShouldFailNamingTheMissingKey()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["FileStorage:Provider"] = "AzureBlob",
            ["FileStorage:AzureBlob:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=eQ==;EndpointSuffix=core.windows.net"
        });

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*FileStorage:AzureBlob:ContainerName*");
    }

    private static FileStorageOptions ResolveOptions(string rootPath)
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["FileStorage:Local:RootPath"] = rootPath });
        return provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddInfrastructureServices(configuration);

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "JOIN.Tests";
        public string ContentRootPath { get; set; } = ContentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
