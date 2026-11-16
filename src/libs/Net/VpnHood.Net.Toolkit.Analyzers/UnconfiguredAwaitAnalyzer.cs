using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace VpnHood.Net.Toolkit.Analyzers;

// VH0001: an await of a task, or an await foreach of an async stream, that does not say where it
// continues - .Vhc() in library and background code, a fixed ConfigureAwait where the code must leave
// or keep the UI thread. An await using's hidden DisposeAsync is not counted, which CA2007 counts.
// Where it applies is .editorconfig's.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnconfiguredAwaitAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "VH0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Await without .Vhc()",
        messageFormat: "The await does not say where it continues: add .Vhc(), or a fixed ConfigureAwait where the code must leave or keep the UI thread",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var compilation = context.Compilation;
        var taskTypes = new[] {
                "System.Threading.Tasks.Task",
                "System.Threading.Tasks.Task`1",
                "System.Threading.Tasks.ValueTask",
                "System.Threading.Tasks.ValueTask`1"
            }
            .Select(compilation.GetTypeByMetadataName)
            .OfType<INamedTypeSymbol>()
            .ToImmutableArray();
        var asyncStreamType = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        var configuredStreamType =
            compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1");

        context.RegisterOperationAction(x => AnalyzeAwait(x, taskTypes), OperationKind.Await);
        if (asyncStreamType != null && configuredStreamType != null)
            context.RegisterOperationAction(x => AnalyzeAwaitForEach(x, asyncStreamType, configuredStreamType),
                OperationKind.Loop);
    }

    // a task awaited as it is: a configured one is another type (ConfiguredTaskAwaitable and the like)
    private static void AnalyzeAwait(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> taskTypes)
    {
        var awaitOperation = (IAwaitOperation)context.Operation;
        if (IsTask(awaitOperation.Operation.Type, taskTypes))
            context.ReportDiagnostic(Diagnostic.Create(Rule, awaitOperation.Syntax.GetLocation()));
    }

    // an async stream enumerated as it is, or given only WithCancellation, which keeps the context
    private static void AnalyzeAwaitForEach(OperationAnalysisContext context, INamedTypeSymbol asyncStreamType,
        INamedTypeSymbol configuredStreamType)
    {
        if (context.Operation is not IForEachLoopOperation { IsAsynchronous: true } forEach ||
            forEach.Syntax is not CommonForEachStatementSyntax syntax)
            return;

        var type = forEach.SemanticModel?.GetTypeInfo(syntax.Expression, context.CancellationToken).Type;
        if (type == null)
            return;

        var isConfiguredStream = SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, configuredStreamType);
        if (IsOrImplements(type, asyncStreamType) || isConfiguredStream && !HasConfigureCall(syntax.Expression))
            context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.Expression.GetLocation()));
    }

    private static bool IsTask(ITypeSymbol? type, ImmutableArray<INamedTypeSymbol> taskTypes)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType) {
            var definition = current.OriginalDefinition;
            if (taskTypes.Any(x => SymbolEqualityComparer.Default.Equals(definition, x)))
                return true;
        }

        return false;
    }

    private static bool IsOrImplements(ITypeSymbol type, INamedTypeSymbol interfaceType)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, interfaceType) ||
               type.AllInterfaces.Any(x => SymbolEqualityComparer.Default.Equals(x.OriginalDefinition, interfaceType));
    }

    // .Vhc() or ConfigureAwait anywhere in the call chain: WithCancellation keeps what came before it
    private static bool HasConfigureCall(ExpressionSyntax expression)
    {
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }) {
            if (memberAccess.Name.Identifier.Text is "Vhc" or "ConfigureAwait")
                return true;

            expression = memberAccess.Expression;
        }

        return false;
    }
}
