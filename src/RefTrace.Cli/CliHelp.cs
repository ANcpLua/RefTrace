using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace RefTrace.Cli;

internal static class CliHelp
{
    internal static void Configure(RootCommand root, TextWriter output, TextWriter error)
    {
        root.Options.OfType<HelpOption>().Single().Action = new HelpAction(root, output);
        var help = new Command("help", "Show command help.");
        var path = new Argument<string[]>("command") { Arity = ArgumentArity.ZeroOrMore };
        help.Arguments.Add(path);
        help.SetAction(parse =>
        {
            Command command = root;
            foreach (var name in parse.GetValue(path) ?? [])
            {
                var child = command.Subcommands.FirstOrDefault(candidate => candidate.Name == name);
                if (child is null)
                {
                    error.WriteLine($"Unknown command '{name}'. Use 'ref help'.");
                    return 2;
                }
                command = child;
            }
            Write(command, root, output);
            return 0;
        });
        root.Subcommands.Add(help);
    }

    private static void Write(Command command, RootCommand root, TextWriter output)
    {
        if (ReferenceEquals(command, root))
        {
            output.WriteLine("ref — trace .NET dependencies and assemblies");
            output.WriteLine("Usage: ref <command> [options]");
            output.WriteLine();
            WriteRows(command.Subcommands.Where(child => !child.Hidden)
                .Select(child => (UsageLabel(child), child.Description ?? "")), output);
            output.WriteLine();
            output.WriteLine("Options: --json  --plain  --version");
            output.WriteLine("Details: ref help <command>");
            return;
        }

        var parts = new List<string>();
        for (var current = command; current is not null; current = current.Parents.OfType<Command>().FirstOrDefault())
            parts.Insert(0, current is RootCommand ? "ref" : UsageLabel(current));
        if (command.Subcommands.Count != 0) parts.Add("<command>");
        parts.Add("[options]");
        output.WriteLine("Usage: " + string.Join(' ', parts));
        output.WriteLine(command.Description);
        output.WriteLine();
        if (command.Arguments.Count != 0)
        {
            output.WriteLine("Arguments:");
            WriteRows(command.Arguments.Select(argument => (ArgumentLabel(argument), argument.Description ?? "")), output);
            output.WriteLine();
        }
        output.WriteLine("Options:");
        WriteRows(command.Options.Concat(root.Options.Where(option => option.Recursive)).Distinct()
            .Where(option => !option.Hidden).Select(option =>
                (string.Join(", ", new[] { option.Name }.Concat(option.Aliases))
                 + (option.Arity.MaximumNumberOfValues > 0 ? " <value>" : "")
                 + (option.Required ? " (required)" : ""), option.Description ?? "")), output);
        if (command.Subcommands.Count == 0) return;
        output.WriteLine();
        output.WriteLine("Commands:");
        WriteRows(command.Subcommands.Where(child => !child.Hidden)
            .Select(child => (UsageLabel(child), child.Description ?? "")), output);
    }

    private static string UsageLabel(Command command)
        => string.Join(' ', new[] { command.Name }.Concat(command.Arguments.Select(ArgumentLabel)));

    private static string ArgumentLabel(Argument argument)
    {
        var label = "<" + argument.Name + ">" + (argument.Arity.MaximumNumberOfValues > 1 ? "..." : "");
        return argument.Arity.MinimumNumberOfValues == 0 ? "[" + label + "]" : label;
    }

    private static void WriteRows(IEnumerable<(string Label, string Description)> rows, TextWriter output)
    {
        var items = rows.ToArray();
        var width = items.Length == 0 ? 0 : items.Max(item => item.Label.Length) + 2;
        foreach (var item in items) output.WriteLine("  " + item.Label.PadRight(width) + item.Description);
    }

    private sealed class HelpAction(RootCommand root, TextWriter output) : SynchronousCommandLineAction
    {
        public override bool ClearsParseErrors => true;
        public override int Invoke(ParseResult parseResult)
        {
            Write(parseResult.CommandResult.Command, root, output);
            return 0;
        }
    }
}
