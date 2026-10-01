namespace JOIN.Domain.Enums;

/// <summary>
/// Tipos de documentos que se pueden adjuntar a un ticket. Sirve dos roles:
/// valor único en <c>TicketDocument.DocumentType</c> (qué es este archivo) y
/// bitmask en <c>TicketAttachmentSettings.AllowedDocumentTypes</c> (qué tipos
/// acepta la empresa) — mismo patrón que <see cref="Security.PermissionFlags"/>.
/// </summary>
[Flags]
public enum DocumentType
{
    /// <summary>
    /// Sin tipo seleccionado — una empresa con este bitmask deshabilita
    /// toda subida por construcción (todo HasFlag falla).
    /// </summary>
    None = 0,

    /// <summary>
    /// Documentos PDF (.pdf).
    /// </summary>
    Pdf = 1 << 0,

    /// <summary>
    /// Documentos Word (.doc, .docx).
    /// </summary>
    Word = 1 << 1,

    /// <summary>
    /// Hojas de cálculo Excel (.xls, .xlsx).
    /// </summary>
    Excel = 1 << 2,

    /// <summary>
    /// Archivos de texto plano (.txt, .csv).
    /// </summary>
    Text = 1 << 3,

    /// <summary>
    /// Imágenes (.png, .jpg, .jpeg, .gif).
    /// </summary>
    Image = 1 << 4,

    /// <summary>
    /// Otros tipos reconocidos (ej. .zip) — no incluye extensiones
    /// desconocidas, que son rechazadas con <c>UNSUPPORTED_DOCUMENT_TYPE</c>.
    /// </summary>
    Other = 1 << 5
}