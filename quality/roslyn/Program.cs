using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length < 1 || args[0] is "--help" or "-h")
{
    Console.WriteLine("Roslyn source architecture: dotnet run --project ArchitectureRoslyn.csproj -- <project root> [max complexity]");
    return args.Length == 0 ? 2 : 0;
}
var root = Path.GetFullPath(args[0]);
if (!Directory.Exists(root)) { Console.Error.WriteLine("ROOT_NOT_FOUND"); return 2; }
var threshold = args.Length >= 2 && int.TryParse(args[1], out int max) && max >= 1 ? max : 15;
var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{ ".git", ".tools", "bin", "obj", ".venv", "node_modules", "dist", "publish", "build", "packages", "quality" };
var methodList = new List<object>();
var types = new List<object>();
var issues = new List<string>();
var duplicateSource = new Dictionary<string, List<string>>();
int scanned = 0, skipped = 0;
foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(p => p))
{
    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
    if (relative.Split('/').Any(excluded.Contains)) continue;
    if (scanned >= 3500 || new FileInfo(file).Length > 1024 * 1024) { skipped++; continue; }
    string source;
    try { source = File.ReadAllText(file, Encoding.UTF8); }
    catch (IOException) { skipped++; continue; }
    scanned++;
    var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
    var node = tree.GetRoot();
    foreach (var error in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Take(3))
        issues.Add(relative + ": " + error.Id + " " + error.GetMessage());
    foreach (var type in node.DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        var ns = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
        var parent = type.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
        var name = type.Identifier.Text;
        var fullName = string.Join(".", new[] { ns, parent, name }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var baseTypes = type.BaseList?.Types.Select(t => t.Type.ToString()).ToArray() ?? Array.Empty<string>();
        types.Add(new { name = fullName, kind = type.Kind().ToString(), path = relative, line = type.GetLocation().GetLineSpan().StartLinePosition.Line + 1, bases = baseTypes });
        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
            if (body is null) continue;
            var methodName = fullName + "." + method.Identifier.Text;
            var syntax = body.DescendantNodesAndSelf().ToList();
            var complexity = 1 +
                syntax.Count(n => n is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax) +
                syntax.OfType<BinaryExpressionSyntax>().Count(n => n.IsKind(SyntaxKind.LogicalAndExpression) || n.IsKind(SyntaxKind.LogicalOrExpression));
            var calls = body.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(x => x.Expression.ToString()).Distinct().Take(150).ToArray();
            var canonical = body.WithoutTrivia().ToFullString();
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            var qualified = methodName + " (" + relative + ")";
            if (canonical.Length > 120)
            {
                if (!duplicateSource.TryGetValue(signature, out var list)) duplicateSource[signature] = list = new List<string>();
                list.Add(qualified);
            }
            methodList.Add(new { type = fullName, name = method.Identifier.Text, qualified, path = relative, line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1, complexity, over_threshold = complexity > threshold, calls, body_hash = signature });
        }
    }
}
var methodsJson = JsonSerializer.SerializeToElement(methodList);
var duplicates = duplicateSource.Where(x => x.Value.Count > 1).Select(x => new { hash = x.Key, methods = x.Value }).ToArray();
var complex = methodsJson.EnumerateArray()
    .Where(x => x.GetProperty("over_threshold").GetBoolean())
    .Select(x => new { name = x.GetProperty("qualified").GetString(), complexity = x.GetProperty("complexity").GetInt32() })
    .OrderByDescending(x => x.complexity).Take(40).ToArray();
var result = new {
    schema = "roslyn-syntax-architecture-v1",
    root,
    files_scanned = scanned,
    files_omitted = skipped,
    types_count = types.Count,
    methods_count = methodList.Count,
    complexity_threshold = threshold,
    over_complexity_count = methodsJson.EnumerateArray().Count(x => x.GetProperty("over_threshold").GetBoolean()),
    duplicate_method_groups = duplicates.Length,
    duplicate_groups = duplicates,
    highest_complexity = complex,
    syntax_errors = issues.Take(40).ToArray(),
    limitation = "Roslyn syntax tree analysis: calls are syntactic candidates, NOT semantic resolved symbols; code coverage and runtime acceptance NOT measured.",
    types,
    methods = methodsJson
};
var output = Path.Combine(root, "quality", "reports");
Directory.CreateDirectory(output);
var fileout = Path.Combine(output, "roslyn-architecture.json");
File.WriteAllText(fileout, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
Console.WriteLine($"ROSLYN_FILES={scanned} TYPES={types.Count} METHODS={methodList.Count} COMPLEX={result.over_complexity_count} DUP_GROUPS={duplicates.Length} SYNTAX_ERRORS={issues.Count}");
Console.WriteLine("ROSLYN_REPORT=" + fileout);
return issues.Count == 0 ? 0 : 1;
