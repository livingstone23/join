using System.Reflection;



namespace JOIN.Domain.Enums;



/// <summary>
/// Base class for Smart Enums — a type-safe, object-oriented alternative to C# enums.
/// Subclasses declare their values as public static readonly fields.
/// </summary>
public abstract class Enumeration<T> where T : Enumeration<T>
{
    public int Value { get; }
    public string Name { get; }

    protected Enumeration(int value, string name)
    {
        Value = value;
        Name = name;
    }

    /// <summary>
    /// Returns all declared values of the enumeration.
    /// </summary>
    public static IEnumerable<T> GetAll()
    {
        return typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(T))
            .Select(f => (T)f.GetValue(null)!);
    }

    /// <summary>
    /// Returns the enumeration instance whose <see cref="Value"/> matches <paramref name="value"/>.
    /// Throws <see cref="InvalidOperationException"/> if no match is found.
    /// </summary>
    public static T FromValue(int value)
    {
        return GetAll().FirstOrDefault(e => e.Value == value)
            ?? throw new InvalidOperationException($"No se encontró {typeof(T).Name} con valor {value}.");
    }

    public override string ToString() => Name;
    
}
