using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace VpnHood.Net.Toolkit.Analyzers;

// VH0002: .Vhc() in code that runs on a UI thread. There an await continues where it must, fixed -
// ConfigureAwait(false) to leave the UI thread, a bare await or ConfigureAwait(true) to come back to it -
// while .Vhc() follows the CaptureContext debug switch (TaskExtensions.DefaultContinueOnCapturedContext).
// Where it applies is .editorconfig's.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class VhcInUiCodeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "VH0002";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: ".Vhc() in UI code",
        messageFormat: "UI code needs a fixed continuation: ConfigureAwait(false) to leave the UI thread, a bare await to come back to it; .Vhc() follows the CaptureContext debug switch",
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

    // only where the toolkit's .Vhc() can be called
    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var taskExtensionsType = context.Compilation.GetTypeByMetadataName("VpnHood.Net.Toolkit.Extensions.TaskExtensions");
        if (taskExtensionsType != null)
            context.RegisterOperationAction(x => AnalyzeInvocation(x, taskExtensionsType), OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol taskExtensionsType)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Vhc" ||
            !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, taskExtensionsType))
            return;

        // on the name, not on the whole chain before it
        var location = invocation.Syntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess }
            ? memberAccess.Name.GetLocation()
            : invocation.Syntax.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }
}
