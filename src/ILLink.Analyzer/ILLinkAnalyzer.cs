using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.IO;

namespace analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ILLinkAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "ILLink";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Linker-friendly";

        private static DiagnosticDescriptor RuleWarning = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static DiagnosticDescriptor RuleError = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(RuleWarning, RuleError); } }

        public override void Initialize(AnalysisContext context)
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            context.RegisterSyntaxNodeAction(AnalyzeTypeOf, SyntaxKind.TypeOfExpression);
            context.RegisterSyntaxNodeAction(AnalyzeTypeOfGetMember, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(WhatIsMemberBinding, SyntaxKind.MemberBindingExpression);
            // context.RegisterSyntaxNodeAction(WhatIsMemberAccess, SyntaxKind.MemberAccessExpressionSynta);
            
        }

        private void WhatIsMemberBinding(SyntaxNodeAnalysisContext context) {
            // WriteLine("MEMBERBINDING....")
            // WriteLine(context.Node);
            // WriteLine(context.Node.GetType());
            // unclear how this is different from member access
        }

        private void AnalyzeTypeOf(SyntaxNodeAnalysisContext context) {
            var typeOfExpr = (TypeOfExpressionSyntax)context.Node;
            // WriteLine("TYPEOF: " + typeOfExpr + " : " + typeOfExpr.GetType());
        }

        private void AnalyzeTypeOfGetMember(SyntaxNodeAnalysisContext context) {
            var invocationExpr = (InvocationExpressionSyntax)context.Node;
            if (IsUnderstoodReflectionPattern(context, invocationExpr)) {
                return;
            }

            // not an understood reflection pattern.
            // report warnings about unsafe APIs.
        }

        private bool IsUnderstoodReflectionPattern(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpr) {
            return DetectGetMethod(context, invocationExpr);
        }

        private bool IsInvocation(
            SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpr,
            string ns, string methodName,
            out ArgumentSyntax argExpr,
            out ExpressionSyntax head) {
            head = null;
            argExpr = null;
            // TODO: number of args? assume 1 for now.
            var memberAccessExpr = invocationExpr.Expression as MemberAccessExpressionSyntax;
            if (memberAccessExpr?.Name.ToString() != methodName) return false;
            var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpr).Symbol as IMethodSymbol;
            if (!memberSymbol?.ToString().StartsWith(ns + "." + methodName) ?? true) return false;
            var argumentList = invocationExpr.ArgumentList as ArgumentListSyntax;
            // TODO: handle more cases. only look for single argument for now.
            // there are overloads specifying arg types, binding flags, etc.
            if ((argumentList?.Arguments.Count ?? 0) != 1) return false;
            argExpr = argumentList.Arguments[0];
            head = memberAccessExpr.Expression;
            return true;
        }

        private bool IsLiteral(
                SyntaxNodeAnalysisContext context,
                ArgumentSyntax argExpr) {
            
            var stringLiteral = argExpr.Expression as LiteralExpressionSyntax;
            if (stringLiteral == null) {
                return false;
            }
            var stringOpt = context.SemanticModel.GetConstantValue(stringLiteral);
            if (!stringOpt.HasValue) return false;
            var stringValue = stringOpt.Value as string;
            if (stringValue == null) return false;
            // TODO: maybe try to bind to the method?
            // for now, just report that we found a literal.
            return true;
        }

        private bool DetectTypeOf(
                SyntaxNodeAnalysisContext context,
                ExpressionSyntax expr) {
            WriteLine("trying to detect typeof: " + expr);
            if (!(expr is TypeOfExpressionSyntax typeOfExpr)) {
                return false;
            }
            return true;
        }

        private bool DetectGetMethod(
                SyntaxNodeAnalysisContext context,
                InvocationExpressionSyntax invocationExpr) {
            // is it a call to get method?
            // is it understood?
                // does it have typeof?
                // is the typeof understood?
            if (!IsInvocation(context, invocationExpr, "System.Type", "GetMethod",
                                out ArgumentSyntax argExpr,
                                out ExpressionSyntax head)) {
                return false;
            }
            WriteLine("getmethod detected: " + invocationExpr);
            bool isTypeOf = DetectTypeOf(context, head);
            bool isLiteral = IsLiteral(context, argExpr);
            if (!isLiteral) {
                // TODO: consider which location to report.
                // for now, the location of the specific non-constant argument.
                var diagnostic = Diagnostic.Create(RuleWarning, argExpr.GetLocation(), "expected string literal in call to GetMethod");
                context.ReportDiagnostic(diagnostic);
            }

            if (!isTypeOf) {
                var diagnostic = Diagnostic.Create(RuleWarning, invocationExpr.Expression.GetLocation(), "expected typeof");
                context.ReportDiagnostic(diagnostic);
            }
            return isTypeOf && isLiteral;
            // TODO: generic case with MakeGenericType
        }

        private bool DetectMakeGenericType() {
            return false;
            // non-generic
            //  head must be simple typeof

            // generic
            //  head can be a typeof(Foo<Bar>). open generic?
            //  OR simple typeof(Foo).MakeGenericType(...)
        }

        bool first = true;
        string filename = "analyzer_output.txt";

        private void WriteLine(object s) {
            StreamWriter sw;
            FileStream fs = null;
            if (first) {
                fs = new FileStream(filename, FileMode.Create);
                sw = new StreamWriter(fs);
                first = false;
            } else {
                sw = File.AppendText(filename);
            }
            sw.WriteLine(s.ToString());
            sw.Dispose();
            if (first) {
                fs.Dispose();
            }
        }
    }
}
