using System.Text;
using Lab2.Engine;

namespace Lab2.Engine.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Базовые литералы и квантификаторы.
        ExpectMatch("literal", "abc", "xxabcxx", 2, 3);
        ExpectMatch("plus", "a+", "baaac", 1, 3);
        ExpectMatch("star", "a*b", "aaab", 0, 4);
        ExpectMatch("optional", "colou?r", "color colour", 0, 5);
        ExpectMatch("braces", "a{2,3}", "aaaaa", 0, 3);
        ExpectAll("braces-all", "a{2,3}", "aaaaa", (0, 3), (3, 2));
        ExpectMatch("exact", "a{3}", "aaaa", 0, 3);
        ExpectMatch("at-least", "a{2,}", "baaaa", 1, 4);

        // Классы символов.
        ExpectMatch("class-range", "[a-c]+", "xxabcz", 2, 3);
        ExpectAll("class-negated", "[^0-9]+", "ab12cd", (0, 2), (4, 2));
        ExpectMatch("class-word-hyphen", @"[\w-]+", "foo-bar", 0, 7);
        ExpectMatch("class-leading-hyphen", "[-a]+", "--aa", 0, 4);
        ExpectMatch("class-trailing-hyphen", "[a-]+", "aa--", 0, 4);
        ExpectMatch("class-literal-close", "[]a]+", "x]aa", 1, 3);
        ExpectAll("class-negated-close", "[^]]+", "ab]cd", (0, 2), (3, 2));
        ExpectMatch("digit-shortcut", @"\d+", "abc123", 3, 3);
        ExpectMatch("word-shortcut", @"\w+", "Привет мир", 0, 6);
        ExpectAll("word-shortcut-all", @"\w+", "Привет мир", (0, 6), (7, 3));
        ExpectMatch("space-shortcut", @"\s+", "a   b", 1, 3);
        ExpectMatch("unicode-letter", @"\p{L}+", "Привет123", 0, 6);
        ExpectMatch("unicode-uppercase", @"\p{Lu}+", "ABC def", 0, 3);
        ExpectMatch("unicode-negated", @"\P{L}+", "abc123", 3, 3);

        // Группы, альтернатива, вложенность.
        ExpectMatch("alternation", "cat|dog", "a dog", 2, 3);
        ExpectMatch("group-repeat", "(ab|cd)+", "abcdab", 0, 6);
        ExpectMatch("non-capturing", "(?:ab)+", "ababab", 0, 6);
        ExpectMatch("named-group", @"(?<word>\w+)", "hello!", 0, 5);
        ExpectMatch("named-group-quotes", @"(?'word'\w+)", "hello!", 0, 5);
        ExpectMatch("nested-group", "((a|b)+c)", "ababc", 0, 5);
        ExpectMatch("empty-branch", "a|", "bbb", 0, 0);

        // Якоря и границы слова.
        ExpectMatch("anchors", "^abc$", "abc", 0, 3);
        ExpectNoMatch("anchors-no", "^abc$", "xabc");
        ExpectMatch("absolute-anchors", @"\Aabc\z", "abc", 0, 3);
        ExpectAll("word-boundary", @"\bcat\b", "cat scatter cat", (0, 3), (12, 3));
        ExpectNoMatch("word-boundary-no", @"\bcat\b", "concat");
        ExpectAll("non-word-boundary", @"\Bcat\B", "scatter", (1, 3));

        // Точка и переводы строк.
        ExpectAll("dot", ".", "a\nb", (0, 1), (2, 1));
        ExpectNoMatch("dot-no-newline", "a.b", "a\nb");
        ExpectMatch("dot-newline-literal", @"a\nb", "a\nb", 0, 3);

        // Экранирование и hex.
        ExpectMatch("escaped-dot", @"a\.b", "a.b", 0, 3);
        ExpectMatch("hex", @"\x41+", "xxAAA", 2, 3);
        ExpectMatch("unicode-hex", @"\u0410+", "xxААА", 2, 3);
        ExpectMatch("control", @"\cA", "\u0001", 0, 1);

        // Пустые совпадения и защита от зацикливания.
        ExpectAll("empty-matches", "a*", "ba", (0, 0), (1, 1), (2, 0));
        ExpectMatch("nullable-repeat", "(a?)*", "aaa", 0, 3);
        ExpectMatch("nullable-repeat-plus", "(a?)+", "aaa", 0, 3);
        ExpectMatch("empty-pattern", "", "abc", 0, 0);

        // Игнорирование регистра.
        ExpectMatch("ignore-case", "привет", "ПРИВЕТ", 0, 6, ignoreCase: true);
        ExpectMatch("ignore-case-range", "[а-я]+", "ПРИВЕТ", 0, 6, ignoreCase: true);
        ExpectNoMatch("case-sensitive", "привет", "ПРИВЕТ");

        // Примеры из интерфейса.
        ExpectMatch("example-email", @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b",
            "Контакты:\r\nsstat@grsu.by\r\nsupport@example.com", 11, 13);
        ExpectMatch("example-phone", @"\d{3}-\d{2}-\d{3}",
            "Телефоны: 123-45-678 и 987-65-432.", 10, 10);
        ExpectMatch("example-date", @"\d{2}\.\d{2}\.\d{4}",
            "Даты: 05.10.2026 и 31.12.2027.", 6, 10);
        ExpectMatch("example-url", @"https?://[^\s]+",
            "Сайты: https://grsu.by и http://example.com/page?x=1", 7, 15);
        ExpectMatch("example-russian-words", @"[А-Яа-яЁё]+",
            "Съешь ещё этих мягких французских булок.", 0, 5);
        ExpectMatch("example-cats-dogs", @"(cat|dog)+",
            "cat dog catdog dogcat", 0, 3);

        // Ошибки синтаксиса.
        ExpectError("unclosed-class", "[a-z", RegexErrorKind.Syntax);
        ExpectError("nothing-to-repeat", "*a", RegexErrorKind.Syntax);
        ExpectError("bad-braces", "a{3,2}", RegexErrorKind.Syntax);
        ExpectError("double-quantifier", "a**", RegexErrorKind.Syntax);
        ExpectError("unclosed-group", "(abc", RegexErrorKind.Syntax);
        ExpectError("extra-close", "abc)", RegexErrorKind.Syntax);
        ExpectError("unknown-escape", @"\q", RegexErrorKind.Syntax);
        ExpectError("bad-range", "[z-a]", RegexErrorKind.Syntax);
        ExpectError("anchor-quantifier", "^*", RegexErrorKind.Syntax);

        // Нерегулярные конструкции.
        ExpectError("backreference", @"(a)\1", RegexErrorKind.NotRegular);
        ExpectError("backreference-k", @"(?<x>a)\k<x>", RegexErrorKind.NotRegular);
        ExpectError("backreference-g", @"(a)\g<1>", RegexErrorKind.NotRegular);
        ExpectError("backreference-python", @"(?P=x)", RegexErrorKind.NotRegular);
        ExpectError("lookahead", "foo(?=bar)", RegexErrorKind.NotRegular);
        ExpectError("negative-lookahead", "foo(?!bar)", RegexErrorKind.NotRegular);
        ExpectError("lookbehind", "(?<=foo)bar", RegexErrorKind.NotRegular);
        ExpectError("negative-lookbehind", "(?<!foo)bar", RegexErrorKind.NotRegular);
        ExpectError("conditional", "(?(1)a|b)", RegexErrorKind.NotRegular);
        ExpectError("recursion", "(?R)", RegexErrorKind.NotRegular);
        ExpectError("balancing-group", "(?<open-close>a)", RegexErrorKind.NotRegular);

        // Регулярные, но не поддерживаемые учебным движком конструкции.
        ExpectError("inline-flags", "(?i)abc", RegexErrorKind.Unsupported);
        ExpectError("atomic-group", "(?>abc)", RegexErrorKind.Unsupported);
        ExpectError("possessive", "a++", RegexErrorKind.Unsupported);
        ExpectError("python-group", "(?P<x>a)", RegexErrorKind.Unsupported);
        ExpectError("large-quantifier", "a{10001}", RegexErrorKind.Unsupported);

        // Лимиты.
        ExpectLengthLimit();
        ExpectStepLimit();
        ExpectPatternLengthLimit();
        ExpectDepthLimit();

        // Проверка, что движок действительно не тянет regex-библиотеку:
        // в исходниках движка не должно быть using System.Text.RegularExpressions.
        ExpectNoRegexLibrary();

        Console.WriteLine();
        Console.WriteLine($"Passed: {_passed}");
        Console.WriteLine($"Failed: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void ExpectMatch(
        string name,
        string pattern,
        string input,
        int index,
        int length,
        bool ignoreCase = false)
    {
        try
        {
            var compiled = RegexPattern.Parse(pattern, ignoreCase);
            var match = compiled.Find(input);

            if (match is null)
            {
                Fail(name, $"ожидалось совпадение [{index}, {length}], получено null");
                return;
            }

            if (match.Value.Index != index || match.Value.Length != length)
            {
                Fail(name,
                    $"ожидалось [{index}, {length}] ('{Escape(input, index, length)}'), " +
                    $"получено [{match.Value.Index}, {match.Value.Length}] ('{match.Value.Value}')");
                return;
            }

            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectNoMatch(string name, string pattern, string input, bool ignoreCase = false)
    {
        try
        {
            var compiled = RegexPattern.Parse(pattern, ignoreCase);
            var match = compiled.Find(input);
            if (match is null)
            {
                Pass(name);
            }
            else
            {
                Fail(name, $"ожидалось отсутствие совпадения, получено [{match.Value.Index}, {match.Value.Length}]");
            }
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectAll(
        string name,
        string pattern,
        string input,
        params (int Index, int Length)[] expected)
    {
        try
        {
            var compiled = RegexPattern.Parse(pattern);
            var actual = compiled.FindAll(input)
                .Select(m => (m.Index, m.Length))
                .ToArray();

            if (actual.SequenceEqual(expected))
            {
                Pass(name);
            }
            else
            {
                Fail(name,
                    $"ожидалось [{string.Join(", ", expected)}], " +
                    $"получено [{string.Join(", ", actual)}]");
            }
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectError(string name, string pattern, RegexErrorKind kind)
    {
        try
        {
            RegexPattern.Parse(pattern);
            Fail(name, $"ожидалась ошибка {kind}, но выражение разобралось");
        }
        catch (RegexParseException ex)
        {
            if (ex.Kind == kind)
            {
                Pass(name);
            }
            else
            {
                Fail(name, $"ожидалась ошибка {kind}, получена {ex.Kind}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Fail(name, $"ожидалось RegexParseException, получено {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectLengthLimit()
    {
        const string name = "input-length-limit";
        try
        {
            var compiled = RegexPattern.Parse("a+");
            compiled.Find(new string('a', RegexPattern.MaxInputLength + 1));
            Fail(name, "ожидался RegexLimitException");
        }
        catch (RegexLimitException)
        {
            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectStepLimit()
    {
        const string name = "step-limit";
        try
        {
            var compiled = RegexPattern.Parse("a+");
            compiled.Find(new string('a', 1000), maxSteps: 10);
            Fail(name, "ожидался RegexLimitException");
        }
        catch (RegexLimitException)
        {
            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectPatternLengthLimit()
    {
        const string name = "pattern-length-limit";
        try
        {
            RegexPattern.Parse(new string('a', RegexParser.MaxPatternLength + 1));
            Fail(name, "ожидался RegexLimitException");
        }
        catch (RegexLimitException)
        {
            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectDepthLimit()
    {
        const string name = "nesting-depth-limit";
        try
        {
            string pattern = new string('(', 600) + "a" + new string(')', 600);
            RegexPattern.Parse(pattern);
            Fail(name, "ожидался RegexParseException");
        }
        catch (RegexParseException ex) when (ex.Kind == RegexErrorKind.Unsupported)
        {
            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpectNoRegexLibrary()
    {
        const string name = "no-regex-library";
        try
        {
            string root = FindEngineDirectory();
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                if (text.Contains("System.Text.RegularExpressions", StringComparison.Ordinal))
                {
                    Fail(name, $"найдено использование regex-библиотеки в {Path.GetFileName(file)}");
                    return;
                }
            }

            Pass(name);
        }
        catch (Exception ex)
        {
            Fail(name, ex.Message);
        }
    }

    private static string FindEngineDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Lab2.Engine");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найден каталог src/Lab2.Engine");
    }

    private static string Escape(string input, int index, int length)
    {
        if (index < 0 || index + length > input.Length)
        {
            return "<вне строки>";
        }

        return input.Substring(index, length)
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static void Pass(string name)
    {
        _passed++;
        Console.WriteLine($"PASS  {name}");
    }

    private static void Fail(string name, string message)
    {
        _failed++;
        Console.WriteLine($"FAIL  {name}: {message}");
    }
}
