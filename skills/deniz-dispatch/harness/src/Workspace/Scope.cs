static class Scope
{
    static readonly RegexOptions MatchOptions = RegexOptions.CultureInvariant
        | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);

    public static string? Overlap(List<string> a, List<string> b, List<string>? sharedPaths = null)
    {
        foreach (var x in a.Where(g => !g.StartsWith('!')))
        {
            foreach (var y in b.Where(g => !g.StartsWith('!')))
            {
                if (sharedPaths is { Count: > 0 }
                    ? IntersectsOutsideShared(Compile(Normalize(x)), Compile(Normalize(y)), sharedPaths.Select(p => Compile(Normalize(p))).ToList())
                    : Intersects(Compile(Normalize(x)), Compile(Normalize(y))))
                {
                    return $"{x} ~ {y}";
                }
            }
        }

        return null;
    }

    // Difference against the union of the shared-path automata. Checking only a pair's
    // prefixes would wrongly exempt a broad scope that also shares non-shared files.
    static bool IntersectsOutsideShared(List<State> a, List<State> b, List<List<State>> excluded)
    {
        var machines = new List<List<State>> { a, b };
        machines.AddRange(excluded);
        var alphabet = machines.SelectMany(m => m).SelectMany(s => s.Moves)
            .Where(m => m.Literal.HasValue).Select(m => m.Literal!.Value).Append('/').Append('\n').ToHashSet();
        var other = Enumerable.Range(1, char.MaxValue).Select(n => (char)n).First(c => !alphabet.Contains(c));
        alphabet.Add(other);
        int[] Closure(List<State> machine, IEnumerable<int> seeds)
        {
            var found = new HashSet<int>();
            var pending = new Stack<int>(seeds);
            while (pending.TryPop(out var state))
                if (found.Add(state)) foreach (var next in machine[state].Empty) pending.Push(next);
            return found.Order().ToArray();
        }
        var queue = new Queue<int[][]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(machines.Select(m => Closure(m, [0])).ToArray());
        while (queue.TryDequeue(out var states))
        {
            if (!seen.Add(string.Join(";", states.Select(s => string.Join(',', s))))) continue;
            bool Final(int index) => states[index].Contains(machines[index].Count - 1);
            if (Final(0) && Final(1) && !Enumerable.Range(2, excluded.Count).Any(Final)) return true;
            foreach (var c in alphabet)
            {
                var next = machines.Select((machine, i) => Closure(machine, states[i].SelectMany(s => machine[s].Moves)
                    .Where(m => m.Literal is { } literal ? Compatible(m, new Move(0, c)) : Accepts(m, c))
                    .Select(m => m.Target))).ToArray();
                if (next[0].Length > 0 && next[1].Length > 0) queue.Enqueue(next);
            }
        }
        return false;
    }

    // Search the product of two glob automata. Prefixes alone cannot distinguish
    // disjoint suffixes, segment boundaries, or literal braces and brackets.
    static bool Intersects(List<State> a, List<State> b)
    {
        var pending = new Queue<(int A, int B)>();
        var seen = new HashSet<(int A, int B)>();
        pending.Enqueue((0, 0));
        while (pending.TryDequeue(out var pair))
        {
            if (!seen.Add(pair)) continue;
            if (pair.A == a.Count - 1 && pair.B == b.Count - 1) return true;

            foreach (var next in a[pair.A].Empty) pending.Enqueue((next, pair.B));
            foreach (var next in b[pair.B].Empty) pending.Enqueue((pair.A, next));
            foreach (var x in a[pair.A].Moves)
            {
                foreach (var y in b[pair.B].Moves)
                {
                    if (Compatible(x, y)) pending.Enqueue((x.Target, y.Target));
                }
            }
        }

        return false;
    }

    static bool Compatible(Move a, Move b)
    {
        if (a.Literal is { } x && b.Literal is { } y)
            return x == y || (OperatingSystem.IsWindows()
                && Regex.IsMatch(x.ToString(), Regex.Escape(y.ToString()), MatchOptions));
        if (a.Literal is { } literalA) return Accepts(b, literalA);
        if (b.Literal is { } literalB) return Accepts(a, literalB);
        return true;
    }

    static bool Accepts(Move move, char c) => move.CrossesDirectories ? c != '\n' : c != '/';

    sealed class State
    {
        public List<int> Empty { get; } = [];
        public List<Move> Moves { get; } = [];
    }

    readonly record struct Move(int Target, char? Literal = null, bool CrossesDirectories = false);

    static List<State> Compile(string glob)
    {
        var states = new List<State> { new() };
        var current = 0;
        int AddState()
        {
            states.Add(new State());
            return states.Count - 1;
        }

        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                var recursive = i + 1 < glob.Length && glob[i + 1] == '*';
                if (recursive) i++;
                if (recursive && i + 1 < glob.Length && glob[i + 1] == '/')
                {
                    i++;
                    // **/ is empty or text ending in a slash, never a partial segment.
                    var loop = AddState();
                    var next = AddState();
                    states[current].Empty.AddRange([loop, next]);
                    states[loop].Moves.Add(new Move(loop, CrossesDirectories: true));
                    states[loop].Moves.Add(new Move(next, '/'));
                    current = next;
                }
                else
                {
                    states[current].Moves.Add(new Move(current, CrossesDirectories: recursive));
                    var next = AddState();
                    states[current].Empty.Add(next);
                    current = next;
                }
            }
            else
            {
                var next = AddState();
                states[current].Moves.Add(new Move(next, c == '?' ? null : c));
                current = next;
            }
        }

        if (glob.IndexOfAny(['*', '?']) < 0)
        {
            // A plain path covers itself and its descendants, not sibling prefixes.
            var tail = AddState();
            var end = AddState();
            states[current].Empty.Add(end);
            states[current].Moves.Add(new Move(tail, '/'));
            states[tail].Moves.Add(new Move(tail, CrossesDirectories: true));
            states[tail].Empty.Add(end);
            current = end;
        }

        // Preserve the matcher's $ anchor, which also accepts before a final newline.
        var accept = AddState();
        states[current].Empty.Add(accept);
        states[current].Moves.Add(new Move(accept, '\n'));
        return states;
    }

    static string Normalize(string glob)
    {
        // Strip a leading "./" or "/" only: trimming every leading dot turned ".github/..." into
        // "github/..." and flagged in-scope dot-folder edits as out of scope.
        var g = glob.Replace('\\', '/');
        while (g.StartsWith("./", StringComparison.Ordinal))
        {
            g = g[2..];
        }

        g = g.TrimStart('/');
        return g.EndsWith('/') ? g + "**" : g;
    }

    public static bool InScope(string path, List<string>? scope)
    {
        if (scope is not { Count: > 0 })
        {
            return true;
        }

        path = path.Replace('\\', '/');
        return scope.Any(glob => ToRegex(Normalize(glob)).IsMatch(path));
    }

    static Regex ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < glob.Length && glob[i + 1] == '/')
                    {
                        i++;
                        sb.Append("(?:.*/)?");
                    }
                    else
                    {
                        sb.Append(".*");
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        // A plain path also covers everything below it.
        sb.Append(glob.IndexOfAny(['*', '?']) < 0 ? "(?:/.*)?$" : "$");
        return new Regex(sb.ToString(), MatchOptions);
    }
}
