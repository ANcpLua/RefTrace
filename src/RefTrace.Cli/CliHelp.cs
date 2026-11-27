using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace RefTrace.Cli;

internal static class CliHelp
{
    internal static void Configure(RootCommand root, TextWriter output, TextWriter error)
    {
        var details = new HelpAction { MaxWidth = 88 };
        root.Options.OfType<HelpOption>().Single().Action = new InformationalAction(parse =>
        {
            if (!ReferenceEquals(parse.CommandResult.Command, root)) return details.Invoke(parse);
            WriteOverview(root, output);
            return 0;
        });
        var help = new Command("help", "Show command help.");
        var path = new Argument<string[]>("command") { Arity = ArgumentArity.ZeroOrMore };
        help.Arguments.Add(path);
        help.Action = new InformationalAction(parse =>
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
            if (ReferenceEquals(command, root)) { WriteOverview(root, output); return 0; }
            return command.Parse(["--help"]).Invoke(new InvocationConfiguration
            {
                Output = output, Error = error, EnableDefaultExceptionHandler = false
            });
        });
        root.Subcommands.Add(help);
        // System.CommandLine's --version prints the informational version of the entry assembly: the package
        // version and the commit. The command runs that option, as help runs --help.
        var version = new Command("version", "Show the version and the commit it was built from.");
        version.SetAction(parse => root.Parse(["--version"]).Invoke(parse.InvocationConfiguration));
        root.Subcommands.Add(version);
    }

    private static void WriteOverview(RootCommand root, TextWriter output)
    {
        output.WriteLine("ref — trace .NET dependencies and assemblies");
        output.WriteLine("Usage: ref <command> [options]");
        output.WriteLine();
        var rows = root.Subcommands.Where(command => !command.Hidden)
            .Select(command => (Label: string.Join(' ', new[] { command.Name }
                .Concat(command.Arguments.Select(ArgumentLabel))), command.Description)).ToArray();
        var width = rows.Max(item => item.Label.Length) + 2;
        foreach (var item in rows) output.WriteLine("  " + item.Label.PadRight(width) + item.Description);
        output.WriteLine();
        output.WriteLine("Options: --json  --plain  --version  --update");
        output.WriteLine("Details: ref help <command>");
    }

    private static string ArgumentLabel(Argument argument)
    {
        var label = "<" + argument.Name + ">" + (argument.Arity.MaximumNumberOfValues > 1 ? "..." : "");
        return argument.Arity.MinimumNumberOfValues == 0 ? "[" + label + "]" : label;
    }

    private sealed class InformationalAction(Func<ParseResult, int> invoke) : SynchronousCommandLineAction
    {
        public override bool ClearsParseErrors => true;
        public override int Invoke(ParseResult parseResult) => invoke(parseResult);
    }
}
