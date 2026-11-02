namespace Lab2.Engine;

/// <summary>Одно найденное совпадение: позиция, длина и сам фрагмент.</summary>
public readonly record struct RegexMatch(int Index, int Length, string Value)
{
    public int End => Index + Length;
}

/// <summary>
/// Скомпилированное регулярное выражение: дерево разбора + NFA.
///
/// ВАЖНО: здесь сознательно не используется встроенный regex-движок .NET.
/// Парсер, построение автомата и поиск реализованы в этом проекте.
/// </summary>
public sealed class RegexPattern
{
    public const int MaxInputLength = 20_000;
    public const long DefaultMaxSteps = 5_000_000;
    public const int DefaultMaxMatches = 2_000;

    private readonly NfaProgram _program;

    public string Source { get; }
    public bool IgnoreCase { get; }
    public RegexNode Root { get; }
    public int StateCount => _program.StateCount;

    private RegexPattern(string source, bool ignoreCase, RegexNode root, NfaProgram program)
    {
        Source = source;
        IgnoreCase = ignoreCase;
        Root = root;
        _program = program;
    }

    /// <summary>
    /// Разобрать выражение и построить автомат.
    /// </summary>
    /// <exception cref="RegexParseException">Выражение некорректно или нерегулярно.</exception>
    /// <exception cref="RegexLimitException">Выражение слишком сложное.</exception>
    public static RegexPattern Parse(string source, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        var root = RegexParser.Parse(source);
        var compiler = new NfaCompiler(ignoreCase);
        var program = compiler.Compile(root);
        return new RegexPattern(source, ignoreCase, root, program);
    }

    /// <summary>Найти первое (самое левое и самое длинное) совпадение.</summary>
    public RegexMatch? Find(string input, int startIndex = 0, long maxSteps = DefaultMaxSteps)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLength(input);

        long steps = 0;
        return _program.Find(input, startIndex, ref steps, maxSteps);
    }

    /// <summary>
    /// Найти все неперекрывающиеся совпадения. Пустые совпадения тоже
    /// возвращаются — интерфейс показывает их отдельно.
    /// </summary>
    public IReadOnlyList<RegexMatch> FindAll(
        string input,
        int maxMatches = DefaultMaxMatches,
        long maxSteps = DefaultMaxSteps)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureLength(input);

        var result = new List<RegexMatch>();
        if (maxMatches <= 0)
        {
            return result;
        }

        long steps = 0;
        int position = 0;

        while (position <= input.Length && result.Count < maxMatches)
        {
            var match = _program.Find(input, position, ref steps, maxSteps);
            if (match is null)
            {
                break;
            }

            result.Add(match.Value);

            // Пустое совпадение не сдвигает позицию — принудительно идём дальше,
            // иначе поиск зациклится.
            position = match.Value.Length > 0
                ? match.Value.Index + match.Value.Length
                : match.Value.Index + 1;
        }

        return result;
    }

    private static void EnsureLength(string input)
    {
        if (input.Length > MaxInputLength)
        {
            throw new RegexLimitException(
                $"Строка слишком длинная: {input.Length} символов, максимум {MaxInputLength}.");
        }
    }
}
