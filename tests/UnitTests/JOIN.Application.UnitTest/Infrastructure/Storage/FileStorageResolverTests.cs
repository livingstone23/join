using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.Infrastructure.Storage;

/// <summary>
/// Unit tests for <see cref="FileStorageResolver"/>. El resolver
/// se registra con DI keyed en producción; estos tests cubren
/// la lógica de mapeo <see cref="StorageProviderKind"/> →
/// <see cref="IFileStorageService"/> sin tocar DI real, llamando
/// directamente al resolver con un <see cref="IServiceProvider"/>
/// armado a mano.
/// </summary>
public sealed class FileStorageResolverTests
{
    [Fact]
    public void Current_WhenProviderIsLocal_ShouldReturnLocalAdapter()
    {
        var localAdapter = new FakeAdapter(StorageProviderKind.Local);
        var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.Local, localAdapter);
        }, StorageProviderKind.Local);

        var resolver = new FileStorageResolver(
            provider,
            Options.Create(new FileStorageOptions { Provider = StorageProviderKind.Local }),
            NullLogger<FileStorageResolver>.Instance);

        resolver.Current.Should().BeSameAs(localAdapter);
    }

    [Fact]
    public void Current_WhenCloudflareR2IsActive_ShouldReturnTheR2Adapter()
    {
        var r2Adapter = new FakeAdapter(StorageProviderKind.CloudflareR2);
        var s3Adapter = new FakeAdapter(StorageProviderKind.S3);
        var localAdapter = new FakeAdapter(StorageProviderKind.Local);

        var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.Local, localAdapter);
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.S3, s3Adapter);
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.CloudflareR2, r2Adapter);
        }, StorageProviderKind.CloudflareR2);

        var resolver = new FileStorageResolver(
            provider,
            Options.Create(new FileStorageOptions { Provider = StorageProviderKind.CloudflareR2 }),
            NullLogger<FileStorageResolver>.Instance);

        resolver.Current.Should().BeSameAs(r2Adapter);
    }

    [Fact]
    public void Resolve_Local_ShouldAlwaysBeAvailable()
    {
        // Local está registrado siempre (es el de pruebas y el
        // fallback para filas históricas). Ningún otro provider
        // se registra — Local resuelve.
        var localAdapter = new FakeAdapter(StorageProviderKind.Local);

        var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.Local, localAdapter);
        }, StorageProviderKind.Local);

        var resolver = new FileStorageResolver(
            provider,
            Options.Create(new FileStorageOptions { Provider = StorageProviderKind.Local }),
            NullLogger<FileStorageResolver>.Instance);

        resolver.Resolve(StorageProviderKind.Local).Should().BeSameAs(localAdapter);
    }

    [Fact]
    public void Resolve_WhenProviderSectionIsMissing_ShouldReturnNull()
    {
        // Solo Local está registrado: una fila con StorageProvider=S3
        // devuelve null. El handler traduce eso a 503
        // STORAGE_PROVIDER_NOT_CONFIGURED.
        var localAdapter = new FakeAdapter(StorageProviderKind.Local);

        var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IFileStorageService>(StorageProviderKind.Local, localAdapter);
        }, StorageProviderKind.Local);

        var resolver = new FileStorageResolver(
            provider,
            Options.Create(new FileStorageOptions { Provider = StorageProviderKind.Local }),
            NullLogger<FileStorageResolver>.Instance);

        resolver.Resolve(StorageProviderKind.AzureBlob).Should().BeNull();
        resolver.Resolve(StorageProviderKind.S3).Should().BeNull();
        resolver.Resolve(StorageProviderKind.CloudflareR2).Should().BeNull();
    }

    [Fact]
    public void Current_WhenProviderIsNotRegistered_ShouldThrow()
    {
        // Si el Provider activo no se registró (configuración rota),
        // Current lanza para que la API no arranque en un estado
        // inválido.
        var provider = BuildProvider(_ => { }, StorageProviderKind.AzureBlob);

        var resolver = new FileStorageResolver(
            provider,
            Options.Create(new FileStorageOptions { Provider = StorageProviderKind.AzureBlob }),
            NullLogger<FileStorageResolver>.Instance);

        var act = () => _ = resolver.Current;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'AzureBlob' is not configured*");
    }

    private static IServiceProvider BuildProvider(Action<IServiceCollection> configure, StorageProviderKind active)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Stub mínimo de <see cref="IFileStorageService"/> para
    /// estos tests. El resolver solo necesita el <c>Kind</c> y
    /// la identidad; los métodos no se invocan porque los tests
    /// del resolver no ejercen la lógica de storage.
    /// </summary>
    private sealed class FakeAdapter : IFileStorageService
    {
        public FakeAdapter(StorageProviderKind kind) { Kind = kind; }
        public StorageProviderKind Kind { get; }
        public Task<FileStorageSaveResult> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken)
            => Task.FromResult(new FileStorageSaveResult(storageKey, 0));
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);
        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
