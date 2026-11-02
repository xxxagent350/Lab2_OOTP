namespace Lab2.Engine;

/// <summary>Категория ошибки, которую нашёл парсер.</summary>
public enum RegexErrorKind
{
    /// <summary>Выражение синтаксически некорректно.</summary>
    Syntax,

    /// <summary>Выражение корректно для многих regex-движков, но не является регулярным.</summary>
    NotRegular,

    /// <summary>Конструкция регулярна, но наш учебный движок её не поддерживает.</summary>
    Unsupported
}

/// <summary>Ошибка разбора выражения с позицией в исходной строке.</summary>
public sealed class RegexParseException : Exception
{
    /// <summary>Позиция в исходном выражении (0-based).</summary>
    public int Position { get; }

    public RegexErrorKind Kind { get; }

    /// <summary>Короткое имя конструкции для пояснения (backreference, lookahead, ...).</summary>
    public string? Construct { get; }

    public RegexParseException(RegexErrorKind kind, string message, int position, string? construct = null)
        : base(message)
    {
        Kind = kind;
        Position = position;
        Construct = construct;
    }

    public string PositionText => Position < 0 ? "позиция не определена" : $"позиция {Position + 1}";
}

/// <summary>Сработал защитный лимит движка (сложность выражения, длина строки, число шагов).</summary>
public sealed class RegexLimitException : Exception
{
    public RegexLimitException(string message) : base(message)
    {
    }
}
