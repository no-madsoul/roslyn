﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.ConvertLinq.ConvertForEachToLinqQuery;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace Microsoft.CodeAnalysis.CSharp.ConvertLinq.ConvertForEachToLinqQuery
{
    internal sealed class DefaultConverter : AbstractConverter
    {
        private static readonly TypeSyntax VarNameIdentifier = SyntaxFactory.IdentifierName("var");
        public DefaultConverter(ForEachInfo<ForEachStatementSyntax, StatementSyntax> forEachInfo)
            : base(forEachInfo) { }

        public override void Convert(SyntaxEditor editor, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            // Filter out identifiers which are not used in statements.
            var symbolNames = new HashSet<string>(ForEachInfo.Statements.SelectMany(statement => semanticModel.AnalyzeDataFlow(statement).ReadInside).Select(symbol => symbol.Name));
            var identifiersUsedInStatements = ForEachInfo.Identifiers.Where(identifier => symbolNames.Contains(identifier.ValueText));

            // If there is a single statement and it is a block, leave it as is.
            // Otherwise, wrap with a block.
            var block = WrapWithBlockIfNecessary(ForEachInfo.Statements.Select(statement => statement.KeepCommentsAndAddElasticMarkers()));

            editor.ReplaceNode(ForEachInfo.ForEachStatement, CreateDefaultReplacementStatement(ForEachInfo, identifiersUsedInStatements, block).WithAdditionalAnnotations(Formatter.Annotation));
        }

        private StatementSyntax CreateDefaultReplacementStatement(
            ForEachInfo<ForEachStatementSyntax, StatementSyntax> forEachInfo,
            IEnumerable<SyntaxToken> identifiers,
            BlockSyntax block)
        {
            var identifiersCount = identifiers.Count();
            if (identifiersCount == 0)
            {
                // Generate foreach(var _ ... select new {})
                return SyntaxFactory.ForEachStatement(VarNameIdentifier, SyntaxFactory.Identifier("_"), CreateQueryExpression(SyntaxFactory.AnonymousObjectCreationExpression(), Enumerable.Empty<SyntaxToken>(), Enumerable.Empty<SyntaxToken>()), block);
            }
            else if (identifiersCount == 1)
            {
                // Generate foreach(var singleIdentifier from ... select singleIdentifier)
                return SyntaxFactory.ForEachStatement(VarNameIdentifier, identifiers.Single(), CreateQueryExpression(SyntaxFactory.IdentifierName(identifiers.Single()), Enumerable.Empty<SyntaxToken>(), Enumerable.Empty<SyntaxToken>()), block);
            }
            else
            {
                var tupleForSelectExpression = SyntaxFactory.TupleExpression(SyntaxFactory.SeparatedList(identifiers.Select(identifier => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(identifier)))));
                var declaration = SyntaxFactory.DeclarationExpression(
                    VarNameIdentifier,
                    SyntaxFactory.ParenthesizedVariableDesignation(
                        SyntaxFactory.SeparatedList<VariableDesignationSyntax>(identifiers.Select(identifier => SyntaxFactory.SingleVariableDesignation(identifier)))));

                // Generate foreach(var (a,b) ... select (a, b))
                return SyntaxFactory.ForEachVariableStatement(declaration, CreateQueryExpression(tupleForSelectExpression, Enumerable.Empty<SyntaxToken>(), Enumerable.Empty<SyntaxToken>()), block);
            }
        }

        private static BlockSyntax WrapWithBlockIfNecessary(IEnumerable<StatementSyntax> statements)
            => (statements.Count() == 1 && statements.Single() is BlockSyntax block) ? block : SyntaxFactory.Block(statements);
    }
}
