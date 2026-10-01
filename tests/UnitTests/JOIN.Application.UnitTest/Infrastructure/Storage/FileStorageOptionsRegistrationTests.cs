using FluentAssertions;
using JOIN.Infrastructure;
using JOIN.Infrastructure.Storage.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Verifies how <c>AddInfrastructureServices</c> resolves <see cref="FileStorageOptions.RootPath"/>:
/// relative paths are anchored to the host content root, absolute paths are left untouched.
/// </summary>
public sealed class FileStorageOptionsRegistrationTests
{
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "join-content-root");

    [Fact]
    public void RootPath_WhenRelative_ShouldBeAnchoredToContentRoot()
    {
        var options = ResolveOptions("App_Data/ticket-attachments");

        options.RootPath.Should().Be(Path.Combine(ContentRoot, "App_Data/ticket-attachments"));
    }

    [Fact]
    public void RootPath_WhenAbsolute_ShouldBeKeptAsConfigured()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "shared-volume", "attachments");

        var options = ResolveOptions(absolute);

        options.RootPath.Should().Be(absolute);
    }

    private static FileStorageOptions ResolveOptions(string rootPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:Local:RootPath"] = rootPath })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "JOIN.Tests";
        public string ContentRootPath { get; set; } = ContentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
