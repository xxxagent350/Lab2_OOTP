namespace Lab2.Engine;

internal abstract class NfaTransition
{
    public int Target { get; }

    protected NfaTransition(int target) => Target = target;
}

internal sealed class EpsilonTransition : NfaTransition
{
    public EpsilonTransition(int target) : base(target)
    {
    }
}

internal sealed class CharTransition : NfaTransition
{
    private readonly Func<char, bool> _predicate;

    public CharTransition(int target, Func<char, bool> predicate) : base(target)
        => _predicate = predicate;

    public bool Matches(char c) => _predicate(c);
}

internal sealed class AssertTransition : NfaTransition
{
    public AnchorKind Kind { get; }

    public AssertTransition(int target, AnchorKind kind) : base(target) => Kind = kind;

    public bool Matches(string input, int position) => AnchorNode.Matches(Kind, input, position);
}

internal sealed class NfaState
{
    public List<NfaTransition> Transitions { get; } = new();
    public bool IsAccept { get; set; }
}

/// <summary>
/// Компилятор дерева разбора в недетерминированный конечный автомат
/// (построение Томпсона). Автомат не хранит captures: для подсветки
/// совпадений достаточно знать границы.
/// </summary>
internal sealed class NfaCompiler
{
    private const int MaxStates = 50_000;

    private readonly bool _ignoreCase;
    private readonly List<NfaState> _states = new();

    public NfaCompiler(bool ignoreCase) => _ignoreCase = ignoreCase;

    public NfaProgram Compile(RegexNode root)
    {
        var fragment = CompileNode(root);
        int accept = NewState();
        _states[accept].IsAccept = true;
        AddEpsilon(fragment.End, accept);
        return new NfaProgram(_states, fragment.Start, accept);
    }

    private readonly record struct Fragment(int Start, int End);

    private Fragment CompileNode(RegexNode node)
    {
        switch (node)
        {
            case EmptyNode:
            {
                int start = NewState();
                int end = NewState();
                AddEpsilon(start, end);
                return new Fragment(start, end);
            }

            case LiteralNode literal:
            {
                int start = NewState();
                int end = NewState();
                char value = literal.Value;
                _states[start].Transitions.Add(
                    new CharTransition(end, c => EqualsChar(c, value)));
                return new Fragment(start, end);
            }

            case AnyCharNode:
            {
                int start = NewState();
                int end = NewState();
                _states[start].Transitions.Add(
                    new CharTransition(end, c => c != '\n'));
                return new Fragment(start, end);
            }

            case CharClassNode charClass:
            {
                int start = NewState();
                int end = NewState();
                _states[start].Transitions.Add(
                    new CharTransition(end, c => charClass.Matches(c, _ignoreCase)));
                return new Fragment(start, end);
            }

            case AnchorNode anchor:
            {
                int start = NewState();
                int end = NewState();
                _states[start].Transitions.Add(new AssertTransition(end, anchor.Kind));
                return new Fragment(start, end);
            }

            case GroupNode group:
                return CompileNode(group.Child);

            case ConcatNode concat:
                return CompileConcat(concat.Children);

            case AlternationNode alternation:
                return CompileAlternation(alternation.Branches);

            case RepeatNode repeat:
                return CompileRepeat(repeat);

            default:
                throw new InvalidOperationException($"Неизвестный узел: {node.GetType().Name}");
        }
    }

    private Fragment CompileConcat(IReadOnlyList<RegexNode> children)
    {
        if (children.Count == 0)
        {
            return CompileNode(new EmptyNode());
        }

        var first = CompileNode(children[0]);
        int current = first.End;

        for (int i = 1; i < children.Count; i++)
        {
            var next = CompileNode(children[i]);
            AddEpsilon(current, next.Start);
            current = next.End;
        }

        return new Fragment(first.Start, current);
    }

    private Fragment CompileAlternation(IReadOnlyList<RegexNode> branches)
    {
        int start = NewState();
        int end = NewState();

        foreach (var branch in branches)
        {
            var fragment = CompileNode(branch);
            AddEpsilon(start, fragment.Start);
            AddEpsilon(fragment.End, end);
        }

        return new Fragment(start, end);
    }

    private Fragment CompileRepeat(RepeatNode repeat)
    {
        if (repeat.Min == 0 && repeat.Max == 0)
        {
            return CompileNode(new EmptyNode());
        }

        int start = NewState();
        int current = start;

        // Обязательные повторения.
        for (int i = 0; i < repeat.Min; i++)
        {
            var fragment = CompileNode(repeat.Child);
            AddEpsilon(current, fragment.Start);
            current = fragment.End;
        }

        if (repeat.Max == int.MaxValue)
        {
            // X{min,} = X{min} X*
            int end = NewState();
            var fragment = CompileNode(repeat.Child);

            AddEpsilon(current, end);          // ноль дополнительных повторений
            AddEpsilon(current, fragment.Start); // войти в цикл
            AddEpsilon(fragment.End, fragment.Start); // повторить ещё раз
            AddEpsilon(fragment.End, end);      // выйти из цикла

            return new Fragment(start, end);
        }

        // X{min,max} = X{min} (X?){max-min}
        for (int i = repeat.Min; i < repeat.Max; i++)
        {
            int next = NewState();
            var fragment = CompileNode(repeat.Child);

            AddEpsilon(current, next);          // пропустить это повторение
            AddEpsilon(current, fragment.Start); // выполнить это повторение
            AddEpsilon(fragment.End, next);

            current = next;
        }

        return new Fragment(start, current);
    }

    private bool EqualsChar(char a, char b)
        => a == b || (_ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b));

    private int NewState()
    {
        if (_states.Count >= MaxStates)
        {
            throw new RegexLimitException(
                $"Слишком сложное выражение: больше {MaxStates} состояний NFA.");
        }

        _states.Add(new NfaState());
        return _states.Count - 1;
    }

    private void AddEpsilon(int from, int to)
        => _states[from].Transitions.Add(new EpsilonTransition(to));
}

/// <summary>
/// NFA с эмуляцией «в ширину»: на каждом шаге хранится множество достижимых
/// состояний вместе с самым левым началом совпадения.
/// Такой подход не подвержен катастрофическому backtracking и не переполняет
/// стек на длинных строках.
/// </summary>
internal sealed class NfaProgram
{
    private readonly NfaState[] _states;
    private readonly int _start;
    private readonly int _accept;

    public int StateCount => _states.Length;

    public NfaProgram(List<NfaState> states, int start, int accept)
    {
        _states = states.ToArray();
        _start = start;
        _accept = accept;
    }

    /// <summary>
    /// Найти самое левое и самое длинное совпадение, начинающееся не раньше
    /// <paramref name="searchStart"/>.
    /// </summary>
    public RegexMatch? Find(
        string input,
        int searchStart,
        ref long steps,
        long maxSteps)
    {
        if (searchStart < 0)
        {
            searchStart = 0;
        }

        if (searchStart > input.Length)
        {
            return null;
        }

        var current = new Dictionary<int, int>();
        int bestStart = int.MaxValue;
        int bestEnd = -1;

        for (int position = searchStart; position <= input.Length; position++)
        {
            // Новая попытка: совпадение может начаться в любой позиции.
            if (bestStart == int.MaxValue)
            {
                AddState(current, _start, position, input, position, ref steps, maxSteps);
            }

            if (current.TryGetValue(_accept, out int acceptStart))
            {
                if (acceptStart < bestStart || (acceptStart == bestStart && position > bestEnd))
                {
                    bestStart = acceptStart;
                    bestEnd = position;
                }
            }

            // Как только найдено совпадение с началом S, все попытки,
            // начинающиеся правее S, уже не могут улучшить результат.
            if (bestStart != int.MaxValue)
            {
                bool keep = false;
                foreach (var pair in current)
                {
                    if (pair.Value <= bestStart)
                    {
                        keep = true;
                        break;
                    }
                }

                if (!keep)
                {
                    break;
                }
            }

            if (position == input.Length)
            {
                break;
            }

            var next = new Dictionary<int, int>();

            foreach (var pair in current)
            {
                if (bestStart != int.MaxValue && pair.Value > bestStart)
                {
                    continue;
                }

                steps += _states[pair.Key].Transitions.Count;
                if (steps > maxSteps)
                {
                    throw new RegexLimitException(
                        "Превышен лимит вычислений при поиске. Упростите выражение или сократите строку.");
                }

                foreach (var transition in _states[pair.Key].Transitions)
                {
                    if (transition is CharTransition character &&
                        character.Matches(input[position]))
                    {
                        AddState(next, transition.Target, pair.Value, input, position + 1, ref steps, maxSteps);
                    }
                }
            }

            current = next;
        }

        if (bestStart == int.MaxValue)
        {
            return null;
        }

        return new RegexMatch(bestStart, bestEnd - bestStart, input.Substring(bestStart, bestEnd - bestStart));
    }

    private void AddState(
        Dictionary<int, int> set,
        int state,
        int start,
        string input,
        int position,
        ref long steps,
        long maxSteps)
    {
        var stack = new Stack<(int State, int Start)>();
        stack.Push((state, start));

        while (stack.Count > 0)
        {
            if (++steps > maxSteps)
            {
                throw new RegexLimitException(
                    "Превышен лимит вычислений при поиске. Упростите выражение или сократите строку.");
            }

            var (id, stateStart) = stack.Pop();

            if (set.TryGetValue(id, out int oldStart) && oldStart <= stateStart)
            {
                continue;
            }

            set[id] = stateStart;

            foreach (var transition in _states[id].Transitions)
            {
                switch (transition)
                {
                    case EpsilonTransition:
                        stack.Push((transition.Target, stateStart));
                        break;
                    case AssertTransition assertion when assertion.Matches(input, position):
                        stack.Push((transition.Target, stateStart));
                        break;
                }
            }
        }
    }
}
