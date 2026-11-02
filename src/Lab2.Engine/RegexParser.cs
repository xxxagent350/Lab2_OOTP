using System.Text;

namespace Lab2.Engine;

/// <summary>
/// Рекурсивный парсер регулярных выражений.
///
/// Грамматика:
///   alternation := concat ('|' concat)*
///   concat      := (atom quantifier?)*
///   atom        := literal | '.' | '^' | '$' | class | group | escape
///   quantifier  := '*' | '+' | '?' | '{n}' | '{n,}' | '{n,m}'  (плюс '?' для ленивости)
///
/// Парсер не использует встроенный regex-движок .NET и сам определяет,
/// является ли введённое выражение регулярным. Конструкции, выходящие за
/// пределы регулярных языков (обратные ссылки, lookaround, условия, рекурсия),
/// отвергаются с <see cref="RegexErrorKind.NotRegular"/>.
/// </summary>
public static class RegexParser
{
    public const int MaxPatternLength = 100_000;

    public static RegexNode Parse(string source)
    {
        source ??= string.Empty;
        if (source.Length > MaxPatternLength)
        {
            throw new RegexLimitException(
                $"Слишком длинное выражение: {source.Length} символов, максимум {MaxPatternLength}.");
        }

        var parser = new Parser(source);
        return parser.Parse();
    }

    private sealed class Parser
    {
        private const int MaxRepeat = 10_000;
        private const int MaxDepth = 500;

        private static readonly Dictionary<string, string> UnicodeCategories =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["L"] = "L", ["Letter"] = "L",
                ["Lu"] = "Lu", ["UppercaseLetter"] = "Lu",
                ["Ll"] = "Ll", ["LowercaseLetter"] = "Ll",
                ["Lt"] = "Lt", ["TitlecaseLetter"] = "Lt",
                ["Lm"] = "Lm", ["ModifierLetter"] = "Lm",
                ["Lo"] = "Lo", ["OtherLetter"] = "Lo",

                ["M"] = "M", ["Mark"] = "M",
                ["Mn"] = "Mn", ["NonSpacingMark"] = "Mn",
                ["Mc"] = "Mc", ["SpacingCombiningMark"] = "Mc",
                ["Me"] = "Me", ["EnclosingMark"] = "Me",

                ["N"] = "N", ["Number"] = "N",
                ["Nd"] = "Nd", ["DecimalDigitNumber"] = "Nd",
                ["Nl"] = "Nl", ["LetterNumber"] = "Nl",
                ["No"] = "No", ["OtherNumber"] = "No",

                ["P"] = "P", ["Punctuation"] = "P",
                ["Pc"] = "Pc", ["ConnectorPunctuation"] = "Pc",
                ["Pd"] = "Pd", ["DashPunctuation"] = "Pd",
                ["Ps"] = "Ps", ["OpenPunctuation"] = "Ps",
                ["Pe"] = "Pe", ["ClosePunctuation"] = "Pe",
                ["Pi"] = "Pi", ["InitialQuotePunctuation"] = "Pi",
                ["Pf"] = "Pf", ["FinalQuotePunctuation"] = "Pf",
                ["Po"] = "Po", ["OtherPunctuation"] = "Po",

                ["S"] = "S", ["Symbol"] = "S",
                ["Sm"] = "Sm", ["MathSymbol"] = "Sm",
                ["Sc"] = "Sc", ["CurrencySymbol"] = "Sc",
                ["Sk"] = "Sk", ["ModifierSymbol"] = "Sk",
                ["So"] = "So", ["OtherSymbol"] = "So",

                ["Z"] = "Z", ["Separator"] = "Z",
                ["Zs"] = "Zs", ["SpaceSeparator"] = "Zs",
                ["Zl"] = "Zl", ["LineSeparator"] = "Zl",
                ["Zp"] = "Zp", ["ParagraphSeparator"] = "Zp",

                ["C"] = "C", ["Other"] = "C",
                ["Cc"] = "Cc", ["Control"] = "Cc",
                ["Cf"] = "Cf", ["Format"] = "Cf",
                ["Cs"] = "Cs", ["Surrogate"] = "Cs",
                ["Co"] = "Co", ["PrivateUse"] = "Co",
                ["Cn"] = "Cn", ["OtherNotAssigned"] = "Cn"
            };

        private readonly string _source;
        private int _index;
        private int _depth;

        public Parser(string source) => _source = source;

        private bool AtEnd => _index >= _source.Length;
        private char Current => _source[_index];

        public RegexNode Parse()
        {
            var node = ParseAlternation();
            if (!AtEnd)
            {
                if (Current == ')')
                {
                    throw Error(RegexErrorKind.Syntax, "Лишняя закрывающая скобка ')'", _index);
                }

                throw Error(RegexErrorKind.Syntax, $"Неожиданный символ '{Current}'", _index);
            }

            return node;
        }

        private RegexNode ParseAlternation()
        {
            var branches = new List<RegexNode> { ParseConcat() };

            while (!AtEnd && Current == '|')
            {
                _index++;
                branches.Add(ParseConcat());
            }

            return branches.Count == 1 ? branches[0] : new AlternationNode(branches);
        }

        private RegexNode ParseConcat()
        {
            var items = new List<RegexNode>();

            while (!AtEnd && Current != '|' && Current != ')')
            {
                char c = Current;

                if (c is '*' or '+' or '?')
                {
                    throw Error(RegexErrorKind.Syntax, $"Квантификатор '{c}' без предшествующего элемента", _index);
                }

                if (c == '{' && IsBraceQuantifierStart(_index))
                {
                    throw Error(RegexErrorKind.Syntax, "Квантификатор '{...}' без предшествующего элемента", _index);
                }

                var atom = ParseAtom();
                items.Add(ParseQuantifier(atom));
            }

            if (items.Count == 0)
            {
                return new EmptyNode();
            }

            return items.Count == 1 ? items[0] : new ConcatNode(items);
        }

        private RegexNode ParseAtom()
        {
            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax, "Неожиданный конец выражения", _index);
            }

            char c = Current;
            switch (c)
            {
                case '(':
                    return ParseGroup();
                case '[':
                    return ParseCharClass();
                case '.':
                    _index++;
                    return new AnyCharNode();
                case '^':
                    _index++;
                    return new AnchorNode(AnchorKind.StartText);
                case '$':
                    _index++;
                    return new AnchorNode(AnchorKind.EndText);
                case '\\':
                    return ParseEscape();
                case ')':
                    throw Error(RegexErrorKind.Syntax, "Лишняя закрывающая скобка ')'", _index);
                case '*':
                case '+':
                case '?':
                    throw Error(RegexErrorKind.Syntax, $"Квантификатор '{c}' без предшествующего элемента", _index);
                case '{':
                    if (IsBraceQuantifierStart(_index))
                    {
                        throw Error(RegexErrorKind.Syntax, "Квантификатор '{...}' без предшествующего элемента", _index);
                    }

                    _index++;
                    return new LiteralNode('{');
                default:
                    _index++;
                    return new LiteralNode(c);
            }
        }

        private RegexNode ParseQuantifier(RegexNode atom)
        {
            if (AtEnd)
            {
                return atom;
            }

            int start = _index;
            int min;
            int max;

            switch (Current)
            {
                case '*':
                    min = 0;
                    max = int.MaxValue;
                    _index++;
                    break;
                case '+':
                    min = 1;
                    max = int.MaxValue;
                    _index++;
                    break;
                case '?':
                    min = 0;
                    max = 1;
                    _index++;
                    break;
                case '{' when IsBraceQuantifierStart(_index):
                    (min, max) = ParseBraceQuantifier();
                    break;
                default:
                    return atom;
            }

            if (atom is AnchorNode)
            {
                throw Error(RegexErrorKind.Syntax,
                    "Квантификатор нельзя применять к якорю или границе слова", start);
            }

            bool lazy = false;
            if (!AtEnd && Current == '?')
            {
                lazy = true;
                _index++;
            }

            if (!AtEnd)
            {
                if (Current == '+' && !lazy)
                {
                    throw Error(RegexErrorKind.Unsupported,
                        "Possessive-квантификаторы (например, '++') не поддерживаются", _index,
                        "possessive quantifier");
                }

                if (Current is '*' or '+' or '?')
                {
                    throw Error(RegexErrorKind.Syntax, "Два квантификатора подряд", _index);
                }

                if (Current == '{' && IsBraceQuantifierStart(_index))
                {
                    throw Error(RegexErrorKind.Syntax, "Два квантификатора подряд", _index);
                }
            }

            return new RepeatNode(atom, min, max, lazy);
        }

        private (int Min, int Max) ParseBraceQuantifier()
        {
            int start = _index;
            _index++; // '{'

            int min = ParseInteger();

            int max;
            if (!AtEnd && Current == ',')
            {
                _index++;
                max = !AtEnd && Current == '}' ? int.MaxValue : ParseInteger();
            }
            else
            {
                max = min;
            }

            if (AtEnd || Current != '}')
            {
                throw Error(RegexErrorKind.Syntax,
                    "Не закрыт квантификатор '{...}': отсутствует '}'", start);
            }

            _index++;

            if (min > MaxRepeat || (max != int.MaxValue && max > MaxRepeat))
            {
                throw Error(RegexErrorKind.Unsupported,
                    $"Квантификатор слишком большой: максимум {MaxRepeat}", start,
                    "large quantifier");
            }

            if (min > max)
            {
                throw Error(RegexErrorKind.Syntax,
                    $"Некорректный квантификатор: минимум {min} больше максимума {max}", start);
            }

            return (min, max);
        }

        private int ParseInteger()
        {
            int start = _index;
            long value = 0;

            while (!AtEnd && char.IsDigit(Current))
            {
                value = value * 10 + (Current - '0');
                if (value > 1_000_000)
                {
                    throw Error(RegexErrorKind.Unsupported, "Слишком большое число в квантификаторе", start);
                }

                _index++;
            }

            if (_index == start)
            {
                throw Error(RegexErrorKind.Syntax, "Ожидалось число в квантификаторе", _index);
            }

            return (int)value;
        }

        private bool IsBraceQuantifierStart(int index)
        {
            if (index >= _source.Length || _source[index] != '{')
            {
                return false;
            }

            int next = index + 1;
            return next < _source.Length && char.IsDigit(_source[next]);
        }

        private RegexNode ParseGroup()
        {
            if (_depth >= MaxDepth)
            {
                throw Error(RegexErrorKind.Unsupported,
                    $"Слишком большая вложенность групп: максимум {MaxDepth}", _index,
                    "nesting depth");
            }

            _depth++;
            try
            {
                return ParseGroupCore();
            }
            finally
            {
                _depth--;
            }
        }

        private RegexNode ParseGroupCore()
        {
            int start = _index;
            _index++; // '('

            bool capturing = true;
            string? name = null;

            if (!AtEnd && Current == '?')
            {
                _index++;
                if (AtEnd)
                {
                    throw Error(RegexErrorKind.Syntax, "Незавершённая конструкция '(?'", start);
                }

                char c = Current;
                switch (c)
                {
                    case ':':
                        _index++;
                        capturing = false;
                        break;

                    case '=':
                    case '!':
                        throw Error(RegexErrorKind.NotRegular,
                            "Опережающая проверка (lookahead) не является регулярной конструкцией",
                            start, "lookahead");

                    case '<':
                        _index++;
                        if (!AtEnd && Current is '=' or '!')
                        {
                            throw Error(RegexErrorKind.NotRegular,
                                "Ретроспективная проверка (lookbehind) не является регулярной конструкцией",
                                start, "lookbehind");
                        }

                        name = ParseGroupName('>', start);
                        break;

                    case '\'':
                        _index++;
                        name = ParseGroupName('\'', start);
                        break;

                    case '>':
                        throw Error(RegexErrorKind.Unsupported,
                            "Атомарные группы '(?>...)' не поддерживаются", start, "atomic group");

                    case '(':
                        throw Error(RegexErrorKind.NotRegular,
                            "Условные конструкции '(?(...)...)' не являются регулярными",
                            start, "conditional");

                    case 'R':
                    case '&':
                        throw Error(RegexErrorKind.NotRegular,
                            "Рекурсивные конструкции и повторное использование групп не являются регулярными",
                            start, "recursion");

                    case 'P':
                        if (_index + 1 < _source.Length && _source[_index + 1] is '=' or '>')
                        {
                            throw Error(RegexErrorKind.NotRegular,
                                "Обратная ссылка в стиле Python '(?P=...)' не является регулярной",
                                start, "backreference");
                        }

                        throw Error(RegexErrorKind.Unsupported,
                            "Именованные группы в стиле Python '(?P<...>)' не поддерживаются",
                            start, "python named group");

                    case 'i':
                    case 'm':
                    case 's':
                    case 'n':
                    case 'x':
                    case '-':
                        throw Error(RegexErrorKind.Unsupported,
                            "Встроенные флаги '(?i...)' не поддерживаются; используйте флажок «Игнорировать регистр»",
                            start, "inline flags");

                    case '#':
                        // Комментарий (? # ... ) — не создаёт узла.
                        while (!AtEnd && Current != ')')
                        {
                            _index++;
                        }

                        if (AtEnd)
                        {
                            throw Error(RegexErrorKind.Syntax,
                                "Не закрыт комментарий '(?#...)' отсутствует ')'", start);
                        }

                        _index++;
                        return new EmptyNode();

                    default:
                        if (char.IsDigit(c))
                        {
                            throw Error(RegexErrorKind.NotRegular,
                                "Рекурсивные конструкции '(?1)' и условные переходы не являются регулярными",
                                start, "recursion");
                        }

                        throw Error(RegexErrorKind.Syntax,
                            $"Неизвестная конструкция '(?{c}...)'", start);
                }
            }

            var child = ParseAlternation();

            if (AtEnd || Current != ')')
            {
                throw Error(RegexErrorKind.Syntax, "Не закрыта группа: отсутствует ')'", start);
            }

            _index++;
            return new GroupNode(child, name, capturing);
        }

        private string ParseGroupName(char terminator, int groupStart)
        {
            int nameStart = _index;

            while (!AtEnd && Current != terminator)
            {
                char c = Current;
                if (c == '-')
                {
                    throw Error(RegexErrorKind.NotRegular,
                        "Балансирующие группы '(?<имя1-имя2>...)' не являются регулярными",
                        _index, "balancing group");
                }

                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    throw Error(RegexErrorKind.Syntax,
                        $"Недопустимый символ '{c}' в имени группы", _index);
                }

                _index++;
            }

            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax,
                    $"Не закрыто имя группы: отсутствует '{terminator}'", groupStart);
            }

            if (_index == nameStart)
            {
                throw Error(RegexErrorKind.Syntax, "Пустое имя группы", groupStart);
            }

            string name = _source.Substring(nameStart, _index - nameStart);
            _index++; // terminator
            return name;
        }

        private RegexNode ParseCharClass()
        {
            int start = _index;
            _index++; // '['

            bool negated = false;
            if (!AtEnd && Current == '^')
            {
                negated = true;
                _index++;
            }

            var atoms = new List<CharClassAtom>();
            bool first = true;

            while (true)
            {
                if (AtEnd)
                {
                    throw Error(RegexErrorKind.Syntax,
                        "Не закрыт класс символов: отсутствует ']'", start);
                }

                if (Current == ']' && !first)
                {
                    _index++;
                    break;
                }

                var (atom, literal, isLiteral) = ParseClassAtom();

                bool rangeStart = isLiteral &&
                                  !AtEnd &&
                                  Current == '-' &&
                                  _index + 1 < _source.Length &&
                                  _source[_index + 1] != ']';

                if (rangeStart)
                {
                    int dashPosition = _index;
                    _index++; // '-'

                    var (_, rangeEnd, endIsLiteral) = ParseClassAtom();
                    if (!endIsLiteral)
                    {
                        throw Error(RegexErrorKind.Syntax,
                            "Граница диапазона должна быть одним символом", dashPosition);
                    }

                    if (literal > rangeEnd)
                    {
                        throw Error(RegexErrorKind.Syntax,
                            $"Некорректный диапазон: '{RegexNode.DisplayChar(literal)}' > '{RegexNode.DisplayChar(rangeEnd)}'",
                            dashPosition);
                    }

                    atoms.Add(new CharRangeAtom(literal, rangeEnd));
                }
                else
                {
                    atoms.Add(atom);
                }

                first = false;
            }

            return new CharClassNode(negated, atoms);
        }

        private (CharClassAtom Atom, char Literal, bool IsLiteral) ParseClassAtom()
        {
            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax, "Неожиданный конец класса символов", _index);
            }

            if (Current == '\\')
            {
                return ParseClassEscape();
            }

            char c = Current;
            _index++;
            return (new CharRangeAtom(c, c), c, true);
        }

        private (CharClassAtom Atom, char Literal, bool IsLiteral) ParseClassEscape()
        {
            int start = _index;
            _index++; // '\'

            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax, "Незавершённая escape-последовательность", start);
            }

            char c = Current;
            _index++;

            switch (c)
            {
                case 'd':
                    return (new CategoryAtom(CharCategoryKind.Digit, false), default, false);
                case 'D':
                    return (new CategoryAtom(CharCategoryKind.Digit, true), default, false);
                case 'w':
                    return (new CategoryAtom(CharCategoryKind.Word, false), default, false);
                case 'W':
                    return (new CategoryAtom(CharCategoryKind.Word, true), default, false);
                case 's':
                    return (new CategoryAtom(CharCategoryKind.Space, false), default, false);
                case 'S':
                    return (new CategoryAtom(CharCategoryKind.Space, true), default, false);
                case 'p':
                    return (ParseUnicodeCategory(start, false), default, false);
                case 'P':
                    return (ParseUnicodeCategory(start, true), default, false);
                case 'b':
                    return (new CharRangeAtom('\b', '\b'), '\b', true);
                case 'n':
                    return Literal('\n');
                case 'r':
                    return Literal('\r');
                case 't':
                    return Literal('\t');
                case 'f':
                    return Literal('\f');
                case 'v':
                    return Literal('\v');
                case '0':
                    return Literal('\0');
                case 'a':
                    return Literal('\a');
                case 'e':
                    return Literal('\u001B');
                case 'x':
                    return Literal(ParseHex(2, start));
                case 'u':
                    return Literal(ParseHex(4, start));
                case 'c':
                    return Literal(ParseControl(start));
                case 'B':
                    throw Error(RegexErrorKind.Syntax,
                        "'\\B' недопустимо внутри класса символов", start);
                case 'A':
                case 'z':
                case 'Z':
                case 'G':
                    throw Error(RegexErrorKind.Syntax,
                        $"Якорь '\\{c}' недопустим внутри класса символов", start);
                case 'k':
                    throw Error(RegexErrorKind.NotRegular,
                        "Обратная ссылка '\\k<...>' не является регулярной конструкцией",
                        start, "backreference");
                case 'g':
                    throw Error(RegexErrorKind.NotRegular,
                        "Обратная ссылка '\\g<...>' не является регулярной конструкцией",
                        start, "backreference");
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    throw Error(RegexErrorKind.NotRegular,
                        "Обратные ссылки внутри класса символов не поддерживаются",
                        start, "backreference");
                default:
                    if (char.IsLetterOrDigit(c))
                    {
                        throw Error(RegexErrorKind.Syntax,
                            $"Неизвестная escape-последовательность '\\{c}'", start);
                    }

                    return Literal(c);
            }

            static (CharClassAtom, char, bool) Literal(char value)
                => (new CharRangeAtom(value, value), value, true);
        }

        private RegexNode ParseEscape()
        {
            int start = _index;
            _index++; // '\'

            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax, "Выражение заканчивается символом '\\'", start);
            }

            char c = Current;
            _index++;

            switch (c)
            {
                case 'd':
                    return Category(CharCategoryKind.Digit, false);
                case 'D':
                    return Category(CharCategoryKind.Digit, true);
                case 'w':
                    return Category(CharCategoryKind.Word, false);
                case 'W':
                    return Category(CharCategoryKind.Word, true);
                case 's':
                    return Category(CharCategoryKind.Space, false);
                case 'S':
                    return Category(CharCategoryKind.Space, true);
                case 'b':
                    return new AnchorNode(AnchorKind.WordBoundary);
                case 'B':
                    return new AnchorNode(AnchorKind.NonWordBoundary);
                case 'A':
                    return new AnchorNode(AnchorKind.StartText);
                case 'z':
                    return new AnchorNode(AnchorKind.EndText);
                case 'Z':
                    return new AnchorNode(AnchorKind.EndTextBeforeFinalNewline);
                case 'p':
                    return new CharClassNode(false, new[] { ParseUnicodeCategory(start, false) });
                case 'P':
                    return new CharClassNode(false, new[] { ParseUnicodeCategory(start, true) });
                case 'n':
                    return new LiteralNode('\n');
                case 'r':
                    return new LiteralNode('\r');
                case 't':
                    return new LiteralNode('\t');
                case 'f':
                    return new LiteralNode('\f');
                case 'v':
                    return new LiteralNode('\v');
                case '0':
                    return new LiteralNode('\0');
                case 'a':
                    return new LiteralNode('\a');
                case 'e':
                    return new LiteralNode('\u001B');
                case 'x':
                    return new LiteralNode(ParseHex(2, start));
                case 'u':
                    return new LiteralNode(ParseHex(4, start));
                case 'c':
                    return new LiteralNode(ParseControl(start));
                case 'k':
                    throw Error(RegexErrorKind.NotRegular,
                        "Обратная ссылка '\\k<...>' делает выражение нерегулярным",
                        start, "backreference");
                case 'g':
                    throw Error(RegexErrorKind.NotRegular,
                        "Обратная ссылка '\\g<...>' делает выражение нерегулярным",
                        start, "backreference");
                case 'G':
                    throw Error(RegexErrorKind.Unsupported,
                        "Якорь '\\G' не поддерживается", start, "\\G");
                case 'R':
                    throw Error(RegexErrorKind.Unsupported,
                        "Последовательность '\\R' не поддерживается", start, "\\R");
                case 'X':
                    throw Error(RegexErrorKind.Unsupported,
                        "Последовательность '\\X' не поддерживается", start, "\\X");
                case 'K':
                    throw Error(RegexErrorKind.Unsupported,
                        "'\\K' не поддерживается", start, "\\K");
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    throw Error(RegexErrorKind.NotRegular,
                        $"Обратная ссылка '\\{c}' делает выражение нерегулярным",
                        start, "backreference");
                default:
                    if (char.IsLetterOrDigit(c))
                    {
                        throw Error(RegexErrorKind.Syntax,
                            $"Неизвестная escape-последовательность '\\{c}'", start);
                    }

                    return new LiteralNode(c);
            }

            static RegexNode Category(CharCategoryKind kind, bool negated)
                => new CharClassNode(false, new[] { new CategoryAtom(kind, negated) });
        }

        private CharClassAtom ParseUnicodeCategory(int escapeStart, bool negated)
        {
            if (AtEnd || Current != '{')
            {
                throw Error(RegexErrorKind.Syntax,
                    "После \\p или \\P ожидается '{'", escapeStart);
            }

            _index++; // '{'
            int nameStart = _index;

            while (!AtEnd && Current != '}')
            {
                _index++;
            }

            if (AtEnd)
            {
                throw Error(RegexErrorKind.Syntax,
                    "Не закрыта категория \\p{...}: отсутствует '}'", escapeStart);
            }

            string name = _source.Substring(nameStart, _index - nameStart);
            _index++; // '}'

            if (!UnicodeCategories.TryGetValue(name, out string? normalized))
            {
                throw Error(RegexErrorKind.Unsupported,
                    $"Категория '\\p{{{name}}}' не поддерживается", escapeStart,
                    "unicode category");
            }

            return new UnicodeCategoryAtom(normalized, negated);
        }

        private char ParseHex(int digits, int escapeStart)
        {
            int value = 0;

            for (int i = 0; i < digits; i++)
            {
                if (AtEnd || !IsHexDigit(Current))
                {
                    throw Error(RegexErrorKind.Syntax,
                        $"Ожидалось {digits} шестнадцатеричных цифр после '\\x' или '\\u'",
                        escapeStart);
                }

                value = value * 16 + HexValue(Current);
                _index++;
            }

            return (char)value;
        }

        private char ParseControl(int escapeStart)
        {
            if (AtEnd || !char.IsLetter(Current))
            {
                throw Error(RegexErrorKind.Syntax,
                    "После '\\c' ожидается буква", escapeStart);
            }

            char c = char.ToUpperInvariant(Current);
            _index++;
            return (char)(c - 64);
        }

        private static bool IsHexDigit(char c)
            => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            _ => c - 'A' + 10
        };

        private static RegexParseException Error(
            RegexErrorKind kind, string message, int position, string? construct = null)
            => new(kind, message, position, construct);
    }
}
