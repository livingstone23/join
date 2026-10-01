namespace JOIN.Infrastructure.Storage.Local;

/// <summary>
/// Opciones de binding para <see cref="LocalDiskFileStorageAdapter"/>.
/// Se bindea desde la sección <c>FileStorage:Local</c> de
/// <c>appsettings.json</c>. El <c>RootPath</c> se interpreta como raíz del
/// filesystem; todas las claves de almacenamiento se resuelven relativas
/// a este path con guard de path traversal (<c>..</c>).
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>
    /// Carpeta raíz del filesystem donde se persisten los adjuntos.
    /// Default <c>App_Data/ticket-attachments</c> (relativa al working
    /// directory del proceso). En entornos con múltiples réplicas de la
    /// API, este path debe apuntar a un volumen compartido (NFS, Azure
    /// Files, etc.) — ver "Identified risks" en la spec.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/ticket-attachments";
}