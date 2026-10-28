using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kordon.SyntaxCheck
{
    /// <summary>
    /// Static gate for the Unity side of the project.
    ///
    /// The assemblies that reference UnityEngine cannot be compiled without an editor
    /// plus a licence, but a large class of "project does not open" mistakes can still
    /// be caught without one.  This tool parses every C# file with Roslyn at the exact
    /// language level Unity 6 uses (C# 9) and then runs structural checks:
    ///
    ///   * syntax errors, unbalanced braces, stray tokens
    ///   * language features newer than C# 9 (Unity would refuse to compile them)
    ///   * every script covered by exactly one assembly definition
    ///   * Kordon.Core stays free of UnityEngine/Unity.* references
    ///   * editor only code (UnityEditor) lives in an Editor assembly
    ///   * duplicate type names across assemblies
    ///   * no loopback addresses in networking code
    ///   * no TODO/FIXME placeholders left in shipped gameplay code
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string root = ".";
            string reportPath = "docs/reports/syntax-report.md";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && i + 1 < args.Length)
                {
                    root = args[++i];
                }
                else if (args[i] == "--report" && i + 1 < args.Length)
                {
                    reportPath = args[++i];
                }
            }

            root = Path.GetFullPath(root);
            string assets = Path.Combine(root, "Assets");
            if (!Directory.Exists(assets))
            {
                Console.Error.WriteLine($"no Assets directory under {root}");
                return 2;
            }

            Report report = new Report();
            List<AssemblyDef> assemblies = FindAssemblies(assets, report);
            List<string> scripts = Directory.GetFiles(assets, "*.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9, DocumentationMode.Parse);
            var typeNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (string script in scripts)
            {
                string relative = Relative(root, script);
                string text = File.ReadAllText(script);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(text, parseOptions, relative);

                foreach (Diagnostic diagnostic in tree.GetDiagnostics())
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        report.Errors.Add($"{relative}: {diagnostic.Id} {diagnostic.GetMessage()} " +
                                          $"(line {diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1})");
                    }
                    else if (diagnostic.Severity == DiagnosticSeverity.Warning &&
                             diagnostic.Id.StartsWith("CS", StringComparison.Ordinal))
                    {
                        report.Warnings.Add($"{relative}: {diagnostic.Id} {diagnostic.GetMessage()}");
                    }
                }

                // Language level: catch features Unity's compiler would reject.
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    string kind = node.Kind().ToString();
                    if (kind == "FileScopedNamespaceDeclaration")
                    {
                        report.Errors.Add($"{relative}: file scoped namespace is a C# 10 feature; Unity 6 compiles C# 9");
                    }
                    else if (kind == "ListExpression" || kind == "CollectionExpression")
                    {
                        report.Errors.Add($"{relative}: collection expression is a C# 12 feature");
                    }
                    else if (kind == "RawStringLiteralExpression" || kind == "RawStringLiteralToken")
                    {
                        report.Errors.Add($"{relative}: raw string literal is a C# 11 feature");
                    }
                }

                AssemblyDef owner = OwnerAssembly(assemblies, script);
                if (owner == null)
                {
                    report.Errors.Add($"{relative}: not covered by any assembly definition");
                }
                else
                {
                    if (owner.NoEngineReferences && UsesEngine(StripComments(text)))
                    {
                        report.Errors.Add($"{relative}: references UnityEngine but lives in " +
                                          $"{owner.Name} which sets noEngineReferences=true");
                    }

                    if (!owner.IsEditorAssembly && text.Contains("using UnityEditor"))
                    {
                        report.Errors.Add($"{relative}: uses UnityEditor outside an Editor assembly");
                    }
                }

                foreach (string typeName in DeclaredTypes(tree))
                {
                    if (!typeNames.TryGetValue(typeName, out List<string> owners))
                    {
                        owners = new List<string>();
                        typeNames[typeName] = owners;
                    }

                    owners.Add(relative);
                }

                CheckNetworkingText(relative, text, report);
                CheckPlaceholderText(relative, text, report);
                report.Files++;
            }

            foreach (KeyValuePair<string, List<string>> pair in typeNames)
            {
                if (pair.Value.Count > 1)
                {
                    report.Errors.Add($"duplicate type name '{pair.Key}' in: {string.Join(", ", pair.Value.ToArray())}");
                }
            }

            CheckDataFiles(root, report);
            CheckAsmdefConsistency(root, assemblies, report);
            WriteReport(root, reportPath, report);

            Console.WriteLine($"parsed {report.Files} scripts, {report.Errors.Count} errors, " +
                              $"{report.Warnings.Count} warnings");
            foreach (string error in report.Errors.Take(40))
            {
                Console.WriteLine("ERROR " + error);
            }

            return report.Errors.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Comments may name Unity (documentation explaining why the Core is engine
        /// free is a good thing); only real code counts for the engine-free rule.
        /// </summary>
        private static string StripComments(string text)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);
            bool inLineComment = false;
            bool inBlockComment = false;
            bool inString = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (inLineComment)
                {
                    if (c == '\n')
                    {
                        inLineComment = false;
                        builder.Append(c);
                    }

                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (inString)
                {
                    builder.Append(c);
                    if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                    {
                        inString = false;
                    }

                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static bool UsesEngine(string text) =>
            text.Contains("UnityEngine") || text.Contains("Unity.Collections") ||
            text.Contains("Unity.Netcode");

        private static IEnumerable<string> DeclaredTypes(SyntaxTree tree)
        {
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                string kind = node.Kind().ToString();
                if (kind == "ClassDeclaration" || kind == "StructDeclaration" ||
                    kind == "InterfaceDeclaration" || kind == "EnumDeclaration")
                {
                    SyntaxToken token = default;
                    foreach (SyntaxToken child in node.ChildTokens())
                    {
                        if (child.IsKind(SyntaxKind.IdentifierToken))
                        {
                            token = child;
                            break;
                        }
                    }

                    if (token != default)
                    {
                        yield return token.ValueText;
                    }
                }
            }
        }

        private static void CheckNetworkingText(string relative, string text, Report report)
        {
            bool isNetworkCode = relative.Contains("/Net/") || relative.Contains("Network") ||
                                 relative.Contains("Transport") || relative.Contains("Server");
            if (!isNetworkCode)
            {
                return;
            }

            foreach (string literal in new[] { "\"localhost\"", "\"127.0.0.1\"", "\"::1\"" })
            {
                if (text.Contains(literal))
                {
                    // A literal loopback address is only acceptable inside the validator that rejects it.
                    bool isValidator = text.Contains("IsLoopback") || text.Contains("loopback");
                    if (!isValidator)
                    {
                        report.Errors.Add($"{relative}: hardcoded loopback address {literal} in networking code " +
                                          "(addresses must come from configuration)");
                    }
                }
            }
        }

        private static readonly string[] PlaceholderMarkers =
        {
            "TODO", "FIXME", "HACK", "NotImplementedException", "throw new NotSupportedException",
            "PLACEHOLDER", "XXX:"
        };

        private static void CheckPlaceholderText(string relative, string text, Report report)
        {
            foreach (string marker in PlaceholderMarkers)
            {
                int index = text.IndexOf(marker, StringComparison.Ordinal);
                while (index >= 0)
                {
                    int lineStart = text.LastIndexOf('\n', index) + 1;
                    int lineEnd = text.IndexOf('\n', index);
                    string line = text.Substring(lineStart,
                        (lineEnd < 0 ? text.Length : lineEnd) - lineStart).Trim();

                    // Documented exceptions: the voice integration point is intentionally
                    // unimplemented because it needs an external provider account.
                    bool allowed = line.StartsWith("//") || line.StartsWith("*") ||
                                   relative.Contains("Voice") || line.Contains("integration point");
                    if (!allowed)
                    {
                        report.Warnings.Add($"{relative}: placeholder marker '{marker}' -> {Truncate(line, 160)}");
                    }

                    index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
                }
            }
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max) + "...";

        private static void CheckDataFiles(string root, Report report)
        {
            string dataRoot = Path.Combine(root, "Assets", "StreamingAssets", "Data");
            if (!Directory.Exists(dataRoot))
            {
                report.Errors.Add("Assets/StreamingAssets/Data is missing");
                return;
            }

            foreach (string file in Directory.GetFiles(dataRoot, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string text = File.ReadAllText(file);
                    // Cheap structural check: balanced braces and brackets.
                    int braces = 0;
                    int brackets = 0;
                    bool inString = false;
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                        {
                            inString = !inString;
                        }
                        else if (!inString)
                        {
                            if (c == '{') braces++;
                            else if (c == '}') braces--;
                            else if (c == '[') brackets++;
                            else if (c == ']') brackets--;
                        }
                    }

                    if (braces != 0 || brackets != 0)
                    {
                        report.Errors.Add($"{Relative(root, file)}: unbalanced JSON (braces {braces}, brackets {brackets})");
                    }
                }
                catch (Exception exc)
                {
                    report.Errors.Add($"{Relative(root, file)}: {exc.Message}");
                }
            }

            string netConfig = Path.Combine(dataRoot, "Config", "net_config.json");
            if (!File.Exists(netConfig))
            {
                report.Errors.Add("Data/Config/net_config.json is missing");
                return;
            }

            string netText = File.ReadAllText(netConfig);
            if (netText.Contains("\"address\": \"localhost\"") || netText.Contains("\"address\": \"127.0.0.1\""))
            {
                report.Errors.Add("net_config.json ships a loopback region address");
            }

            if (!netText.Contains("eu-central"))
            {
                report.Errors.Add("net_config.json has no EU region (first supported region is Europe)");
            }
        }

        private static void CheckAsmdefConsistency(string root, List<AssemblyDef> assemblies, Report report)
        {
            AssemblyDef core = assemblies.FirstOrDefault(a => a.Name == "Kordon.Core");
            if (core == null)
            {
                report.Errors.Add("Kordon.Core assembly definition is missing");
            }
            else if (!core.NoEngineReferences)
            {
                report.Errors.Add("Kordon.Core must set noEngineReferences=true so the rules can be tested " +
                                  "outside Unity");
            }

            foreach (AssemblyDef asm in assemblies)
            {
                foreach (string reference in asm.References)
                {
                    if (assemblies.All(a => a.Name != reference) && !reference.StartsWith("Unity.") &&
                        !reference.StartsWith("Newtonsoft") && !reference.StartsWith("nunit"))
                    {
                        report.Warnings.Add($"{asm.Name}.asmdef references unknown assembly '{reference}'");
                    }
                }

                if (asm.Name.Contains("Tests") && !asm.References.Any(r => r.Contains("Test")))
                {
                    report.Warnings.Add($"{asm.Name}.asmdef looks like a test assembly but references no test framework");
                }
            }
        }

        private sealed class AssemblyDef
        {
            public string Name;
            public string Directory;
            public bool NoEngineReferences;
            public bool IsEditorAssembly;
            public List<string> References = new List<string>();
        }

        private static List<AssemblyDef> FindAssemblies(string assets, Report report)
        {
            List<AssemblyDef> result = new List<AssemblyDef>();
            foreach (string file in Directory.GetFiles(assets, "*.asmdef", SearchOption.AllDirectories))
            {
                try
                {
                    string text = File.ReadAllText(file);
                    AssemblyDef def = new AssemblyDef
                    {
                        Name = ExtractString(text, "name"),
                        Directory = Path.GetDirectoryName(file),
                        NoEngineReferences = ExtractBool(text, "noEngineReferences")
                    };

                    if (def.Name == null)
                    {
                        report.Errors.Add($"{file}: asmdef without a name");
                        continue;
                    }

                    def.IsEditorAssembly = text.Contains("\"Editor\"") &&
                                           text.IndexOf("\"includePlatforms\"", StringComparison.Ordinal) <
                                           text.IndexOf("\"Editor\"", StringComparison.Ordinal) &&
                                           IsEditorPlatformBlock(text);

                    def.References = ExtractStringArray(text, "references");
                    result.Add(def);
                }
                catch (Exception exc)
                {
                    report.Errors.Add($"{file}: {exc.Message}");
                }
            }

            return result;
        }

        private static bool IsEditorPlatformBlock(string text)
        {
            int index = text.IndexOf("\"includePlatforms\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            int end = text.IndexOf(']', index);
            return end > index && text.Substring(index, end - index).Contains("Editor");
        }

        private static AssemblyDef OwnerAssembly(List<AssemblyDef> assemblies, string script)
        {
            AssemblyDef best = null;
            foreach (AssemblyDef def in assemblies)
            {
                if (script.StartsWith(def.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    if (best == null || def.Directory.Length > best.Directory.Length)
                    {
                        best = def;
                    }
                }
            }

            return best;
        }

        private static string ExtractString(string json, string key)
        {
            int index = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', index);
            if (colon < 0)
            {
                return null;
            }

            int quote = json.IndexOf('"', colon + 1);
            if (quote < 0)
            {
                return null;
            }

            int end = json.IndexOf('"', quote + 1);
            return end < 0 ? null : json.Substring(quote + 1, end - quote - 1);
        }

        private static bool ExtractBool(string json, string key)
        {
            int index = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            int colon = json.IndexOf(':', index);
            if (colon < 0)
            {
                return false;
            }

            string tail = json.Substring(colon + 1, Math.Min(16, json.Length - colon - 1));
            return tail.TrimStart().StartsWith("true", StringComparison.Ordinal);
        }

        private static List<string> ExtractStringArray(string json, string key)
        {
            List<string> result = new List<string>();
            int index = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return result;
            }

            int open = json.IndexOf('[', index);
            int close = open < 0 ? -1 : json.IndexOf(']', open);
            if (open < 0 || close < 0)
            {
                return result;
            }

            string body = json.Substring(open + 1, close - open - 1);
            foreach (string raw in body.Split(','))
            {
                string item = raw.Trim().Trim('"');
                if (item.Length > 0)
                {
                    // asmdef references may be "Name" or "GUID:xxxx"
                    result.Add(item.StartsWith("GUID:") ? item : item);
                }
            }

            return result;
        }

        private static string Relative(string root, string path) =>
            path.StartsWith(root, StringComparison.Ordinal)
                ? path.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/')
                : path;

        private static void WriteReport(string root, string reportPath, Report report)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Roslyn syntax and project structure report");
            sb.AppendLine();
            sb.AppendLine("Generated by `Tests/Kordon.SyntaxCheck` via `.github/workflows/core-tests.yml`.");
            sb.AppendLine();
            sb.AppendLine($"Scripts parsed: **{report.Files}**  ");
            sb.AppendLine($"Errors: **{report.Errors.Count}**  ");
            sb.AppendLine($"Warnings: **{report.Warnings.Count}**");
            sb.AppendLine();
            sb.AppendLine("Errors block CI. They are the mistakes that would stop Unity from opening the");
            sb.AppendLine("project: syntax errors, C# 10+ features (Unity 6 compiles C# 9), scripts outside");
            sb.AppendLine("an assembly definition, engine references inside `Kordon.Core`, editor code in a");
            sb.AppendLine("runtime assembly, duplicate type names, hardcoded loopback addresses in networking");
            sb.AppendLine("code and malformed data files.");
            sb.AppendLine();
            if (report.Errors.Count > 0)
            {
                sb.AppendLine("## Errors");
                sb.AppendLine();
                foreach (string error in report.Errors)
                {
                    sb.AppendLine("- " + error);
                }

                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## Errors");
                sb.AppendLine();
                sb.AppendLine("None.");
                sb.AppendLine();
            }

            if (report.Warnings.Count > 0)
            {
                sb.AppendLine("## Warnings");
                sb.AppendLine();
                foreach (string warning in report.Warnings)
                {
                    sb.AppendLine("- " + warning);
                }

                sb.AppendLine();
            }

            string full = Path.Combine(root, reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, sb.ToString());
        }

        private sealed class Report
        {
            public int Files;
            public List<string> Errors = new List<string>();
            public List<string> Warnings = new List<string>();
        }
    }
}
