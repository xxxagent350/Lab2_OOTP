using System.Globalization;
using System.Text;

namespace Lab2.Engine;

/// <summary>
/// Узел синтаксического дерева регулярного выражения.
/// Дерево строит собственный парсер (см. <see cref="RegexParser"/>),
/// а по дереву компилируется NFA (см. NfaCompiler).
/// </summary>
public abstract class RegexNode
{
    /// <summary>Добавить описание узла (и всех потомков) в указанный StringBuilder.</summary>
    public abstract void Describe(StringBuilder sb, int indent);

    /// <summary>Текстовое дерево разбора — удобно показывать в интерфейсе.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        Describe(sb, 0);
        return sb.ToString();
    }

    protected static void Line(StringBuilder sb, int indent, string text)
        => sb.Append(' ', indent * 2).AppendLine(text);

    /// <summary>Показать символ в человекочитаемом виде (управляющие символы — escape-последовательностями).</summary>
    public static string DisplayChar(char c) => c switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        '\f' => "\\f",
        '\v' => "\\v",
        '\b' => "\\b",
        '\\' => "\\\\",
        _ when char.IsControl(c) => $"\\u{(int)c:X4}",
        _ => c.ToString()
    };
}

/// <summary>Пустая ветвь: например, вторая половина выражения <c>a|</c>.</summary>
public sealed class EmptyNode : RegexNode
{
    public override void Describe(StringBuilder sb, int indent)
        => Line(sb, indent, "Пусто (пустая ветвь)");
}

/// <summary>Обычный символ.</summary>
public sealed class LiteralNode : RegexNode
{
    public char Value { get; }

    public LiteralNode(char value) => Value = value;

    public override void Describe(StringBuilder sb, int indent)
        => Line(sb, indent, $"Символ '{DisplayChar(Value)}'");
}

/// <summary>Точка — любой символ, кроме перевода строки.</summary>
public sealed class AnyCharNode : RegexNode
{
    public override void Describe(StringBuilder sb, int indent)
        => Line(sb, indent, "Любой символ, кроме \\n");
}

/// <summary>Класс символов <c>[...]</c>, в том числе отрицательный <c>[^...]</c>.</summary>
public sealed class CharClassNode : RegexNode
{
    public bool Negated { get; }
    public IReadOnlyList<CharClassAtom> Atoms { get; }

    public CharClassNode(bool negated, IReadOnlyList<CharClassAtom> atoms)
    {
        Negated = negated;
        Atoms = atoms;
    }

    public bool Matches(char c, bool ignoreCase)
    {
        bool any = false;
        foreach (var atom in Atoms)
        {
            if (atom.Matches(c, ignoreCase))
            {
                any = true;
                break;
            }
        }

        return Negated ? !any : any;
    }

    public override void Describe(StringBuilder sb, int indent)
    {
        var body = string.Concat(Atoms.Select(a => a.Describe()));
        Line(sb, indent, $"Класс [{(Negated ? "^" : string.Empty)}{body}]");
    }
}

/// <summary>Один элемент класса символов: диапазон, \d, \w, \s, \p{...} и т. п.</summary>
public abstract class CharClassAtom
{
    public abstract bool Matches(char c, bool ignoreCase);
    public abstract string Describe();
}

/// <summary>Диапазон символов или одиночный символ.</summary>
public sealed class CharRangeAtom : CharClassAtom
{
    public char From { get; }
    public char To { get; }

    public CharRangeAtom(char from, char to)
    {
        From = from;
        To = to;
    }

    public override bool Matches(char c, bool ignoreCase)
    {
        if (InRange(c, From, To))
        {
            return true;
        }

        if (!ignoreCase)
        {
            return false;
        }

        char lower = char.ToLowerInvariant(c);
        char upper = char.ToUpperInvariant(c);
        return InRange(lower, From, To) || InRange(upper, From, To);
    }

    private static bool InRange(char c, char from, char to) => c >= from && c <= to;

    public override string Describe()
        => From == To
            ? RegexNode.DisplayChar(From)
            : $"{RegexNode.DisplayChar(From)}-{RegexNode.DisplayChar(To)}";
}

public enum CharCategoryKind
{
    Digit,
    Word,
    Space
}

/// <summary>\d, \D, \w, \W, \s, \S (в том числе внутри класса).</summary>
public sealed class CategoryAtom : CharClassAtom
{
    public CharCategoryKind Kind { get; }
    public bool Negated { get; }

    public CategoryAtom(CharCategoryKind kind, bool negated)
    {
        Kind = kind;
        Negated = negated;
    }

    public override bool Matches(char c, bool ignoreCase)
    {
        bool matches = Kind switch
        {
            CharCategoryKind.Digit => char.IsDigit(c),
            CharCategoryKind.Word => char.IsLetterOrDigit(c) || c == '_',
            CharCategoryKind.Space => char.IsWhiteSpace(c),
            _ => false
        };

        return Negated ? !matches : matches;
    }

    public override string Describe()
    {
        char letter = Kind switch
        {
            CharCategoryKind.Digit => 'd',
            CharCategoryKind.Word => 'w',
            CharCategoryKind.Space => 's',
            _ => '?'
        };

        return "\\" + (Negated ? char.ToUpperInvariant(letter) : letter);
    }
}

/// <summary>\p{L}, \p{Lu}, \P{Nd} и другие категории Unicode.</summary>
public sealed class UnicodeCategoryAtom : CharClassAtom
{
    public string Group { get; }
    public bool Negated { get; }

    public UnicodeCategoryAtom(string group, bool negated)
    {
        Group = group;
        Negated = negated;
    }

    public override bool Matches(char c, bool ignoreCase)
    {
        var category = char.GetUnicodeCategory(c);
        bool matches = Group switch
        {
            "L" => category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter,
            "Lu" => category == UnicodeCategory.UppercaseLetter,
            "Ll" => category == UnicodeCategory.LowercaseLetter,
            "Lt" => category == UnicodeCategory.TitlecaseLetter,
            "Lm" => category == UnicodeCategory.ModifierLetter,
            "Lo" => category == UnicodeCategory.OtherLetter,

            "M" => category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark,
            "Mn" => category == UnicodeCategory.NonSpacingMark,
            "Mc" => category == UnicodeCategory.SpacingCombiningMark,
            "Me" => category == UnicodeCategory.EnclosingMark,

            "N" => category is UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.OtherNumber,
            "Nd" => category == UnicodeCategory.DecimalDigitNumber,
            "Nl" => category == UnicodeCategory.LetterNumber,
            "No" => category == UnicodeCategory.OtherNumber,

            "P" => category is UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation,
            "Pc" => category == UnicodeCategory.ConnectorPunctuation,
            "Pd" => category == UnicodeCategory.DashPunctuation,
            "Ps" => category == UnicodeCategory.OpenPunctuation,
            "Pe" => category == UnicodeCategory.ClosePunctuation,
            "Pi" => category == UnicodeCategory.InitialQuotePunctuation,
            "Pf" => category == UnicodeCategory.FinalQuotePunctuation,
            "Po" => category == UnicodeCategory.OtherPunctuation,

            "S" => category is UnicodeCategory.MathSymbol
                or UnicodeCategory.CurrencySymbol
                or UnicodeCategory.ModifierSymbol
                or UnicodeCategory.OtherSymbol,
            "Sm" => category == UnicodeCategory.MathSymbol,
            "Sc" => category == UnicodeCategory.CurrencySymbol,
            "Sk" => category == UnicodeCategory.ModifierSymbol,
            "So" => category == UnicodeCategory.OtherSymbol,

            "Z" => category is UnicodeCategory.SpaceSeparator
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator,
            "Zs" => category == UnicodeCategory.SpaceSeparator,
            "Zl" => category == UnicodeCategory.LineSeparator,
            "Zp" => category == UnicodeCategory.ParagraphSeparator,

            "C" => category is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned,
            "Cc" => category == UnicodeCategory.Control,
            "Cf" => category == UnicodeCategory.Format,
            "Cs" => category == UnicodeCategory.Surrogate,
            "Co" => category == UnicodeCategory.PrivateUse,
            "Cn" => category == UnicodeCategory.OtherNotAssigned,

            _ => false
        };

        return Negated ? !matches : matches;
    }

    public override string Describe() => $"\\{(Negated ? "P" : "p")}{{{Group}}}";
}

/// <summary>Последовательность узлов.</summary>
public sealed class ConcatNode : RegexNode
{
    public IReadOnlyList<RegexNode> Children { get; }

    public ConcatNode(IReadOnlyList<RegexNode> children) => Children = children;

    public override void Describe(StringBuilder sb, int indent)
    {
        Line(sb, indent, "Конкатенация");
        foreach (var child in Children)
        {
            child.Describe(sb, indent + 1);
        }
    }
}

/// <summary>Альтернатива <c>a|b|c</c>.</summary>
public sealed class AlternationNode : RegexNode
{
    public IReadOnlyList<RegexNode> Branches { get; }

    public AlternationNode(IReadOnlyList<RegexNode> branches) => Branches = branches;

    public override void Describe(StringBuilder sb, int indent)
    {
        Line(sb, indent, "Альтернатива (|)");
        foreach (var branch in Branches)
        {
            branch.Describe(sb, indent + 1);
        }
    }
}

/// <summary>Квантификатор: <c>*</c>, <c>+</c>, <c>?</c>, <c>{n}</c>, <c>{n,}</c>, <c>{n,m}</c>.</summary>
public sealed class RepeatNode : RegexNode
{
    public RegexNode Child { get; }
    public int Min { get; }
    public int Max { get; }
    public bool Lazy { get; }

    public RepeatNode(RegexNode child, int min, int max, bool lazy)
    {
        Child = child;
        Min = min;
        Max = max;
        Lazy = lazy;
    }

    public override void Describe(StringBuilder sb, int indent)
    {
        string max = Max == int.MaxValue ? "∞" : Max.ToString();
        string lazy = Lazy ? ", ленивый" : ", жадный";
        Line(sb, indent, $"Повтор {Min}..{max}{lazy}");
        Child.Describe(sb, indent + 1);
    }
}

/// <summary>Группа: обычная, незахватывающая или именованная.</summary>
public sealed class GroupNode : RegexNode
{
    public RegexNode Child { get; }
    public string? Name { get; }
    public bool Capturing { get; }

    public GroupNode(RegexNode child, string? name, bool capturing)
    {
        Child = child;
        Name = name;
        Capturing = capturing;
    }

    public override void Describe(StringBuilder sb, int indent)
    {
        string title = Name is not null
            ? $"Группа (?<{Name}>...)"
            : Capturing
                ? "Группа (...)"
                : "Группа (?:...)";
        Line(sb, indent, title);
        Child.Describe(sb, indent + 1);
    }
}

public enum AnchorKind
{
    StartText,
    EndText,
    EndTextBeforeFinalNewline,
    WordBoundary,
    NonWordBoundary
}

/// <summary>Якоря ^, $, \A, \z, \Z и границы слова \b, \B.</summary>
public sealed class AnchorNode : RegexNode
{
    public AnchorKind Kind { get; }

    public AnchorNode(AnchorKind kind) => Kind = kind;

    public bool Matches(string input, int position) => Matches(Kind, input, position);

    public static bool Matches(AnchorKind kind, string input, int position) => kind switch
    {
        AnchorKind.StartText => position == 0,
        AnchorKind.EndText => position == input.Length,
        AnchorKind.EndTextBeforeFinalNewline =>
            position == input.Length ||
            (position == input.Length - 1 && input[position] == '\n') ||
            (position == input.Length - 2 && input[position] == '\r' && input[position + 1] == '\n'),
        AnchorKind.WordBoundary =>
            IsWordCharAt(input, position - 1) != IsWordCharAt(input, position),
        AnchorKind.NonWordBoundary =>
            IsWordCharAt(input, position - 1) == IsWordCharAt(input, position),
        _ => false
    };

    private static bool IsWordCharAt(string input, int index)
        => index >= 0 && index < input.Length && (char.IsLetterOrDigit(input[index]) || input[index] == '_');

    public override void Describe(StringBuilder sb, int indent)
    {
        string title = Kind switch
        {
            AnchorKind.StartText => "Якорь ^ (начало текста)",
            AnchorKind.EndText => "Якорь $ (конец текста)",
            AnchorKind.EndTextBeforeFinalNewline => "Якорь \\Z (конец текста или перед завершающим \\n)",
            AnchorKind.WordBoundary => "Граница слова \\b",
            AnchorKind.NonWordBoundary => "Не граница слова \\B",
            _ => "Якорь"
        };

        Line(sb, indent, title);
    }
}
