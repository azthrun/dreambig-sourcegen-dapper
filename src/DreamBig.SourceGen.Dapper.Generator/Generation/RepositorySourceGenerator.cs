using System.Globalization;
using System.Text;
using DreamBig.SourceGen.Dapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DreamBig.SourceGen.Dapper.Generator.Generation;

/// <summary>
/// Generates Dapper repository implementations from DreamBig repository attributes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RepositorySourceGenerator : IIncrementalGenerator
{
    private const string DbRepositoryAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbRepositoryAttribute";
    private const string DbUnitOfWorkAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbUnitOfWorkAttribute";
    private const string DbTableAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbTableAttribute";
    private const string DbColumnAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbColumnAttribute";
    private const string DbKeyAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbKeyAttribute";
    private const string DbIgnoreAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbIgnoreAttribute";
    private const string DbQueryAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbQueryAttribute";
    private const string DbJoinAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbJoinAttribute";
    private const string DbStoredProcedureAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbStoredProcedureAttribute";
    private const string DbParamAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbParamAttribute";
    private const string DialectPropertyName = "build_property.DreamBigDapperDialect";
    private static readonly SymbolDisplayFormat NullableAwareTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Initializes the incremental source generation pipeline.
    /// </summary>
    /// <param name="context">Incremental generator context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InterfaceDeclarationSyntax ids && ids.AttributeLists.Count > 0,
                static (ctx, _) => (INamedTypeSymbol?)ctx.SemanticModel.GetDeclaredSymbol((InterfaceDeclarationSyntax)ctx.Node))
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!);

        var dialect = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ResolveDialect(provider));

        var compilationAndInterfaces = context.CompilationProvider
            .Combine(interfaces.Collect())
            .Combine(dialect);

        context.RegisterSourceOutput(compilationAndInterfaces, static (spc, payload) =>
        {
            var ((compilation, candidates), dialect) = payload;
            _ = compilation;
            var generatedRepositories = new Dictionary<string, RepositoryModel>(StringComparer.Ordinal);
            var failedRepositories = new HashSet<string>(StringComparer.Ordinal);
            var generatedUnitsOfWork = new List<UnitOfWorkModel>();

            foreach (var interfaceSymbol in candidates)
            {
                if (!HasAttribute(interfaceSymbol, DbRepositoryAttribute))
                {
                    continue;
                }

                var diagnostics = new List<Diagnostic>();
                var repository = BuildRepositoryModel(interfaceSymbol, diagnostics);

                foreach (var diagnostic in diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (repository is null)
                {
                    failedRepositories.Add(interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    continue;
                }

                var source = RenderRepository(repository, dialect);
                spc.AddSource($"{repository.ImplementationName}.g.cs", SourceText.From(source, Encoding.UTF8));
                generatedRepositories[interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = repository;
            }

            foreach (var interfaceSymbol in candidates)
            {
                if (!HasAttribute(interfaceSymbol, DbUnitOfWorkAttribute))
                {
                    continue;
                }

                var diagnostics = new List<Diagnostic>();
                var unitOfWork = BuildUnitOfWorkModel(interfaceSymbol, generatedRepositories, failedRepositories, diagnostics);

                foreach (var diagnostic in diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                if (unitOfWork is null)
                {
                    continue;
                }

                var source = RenderUnitOfWork(unitOfWork);
                spc.AddSource($"{unitOfWork.ImplementationName}.g.cs", SourceText.From(source, Encoding.UTF8));
                generatedUnitsOfWork.Add(unitOfWork);
            }

            var diSource = RenderServiceRegistration(generatedRepositories.Values, generatedUnitsOfWork);
            spc.AddSource("DreamBigDapperGeneratedServiceCollectionExtensions.g.cs", SourceText.From(diSource, Encoding.UTF8));
        });
    }

    private static RepositoryModel? BuildRepositoryModel(INamedTypeSymbol interfaceSymbol, List<Diagnostic> diagnostics)
    {
        var methods = new List<RepositoryMethodModel>();

        foreach (var member in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            var model = BuildMethodModel(member, diagnostics);
            if (model is null)
            {
                continue;
            }

            methods.Add(model);
        }

        if (methods.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                interfaceSymbol.Locations.FirstOrDefault(),
                interfaceSymbol.Name));
            return null;
        }

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        var repositoryAttribute = interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => IsAttribute(a.AttributeClass, DbRepositoryAttribute));
        var caseSensitive = ReadNamedAttributeBool(repositoryAttribute, "CaseSensitive") ?? true;

        return new RepositoryModel(
            Namespace: ns,
            InterfaceName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            InterfaceQualifiedName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ImplementationName: ResolveRepositoryImplementationName(interfaceSymbol.Name),
            Methods: methods,
            CaseSensitive: caseSensitive);
    }

    private static UnitOfWorkModel? BuildUnitOfWorkModel(
        INamedTypeSymbol interfaceSymbol,
        IReadOnlyDictionary<string, RepositoryModel> generatedRepositories,
        ISet<string> failedRepositories,
        List<Diagnostic> diagnostics)
    {
        var properties = new List<UnitOfWorkRepositoryPropertyModel>();
        var hasInvalidMember = false;

        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet })
            {
                continue;
            }

            if (member is not IPropertySymbol property)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnitOfWorkMemberInvalid,
                    member.Locations.FirstOrDefault(),
                    interfaceSymbol.Name,
                    member.Name));
                hasInvalidMember = true;
                continue;
            }

            if (property.SetMethod is not null || property.GetMethod is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnitOfWorkMemberInvalid,
                    property.Locations.FirstOrDefault(),
                    interfaceSymbol.Name,
                    property.Name));
                hasInvalidMember = true;
                continue;
            }

            if (property.Type is not INamedTypeSymbol { TypeKind: TypeKind.Interface } repositoryInterface
                || !HasAttribute(repositoryInterface, DbRepositoryAttribute))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnitOfWorkRepositoryTypeInvalid,
                    property.Locations.FirstOrDefault(),
                    property.Name,
                    interfaceSymbol.Name));
                hasInvalidMember = true;
                continue;
            }

            var repoInterfaceName = repositoryInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (failedRepositories.Contains(repoInterfaceName) || !generatedRepositories.ContainsKey(repoInterfaceName))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.UnitOfWorkRepositoryGenerationFailed,
                    property.Locations.FirstOrDefault(),
                    property.Name,
                    interfaceSymbol.Name,
                    repositoryInterface.Name));
                hasInvalidMember = true;
                continue;
            }

            var implementationName = generatedRepositories[repoInterfaceName].ImplementationName;

            properties.Add(new UnitOfWorkRepositoryPropertyModel(
                Name: property.Name,
                TypeName: property.Type.ToDisplayString(NullableAwareTypeFormat),
                RepositoryImplementationName: implementationName));
        }

        var duplicate = properties
            .GroupBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static g => g.Count() > 1);

        if (duplicate is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnitOfWorkDuplicateProperty,
                interfaceSymbol.Locations.FirstOrDefault(),
                interfaceSymbol.Name,
                duplicate.Key));
            return null;
        }

        if (properties.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnitOfWorkContainsNoRepositories,
                interfaceSymbol.Locations.FirstOrDefault(),
                interfaceSymbol.Name));
            return null;
        }

        if (hasInvalidMember)
        {
            return null;
        }

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        return new UnitOfWorkModel(
            Namespace: ns,
            InterfaceQualifiedName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ImplementationName: ResolveRepositoryImplementationName(interfaceSymbol.Name),
            RepositoryProperties: properties);
    }

    private static RepositoryMethodModel? BuildMethodModel(IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var operationKind = ResolveOperationKind(method);
        if (operationKind == RepositoryOperationKind.Unknown)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        var parameters = method.Parameters.Select(static p => new MethodParameterModel(
            Name: p.Name,
            TypeName: p.Type.ToDisplayString(NullableAwareTypeFormat),
            ParameterName: ResolveDbParamName(p),
            DbParamAttribute: ReadDbParamAttribute(p),
            IsCancellationToken: IsCancellationTokenType(p.Type))).ToList();

        var methodShape = MethodShape.FromReturnType(method.ReturnType);
        if (!methodShape.IsSupported)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (!methodShape.IsAsync)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.AsyncReturnTypeRequired,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        var cancellationTokenParameter = parameters.FirstOrDefault(static p => p.IsCancellationToken);
        if (cancellationTokenParameter is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.CancellationTokenRequired,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        var operationParameters = parameters.Where(static p => !p.IsCancellationToken).ToList();

        EntityModel? entity = null;

        switch (operationKind)
        {
            case RepositoryOperationKind.Insert:
            case RepositoryOperationKind.Update:
                if (operationParameters.Count == 1
                    && method.Parameters.First(p => p.Name == operationParameters[0].Name).Type is INamedTypeSymbol entityType)
                {
                    entity = BuildEntityModel(entityType, method, diagnostics);
                }

                break;
            case RepositoryOperationKind.GetById:
            case RepositoryOperationKind.GetAll:
            case RepositoryOperationKind.GetPage:
            case RepositoryOperationKind.Query:
                var entityCandidate = methodShape.ElementType as INamedTypeSymbol;
                if (entityCandidate is not null)
                {
                    entity = BuildEntityModel(entityCandidate, method, diagnostics);
                }

                break;
            case RepositoryOperationKind.StoredProcedure:
                var spEntity = methodShape.ElementType as INamedTypeSymbol;
                if (spEntity is not null)
                {
                    entity = BuildEntityModel(spEntity, method, diagnostics);
                }

                break;
            case RepositoryOperationKind.Delete:
                var deleteEntity = TryResolveDeleteEntity(method);
                if (deleteEntity is not null)
                {
                    entity = BuildEntityModel(deleteEntity, method, diagnostics);
                }

                break;
            default:
                break;
        }

        if (methodShape.IsTaskWithoutResult
            && operationKind is RepositoryOperationKind.GetById
                or RepositoryOperationKind.GetAll
                or RepositoryOperationKind.GetPage
                or RepositoryOperationKind.Query
                or RepositoryOperationKind.StoredProcedure)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if ((operationKind == RepositoryOperationKind.Update || operationKind == RepositoryOperationKind.Delete || operationKind == RepositoryOperationKind.GetById)
            && entity is not null
            && entity.KeyProperty is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MissingKey,
                method.Locations.FirstOrDefault(),
                entity.ClrTypeName,
                operationKind.ToString()));
            return null;
        }

        if (operationKind == RepositoryOperationKind.StoredProcedure)
        {
            var spAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbStoredProcedureAttribute));
            var spName = spAttribute?.ConstructorArguments.Length > 0
                ? spAttribute.ConstructorArguments[0].Value as string
                : null;

            if (string.IsNullOrWhiteSpace(spName))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.StoredProcedureNameMissing,
                    method.Locations.FirstOrDefault(),
                    method.Name));
                return null;
            }
        }

        var queryMetadata = ReadQueryMetadata(method, entity, diagnostics);
        if (operationKind == RepositoryOperationKind.StoredProcedure)
        {
            var spAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbStoredProcedureAttribute));
            var spName = spAttribute?.ConstructorArguments.Length > 0
                ? spAttribute.ConstructorArguments[0].Value as string
                : null;
            var spSchemaExplicit = TryReadNamedAttributeString(spAttribute, "Schema", out var spSchema);
            if (!spSchemaExplicit)
            {
                spSchema = null;
            }

            queryMetadata = queryMetadata with
            {
                StoredProcedure = new StoredProcedureMetadata(spName ?? string.Empty, spSchema, spSchemaExplicit),
            };
        }

        return new RepositoryMethodModel(
            Name: method.Name,
            ReturnTypeName: method.ReturnType.ToDisplayString(NullableAwareTypeFormat),
            IsAsync: methodShape.IsAsync,
            ReturnsEnumerable: methodShape.ReturnsEnumerable,
            ReturnsProcedureResult: methodShape.IsProcedureResult,
            IsTaskWithoutResult: methodShape.IsTaskWithoutResult,
            ElementTypeName: methodShape.ElementType?.ToDisplayString(NullableAwareTypeFormat),
            OperationKind: operationKind,
            Parameters: parameters,
            CancellationTokenParameterName: cancellationTokenParameter.Name,
            Entity: entity,
            QueryMetadata: queryMetadata);
    }

    private static QueryMetadata ReadQueryMetadata(IMethodSymbol method, EntityModel? entity, List<Diagnostic> diagnostics)
    {
        var queryAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbQueryAttribute));
        var from = ReadNamedAttributeString(queryAttribute, "From");
        var where = ReadNamedAttributeString(queryAttribute, "Where");
        var schemaExplicit = TryReadNamedAttributeString(queryAttribute, "Schema", out var querySchema);
        if (!schemaExplicit)
        {
            querySchema = null;
        }
        var orderBy = ReadNamedAttributeString(queryAttribute, "OrderBy");
        var orderByDirectionValue = ReadNamedAttributeInt(queryAttribute, "OrderByDirection") ?? 0;
        var orderByDirection = orderByDirectionValue == 1 ? OrderByDirectionModel.Desc : OrderByDirectionModel.Asc;
        var joinOverride = ReadNamedAttributeString(queryAttribute, "Join");

        var joins = method.GetAttributes()
            .Where(a => IsAttribute(a.AttributeClass, DbJoinAttribute))
            .Select((a, index) =>
            {
                var joinTypeValue = ReadNamedAttributeInt(a, "JoinType") ?? 0;
                var joinType = joinTypeValue switch
                {
                    1 => "Left",
                    2 => "Right",
                    3 => "Full",
                    _ => "Inner",
                };

                var joinTableType = ReadNamedAttributeType(a, "JoinTable");
                var joinColumnA = ReadNamedAttributeString(a, "JoinColumnA");
                var joinColumnB = ReadNamedAttributeString(a, "JoinColumnB");
                var joinWhere = ReadNamedAttributeString(a, "Where");
                var joinSchemaExplicit = TryReadNamedAttributeString(a, "Schema", out var joinSchema);
                if (!joinSchemaExplicit)
                {
                    joinSchema = null;
                }

                if (joinTableType is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.JoinPropertyMissing,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        "JoinTable"));
                }

                if (string.IsNullOrWhiteSpace(joinColumnA))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.JoinPropertyMissing,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        "JoinColumnA"));
                }

                if (string.IsNullOrWhiteSpace(joinColumnB))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.JoinPropertyMissing,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        "JoinColumnB"));
                }

                var joinTableEntity = joinTableType is null ? null : BuildEntityModel(joinTableType, method, diagnostics);
                var tableName = joinTableEntity?.TableName ?? "Unknown";
                var resolvedSchema = joinSchemaExplicit ? joinSchema : joinTableEntity?.Schema;
                var resolvedSchemaExplicit = joinSchemaExplicit || (joinTableEntity?.IsSchemaExplicit ?? false);
                var alias = $"t{index + 1}";

                var leftColumn = ResolveJoinColumn(entity, joinColumnA, method, diagnostics, isLeft: true);
                var rightColumn = ResolveJoinColumn(joinTableEntity, joinColumnB, method, diagnostics, isLeft: false);

                return new QueryJoinModel(joinType, tableName, resolvedSchema, resolvedSchemaExplicit, alias, leftColumn, rightColumn, joinWhere);
            })
            .ToList();

        string? orderByColumn = null;
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            if (entity is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.OrderByColumnInvalid,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    orderBy,
                    "(unknown)"));
            }
            else if (!TryResolveColumn(entity, orderBy!, out var resolvedOrderBy))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.OrderByColumnInvalid,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    orderBy,
                    entity.ClrTypeName));
            }
            else
            {
                orderByColumn = resolvedOrderBy;
            }
        }

        return new QueryMetadata(from, querySchema, schemaExplicit, where, orderByColumn, orderByDirection, joinOverride, joins);
    }

    private static EntityModel? BuildEntityModel(INamedTypeSymbol type, IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var tableAttribute = type.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbTableAttribute));
        var tableName = tableAttribute?.ConstructorArguments.Length > 0
            ? tableAttribute.ConstructorArguments[0].Value?.ToString()
            : null;

        var schemaExplicit = TryReadNamedAttributeString(tableAttribute, "Schema", out var schema);
        if (!schemaExplicit)
        {
            schema = null;
        }
        var configuredPrimaryKey = ReadNamedAttributeString(tableAttribute, "PrimaryKey");
        if (string.IsNullOrWhiteSpace(tableName))
        {
            tableName = type.Name;
        }

        var properties = new List<EntityPropertyModel>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic)
            {
                continue;
            }

            if (HasAttribute(property, DbIgnoreAttribute))
            {
                continue;
            }

            var columnAttribute = property.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbColumnAttribute));
            var columnName = columnAttribute?.ConstructorArguments.Length > 0
                ? columnAttribute.ConstructorArguments[0].Value?.ToString()
                : null;
            var resolvedColumnName = string.IsNullOrWhiteSpace(columnName) ? property.Name : columnName!;
            var isConfiguredKey = !string.IsNullOrWhiteSpace(configuredPrimaryKey)
                && (string.Equals(property.Name, configuredPrimaryKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(resolvedColumnName, configuredPrimaryKey, StringComparison.OrdinalIgnoreCase));

            properties.Add(new EntityPropertyModel(
                PropertyName: property.Name,
                ColumnName: resolvedColumnName,
                TypeName: property.Type.ToDisplayString(NullableAwareTypeFormat),
                IsKey: HasAttribute(property, DbKeyAttribute) || isConfiguredKey));
        }

        var duplicate = properties
            .GroupBy(static p => p.ColumnName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static g => g.Count() > 1);

        if (duplicate is not null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.ConflictingColumnMapping,
                method.Locations.FirstOrDefault(),
                type.Name,
                duplicate.Key));
            return null;
        }

        return new EntityModel(
            ClrTypeName: type.ToDisplayString(NullableAwareTypeFormat),
            Schema: schema,
            IsSchemaExplicit: schemaExplicit,
            TableName: tableName!,
            Properties: properties,
            KeyProperty: properties.FirstOrDefault(static p => p.IsKey));
    }

    private static RepositoryOperationKind ResolveOperationKind(IMethodSymbol method)
    {
        if (HasAttribute(method, DbStoredProcedureAttribute))
        {
            return RepositoryOperationKind.StoredProcedure;
        }

        if (HasAttribute(method, DbQueryAttribute) || method.GetAttributes().Any(a => IsAttribute(a.AttributeClass, DbJoinAttribute)))
        {
            return RepositoryOperationKind.Query;
        }

        if (method.Name.StartsWith("Insert", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.Insert;
        }

        if (method.Name.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.Update;
        }

        if (method.Name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.Delete;
        }

        if (method.Name.StartsWith("GetById", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.GetById;
        }

        if (method.Name.StartsWith("GetAll", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.GetAll;
        }

        if (method.Name.StartsWith("GetPage", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.GetPage;
        }

        return RepositoryOperationKind.Unknown;
    }

    private static INamedTypeSymbol? TryResolveDeleteEntity(IMethodSymbol method)
    {
        if (!method.Name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidateName = method.Name.Substring("Delete".Length);
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return null;
        }

        if (candidateName.EndsWith("ById", StringComparison.OrdinalIgnoreCase))
        {
            candidateName = candidateName.Substring(0, candidateName.Length - "ById".Length);
        }

        var containingNamespace = method.ContainingNamespace;
        if (containingNamespace is null)
        {
            return null;
        }

        var directMatch = containingNamespace.GetTypeMembers(candidateName).FirstOrDefault();
        if (directMatch is not null)
        {
            return directMatch;
        }

        if (candidateName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return containingNamespace.GetTypeMembers(candidateName.Substring(0, candidateName.Length - 1)).FirstOrDefault();
        }

        return null;
    }

    private static string RenderRepository(RepositoryModel model, DatabaseDialect dialect)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Dapper;");
        sb.AppendLine("using DreamBig.SourceGen.Dapper.Attributes;");
        sb.AppendLine("using DreamBig.SourceGen.Dapper.Extensions;");
        sb.AppendLine("using DreamBig.SourceGen.Dapper.Internal;");

        if (!string.IsNullOrWhiteSpace(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"public sealed partial class {model.ImplementationName} : {model.InterfaceQualifiedName}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IDbConnection _connection;");
        sb.AppendLine("    private readonly IDbTransaction? _transaction;");
        sb.AppendLine("    private readonly IGeneratedTransactionContext? _transactionContext;");
        sb.AppendLine();
        sb.AppendLine($"    public {model.ImplementationName}(IDbConnection connection, IDbTransaction? transaction = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine("        _transaction = transaction;");
        sb.AppendLine("        _transactionContext = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public {model.ImplementationName}(IDbConnection connection, IGeneratedTransactionContext transactionContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine("        _transaction = null;");
        sb.AppendLine("        _transactionContext = transactionContext ?? throw new ArgumentNullException(nameof(transactionContext));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private IDbTransaction? ResolveTransaction()");
        sb.AppendLine("        => _transactionContext?.CurrentTransaction ?? _transaction;");
        sb.AppendLine();
        sb.AppendLine("    private void EnsureTransactionRequired(string methodName)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (ResolveTransaction() is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        throw new InvalidOperationException($\"Method '{methodName}' requires an active transaction.\");");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in model.Methods)
        {
            sb.Append(RenderMethod(method, dialect, model.CaseSensitive));
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderMethod(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var sb = new StringBuilder();
        var parameterList = string.Join(", ", method.Parameters.Select(static p => $"{p.TypeName} {p.Name}"));

        sb.AppendLine($"    public async {method.ReturnTypeName} {method.Name}({parameterList})");
        sb.AppendLine("    {");

        switch (method.OperationKind)
        {
            case RepositoryOperationKind.Insert:
                RenderInsert(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.Update:
                RenderUpdate(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.Delete:
                RenderDelete(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.GetById:
                RenderGetById(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.GetAll:
                RenderGetAll(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.GetPage:
                RenderGetPage(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.Query:
                RenderQuery(method, sb, dialect, caseSensitive);
                break;
            case RepositoryOperationKind.StoredProcedure:
                RenderStoredProcedure(method, sb, dialect, caseSensitive);
                break;
            default:
                sb.AppendLine("        throw new NotSupportedException(\"Unsupported generated method.\");");
                break;
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static string RenderUnitOfWork(UnitOfWorkModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using DreamBig.SourceGen.Dapper.Internal;");

        if (!string.IsNullOrWhiteSpace(model.Namespace))
        {
            sb.AppendLine($"namespace {model.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine($"public sealed partial class {model.ImplementationName} : {model.InterfaceQualifiedName}, IGeneratedTransactionContext, IAsyncDisposable");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly Func<IDbConnection> _connectionFactory;");
        sb.AppendLine("    private IDbConnection? _connection;");
        sb.AppendLine("    private IDbTransaction? _currentTransaction;");
        sb.AppendLine("    private bool _isDisposed;");

        foreach (var property in model.RepositoryProperties)
        {
            sb.AppendLine($"    private {property.RepositoryImplementationName}? _{property.Name};");
        }

        sb.AppendLine();
        sb.AppendLine($"    public {model.ImplementationName}(Func<IDbConnection> connectionFactory)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public IDbTransaction? CurrentTransaction => _currentTransaction;");
        sb.AppendLine();

        foreach (var property in model.RepositoryProperties)
        {
            sb.AppendLine($"    public {property.TypeName} {property.Name} => _{property.Name} ??= new {property.RepositoryImplementationName}(GetOrCreateConnection(), this);");
        }

        sb.AppendLine();
        sb.AppendLine("    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ThrowIfDisposed();");
        sb.AppendLine();
        sb.AppendLine("        if (_currentTransaction is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"An active transaction already exists.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await EnsureConnectionOpenAsync(cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        _currentTransaction = GetOrCreateConnection().BeginTransaction(isolationLevel);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public async Task CommitAsync(CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ThrowIfDisposed();");
        sb.AppendLine();
        sb.AppendLine("        if (_currentTransaction is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"No active transaction exists to commit.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var transaction = _currentTransaction;");
        sb.AppendLine();
        sb.AppendLine("        if (transaction is System.Data.Common.DbTransaction dbTransaction)");
        sb.AppendLine("        {");
        sb.AppendLine("            await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            _ = cancellationToken;");
        sb.AppendLine("            transaction.Commit();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await DisposeTransactionAsync(transaction).ConfigureAwait(false);");
        sb.AppendLine("        _currentTransaction = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public async Task RollbackAsync(CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        ThrowIfDisposed();");
        sb.AppendLine();
        sb.AppendLine("        if (_currentTransaction is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(\"No active transaction exists to rollback.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var transaction = _currentTransaction;");
        sb.AppendLine();
        sb.AppendLine("        if (transaction is System.Data.Common.DbTransaction dbTransaction)");
        sb.AppendLine("        {");
        sb.AppendLine("            await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            _ = cancellationToken;");
        sb.AppendLine("            transaction.Rollback();");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        await DisposeTransactionAsync(transaction).ConfigureAwait(false);");
        sb.AppendLine("        _currentTransaction = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public async ValueTask DisposeAsync()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_isDisposed)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        _isDisposed = true;");
        sb.AppendLine();
        sb.AppendLine("        if (_currentTransaction is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            await DisposeTransactionAsync(_currentTransaction).ConfigureAwait(false);");
        sb.AppendLine("            _currentTransaction = null;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (_connection is IAsyncDisposable asyncDisposable)");
        sb.AppendLine("        {");
        sb.AppendLine("            await asyncDisposable.DisposeAsync().ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            _connection?.Dispose();");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private IDbConnection GetOrCreateConnection()");
        sb.AppendLine("    {");
        sb.AppendLine("        ThrowIfDisposed();");
        sb.AppendLine();
        sb.AppendLine("        if (_connection is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            return _connection;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        _connection = _connectionFactory() ?? throw new InvalidOperationException(\"The connection factory returned null.\");");
        sb.AppendLine("        return _connection;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private async Task EnsureConnectionOpenAsync(CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        var connection = GetOrCreateConnection();");
        sb.AppendLine();
        sb.AppendLine("        if (connection.State == ConnectionState.Open)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (connection is System.Data.Common.DbConnection dbConnection)");
        sb.AppendLine("        {");
        sb.AppendLine("            await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        _ = cancellationToken;");
        sb.AppendLine("        connection.Open();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static async ValueTask DisposeTransactionAsync(IDbTransaction transaction)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (transaction is IAsyncDisposable asyncDisposable)");
        sb.AppendLine("        {");
        sb.AppendLine("            await asyncDisposable.DisposeAsync().ConfigureAwait(false);");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        transaction.Dispose();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private void ThrowIfDisposed()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_isDisposed)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ObjectDisposedException(GetType().Name);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderServiceRegistration(
        IEnumerable<RepositoryModel> repositories,
        IEnumerable<UnitOfWorkModel> unitOfWorks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace DreamBig.SourceGen.Dapper;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Dependency injection helpers for generated repositories.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class DreamBigDapperGeneratedServiceCollectionExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers generated repositories and unit of work types.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <param name=\"services\">Service collection.</param>");
        sb.AppendLine("    /// <returns>Service collection.</returns>");
        sb.AppendLine("    public static IServiceCollection AddDreamBigDapperGenerated(this IServiceCollection services)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (services is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new ArgumentNullException(nameof(services));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (!services.Any(static sd => sd.ServiceType == typeof(global::DreamBig.SourceGen.Dapper.Internal.DreamBigDapperGeneratedMarker)))");
        sb.AppendLine("        {");
        sb.AppendLine("            services.AddSingleton<global::DreamBig.SourceGen.Dapper.Internal.DreamBigDapperGeneratedMarker>();");
        sb.AppendLine("        }");
        sb.AppendLine();

        foreach (var repository in repositories.OrderBy(static r => r.InterfaceQualifiedName, StringComparer.Ordinal))
        {
            var implementation = GetQualifiedImplementationName(repository.Namespace, repository.ImplementationName);
            sb.AppendLine($"        services.AddScoped<{repository.InterfaceQualifiedName}, {implementation}>();");
        }

        foreach (var unitOfWork in unitOfWorks.OrderBy(static u => u.InterfaceQualifiedName, StringComparer.Ordinal))
        {
            var implementation = GetQualifiedImplementationName(unitOfWork.Namespace, unitOfWork.ImplementationName);
            sb.AppendLine($"        services.AddScoped<{unitOfWork.InterfaceQualifiedName}, {implementation}>();");
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetQualifiedImplementationName(string? ns, string name)
        => string.IsNullOrWhiteSpace(ns) ? $"global::{name}" : $"global::{ns}.{name}";

    private static string ResolveRepositoryImplementationName(string interfaceName)
    {
        if (interfaceName.Length > 1
            && interfaceName[0] == 'I'
            && char.IsUpper(interfaceName[1]))
        {
            interfaceName = interfaceName.Substring(1);
        }

        return interfaceName + "Generated";
    }

    private static void RenderInsert(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || operationParameters.Count != 1)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Insert signature is invalid.\");");
            return;
        }

        var entityParameter = operationParameters[0].Name;
        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var writeColumns = method.Entity.Properties.Where(static p => !p.IsKey).ToList();
        var columnsSql = string.Join(", ", writeColumns.Select(p => Quote(dialect, p.ColumnName, caseSensitive)));
        var valuesSql = string.Join(", ", writeColumns.Select(p => "@" + p.PropertyName));
        var sql = $"INSERT INTO {QualifiedTable(dialect, method.Entity, caseSensitive)} ({columnsSql}) VALUES ({valuesSql});";
        var executeExpression = $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", {entityParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static void RenderUpdate(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count != 1)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Update signature is invalid.\");");
            return;
        }

        var entityParameter = operationParameters[0].Name;
        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var writeColumns = method.Entity.Properties.Where(static p => !p.IsKey).ToList();
        var setSql = string.Join(", ", writeColumns.Select(p => $"{Quote(dialect, p.ColumnName, caseSensitive)} = @{p.PropertyName}"));
        var sql = $"UPDATE {QualifiedTable(dialect, method.Entity, caseSensitive)} SET {setSql} WHERE {Quote(dialect, method.Entity.KeyProperty.ColumnName, caseSensitive)} = @{method.Entity.KeyProperty.PropertyName};";
        var executeExpression = $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", {entityParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static void RenderDelete(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (operationParameters.Count == 0)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Delete signature is invalid.\");");
            return;
        }

        var table = method.Entity is null ? QualifiedTable(dialect, "Unknown", caseSensitive) : QualifiedTable(dialect, method.Entity, caseSensitive);
        var keyColumn = method.Entity?.KeyProperty?.ColumnName ?? operationParameters[0].Name;
        var paramName = operationParameters[0].Name;
        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var sql = $"DELETE FROM {table} WHERE {Quote(dialect, keyColumn, caseSensitive)} = @{paramName};";
        var executeExpression = $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", new {{ {paramName} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static void RenderGetById(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count == 0)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetById signature is invalid.\");");
            return;
        }

        var keyParameter = operationParameters[0].Name;
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var selectSql = BuildEntitySelect(method.Entity, dialect, caseSensitive);
        var sql = $"{selectSql} WHERE {Quote(dialect, method.Entity.KeyProperty.ColumnName, caseSensitive)} = @{keyParameter};";

        sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ {keyParameter} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
        sb.AppendLine("        return rows.FirstOrDefault();");
    }

    private static void RenderGetAll(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        if (method.Entity is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetAll signature is invalid.\");");
            return;
        }

        var sql = BuildEntitySelect(method.Entity, dialect, caseSensitive) + ";";

        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", transaction: transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static void RenderGetPage(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        if (method.Entity is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetPage signature is invalid.\");");
            return;
        }

        var orderBy = method.Entity.KeyProperty?.ColumnName ?? method.Entity.Properties.FirstOrDefault()?.ColumnName ?? "Id";
        var orderBySql = $"{BuildEntitySelect(method.Entity, dialect, caseSensitive)} ORDER BY {Quote(dialect, orderBy, caseSensitive)}";
        var sql = dialect switch
        {
            DatabaseDialect.PostgreSql => $"{orderBySql} LIMIT @take OFFSET @skip;",
            _ => $"{orderBySql} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;",
        };

        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ skip, take }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static void RenderQuery(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var entity = method.Entity;
        var from = method.QueryMetadata.From;
        var baseAlias = "t0";

        if (string.IsNullOrWhiteSpace(from))
        {
            from = entity is null ? QualifiedTable(dialect, "Unknown", caseSensitive) : QualifiedTable(dialect, entity, caseSensitive);
        }
        else if (!IsQualifiedTableExpression(from!))
        {
            from = QualifiedTable(dialect, method.QueryMetadata.Schema, method.QueryMetadata.IsSchemaExplicit, from!, caseSensitive);
        }

        var selectColumns = entity is null
            ? "*"
            : string.Join(", ", entity.Properties.Select(p => $"{baseAlias}.{Quote(dialect, p.ColumnName, caseSensitive)} AS {Quote(dialect, p.PropertyName, caseSensitive)}"));

        var joinSql = string.IsNullOrWhiteSpace(method.QueryMetadata.Join)
            ? BuildJoinClauses(method.QueryMetadata.Joins, baseAlias, dialect, caseSensitive)
            : " " + method.QueryMetadata.Join;

        var whereSql = string.IsNullOrWhiteSpace(method.QueryMetadata.Where) ? string.Empty : $" WHERE {method.QueryMetadata.Where}";
        var orderBySql = method.QueryMetadata.OrderByColumn is null
            ? string.Empty
            : $" ORDER BY {baseAlias}.{Quote(dialect, method.QueryMetadata.OrderByColumn, caseSensitive)} {ToSql(method.QueryMetadata.OrderByDirection)}";
        var sql = $"SELECT {selectColumns} FROM {from} {baseAlias}{joinSql}{whereSql}{orderBySql};";

        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        var anonymousParam = operationParameters.Count == 0
            ? "null"
            : "new { " + string.Join(", ", operationParameters.Select(static p => p.Name)) + " }";

        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", {anonymousParam}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static void RenderStoredProcedure(RepositoryMethodModel method, StringBuilder sb, DatabaseDialect dialect, bool caseSensitive)
    {
        var storedProcedure = method.QueryMetadata.StoredProcedure;
        var procedureName = ResolveStoredProcedureName(dialect, storedProcedure, caseSensitive);

        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine("        var dynamicParameters = new DynamicParameters();");
        foreach (var parameter in method.Parameters.Where(static p => !p.IsCancellationToken))
        {
            var config = parameter.DbParamAttribute;
            if (config is null)
            {
                sb.AppendLine($"        dynamicParameters.Add(\"{parameter.ParameterName}\", {parameter.Name});");
                continue;
            }

            var dbTypeValue = config.DbType.HasValue
                ? $"(System.Data.DbType?)System.Data.DbType.{config.DbType.Value}"
                : "null";

            var sizeValue = config.Size.HasValue ? config.Size.Value.ToString(CultureInfo.InvariantCulture) : "null";
            var direction = config.Direction switch
            {
                DbParamDirectionModel.Output => "System.Data.ParameterDirection.Output",
                DbParamDirectionModel.InputOutput => "System.Data.ParameterDirection.InputOutput",
                _ => "System.Data.ParameterDirection.Input",
            };

            sb.AppendLine($"        dynamicParameters.Add(\"{parameter.ParameterName}\", {parameter.Name}, {dbTypeValue}, {direction}, {sizeValue});");
        }

        var outputNames = method.Parameters
            .Where(static p => !p.IsCancellationToken)
            .Where(static p => p.DbParamAttribute is { Direction: DbParamDirectionModel.Output or DbParamDirectionModel.InputOutput })
            .Select(static p => $"\"{p.ParameterName}\"")
            .ToList();

        sb.AppendLine($"        var result = await _connection.QueryStoredProcedureGeneratedAsync<{method.ElementTypeName ?? "dynamic"}>(\"{EscapeSql(procedureName)}\", dynamicParameters, new[] {{ {string.Join(", ", outputNames)} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");

        if (method.ReturnsProcedureResult)
        {
            sb.AppendLine("        return result;");
            return;
        }

        if (method.ReturnsEnumerable)
        {
            sb.AppendLine("        return result.Rows;");
            return;
        }

        sb.AppendLine("        return result.Rows.FirstOrDefault();");
    }

    private static string BuildEntitySelect(EntityModel entity, DatabaseDialect dialect, bool caseSensitive)
    {
        var selectColumns = string.Join(", ", entity.Properties.Select(p => $"{Quote(dialect, p.ColumnName, caseSensitive)} AS {Quote(dialect, p.PropertyName, caseSensitive)}"));
        return $"SELECT {selectColumns} FROM {QualifiedTable(dialect, entity, caseSensitive)}";
    }

    private static string BuildJoinClauses(IReadOnlyList<QueryJoinModel> joins, string baseAlias, DatabaseDialect dialect, bool caseSensitive)
    {
        if (joins.Count == 0)
        {
            return string.Empty;
        }

        var clauses = joins.Select(join =>
        {
            var keyword = join.JoinType switch
            {
                "Left" => "LEFT OUTER JOIN",
                "Right" => "RIGHT OUTER JOIN",
                "Full" => "FULL OUTER JOIN",
                _ => "INNER JOIN",
            };

            var onClause = $"{baseAlias}.{Quote(dialect, join.LeftColumn, caseSensitive)} = {join.Alias}.{Quote(dialect, join.RightColumn, caseSensitive)}";
            if (!string.IsNullOrWhiteSpace(join.Where))
            {
                onClause += $" AND ({join.Where})";
            }

            var table = QualifiedTable(dialect, join.TableSchema, join.IsSchemaExplicit, join.TableName, caseSensitive);
            return $" {keyword} {table} {join.Alias} ON {onClause}";
        });

        return string.Concat(clauses);
    }

    private static string QualifiedTable(DatabaseDialect dialect, EntityModel entity, bool caseSensitive)
        => QualifiedTable(dialect, entity.Schema, entity.IsSchemaExplicit, entity.TableName, caseSensitive);

    private static string QualifiedTable(DatabaseDialect dialect, string table, bool caseSensitive)
        => QualifiedTable(dialect, schema: null, isSchemaExplicit: false, table, caseSensitive);

    private static string QualifiedTable(DatabaseDialect dialect, string? schema, bool isSchemaExplicit, string table, bool caseSensitive)
    {
        var resolvedSchema = isSchemaExplicit ? schema : ResolveDefaultSchema(dialect, schema);
        if (string.IsNullOrWhiteSpace(resolvedSchema))
        {
            return Quote(dialect, table, caseSensitive);
        }

        return $"{Quote(dialect, resolvedSchema!, caseSensitive)}.{Quote(dialect, table, caseSensitive)}";
    }

    private static bool IsQualifiedTableExpression(string value)
        => value.IndexOf("[", StringComparison.Ordinal) >= 0
            || value.IndexOf("]", StringComparison.Ordinal) >= 0
            || value.IndexOf("\"", StringComparison.Ordinal) >= 0
            || value.IndexOf("`", StringComparison.Ordinal) >= 0
            || value.IndexOf(".", StringComparison.Ordinal) >= 0;

    private static string Quote(DatabaseDialect dialect, string identifier, bool caseSensitive)
        => dialect switch
        {
            DatabaseDialect.PostgreSql => caseSensitive
                ? $"\"{identifier.Replace("\"", "\"\"")}\""
                : identifier,
            _ => $"[{identifier.Replace("]", "]]")}]",
        };

    private static string ResolveDefaultSchema(DatabaseDialect dialect, string? schema)
    {
        if (!string.IsNullOrWhiteSpace(schema))
        {
            return schema!;
        }

        return dialect switch
        {
            DatabaseDialect.PostgreSql => "public",
            _ => "dbo",
        };
    }

    private static string ResolveStoredProcedureName(DatabaseDialect dialect, StoredProcedureMetadata? storedProcedure, bool caseSensitive)
    {
        var name = storedProcedure?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "UnknownProcedure";
        }

        var schema = storedProcedure?.Schema;
        var isSchemaExplicit = storedProcedure?.IsSchemaExplicit ?? false;
        var resolvedSchema = isSchemaExplicit ? schema : ResolveDefaultSchema(dialect, schema);
        if (string.IsNullOrWhiteSpace(resolvedSchema))
        {
            return Quote(dialect, name!, caseSensitive);
        }

        return $"{Quote(dialect, resolvedSchema!, caseSensitive)}.{Quote(dialect, name!, caseSensitive)}";
    }

    private static string EscapeSql(string sql)
        => sql.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static bool HasAttribute(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().Any(a => IsAttribute(a.AttributeClass, attributeName));

    private static bool IsAttribute(INamedTypeSymbol? symbol, string attributeName)
    {
        if (symbol is null)
        {
            return false;
        }

        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullName.StartsWith("global::", StringComparison.Ordinal))
        {
            fullName = fullName.Substring("global::".Length);
        }

        return fullName == attributeName;
    }

    private static string? ReadNamedAttributeString(AttributeData? attributeData, string argumentName)
    {
        return TryReadNamedAttributeString(attributeData, argumentName, out var value) ? value : null;
    }

    private static bool TryReadNamedAttributeString(AttributeData? attributeData, string argumentName, out string? value)
    {
        value = null;
        if (attributeData is null)
        {
            return false;
        }

        foreach (var argument in attributeData.NamedArguments)
        {
            if (argument.Key == argumentName)
            {
                value = argument.Value.Value as string;
                return true;
            }
        }

        return false;
    }

    private static int? ReadNamedAttributeInt(AttributeData? attributeData, string argumentName)
    {
        if (attributeData is null)
        {
            return null;
        }

        foreach (var argument in attributeData.NamedArguments)
        {
            if (argument.Key == argumentName)
            {
                return argument.Value.Value as int?;
            }
        }

        return null;
    }

    private static bool? ReadNamedAttributeBool(AttributeData? attributeData, string argumentName)
    {
        if (attributeData is null)
        {
            return null;
        }

        foreach (var argument in attributeData.NamedArguments)
        {
            if (argument.Key == argumentName)
            {
                return argument.Value.Value as bool?;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ReadNamedAttributeType(AttributeData? attributeData, string argumentName)
    {
        if (attributeData is null)
        {
            return null;
        }

        foreach (var argument in attributeData.NamedArguments)
        {
            if (argument.Key == argumentName)
            {
                return argument.Value.Value as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static bool TryResolveColumn(EntityModel entity, string propertyName, out string columnName)
    {
        var match = entity.Properties.FirstOrDefault(p =>
            string.Equals(p.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            columnName = string.Empty;
            return false;
        }

        columnName = match.ColumnName;
        return true;
    }

    private static string ResolveJoinColumn(
        EntityModel? entity,
        string? propertyName,
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        bool isLeft)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        if (entity is null)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.JoinEntityMissing,
                method.Locations.FirstOrDefault(),
                method.Name,
                isLeft ? "left" : "right"));
            return propertyName ?? string.Empty;
        }

        if (!TryResolveColumn(entity, propertyName!, out var columnName))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.JoinColumnInvalid,
                method.Locations.FirstOrDefault(),
                method.Name,
                propertyName,
                entity.ClrTypeName,
                isLeft ? "left" : "right"));
            return propertyName ?? string.Empty;
        }

        return columnName;
    }

    private static string ToSql(OrderByDirectionModel direction)
        => direction == OrderByDirectionModel.Desc ? "DESC" : "ASC";

    private static string ResolveDbParamName(IParameterSymbol parameter)
    {
        var attribute = parameter.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbParamAttribute));
        var configured = attribute?.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        return string.IsNullOrWhiteSpace(configured) ? "@" + parameter.Name : configured!;
    }

    private static bool IsCancellationTokenType(ITypeSymbol type)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(typeName, "global::System.Threading.CancellationToken", StringComparison.Ordinal);
    }

    private static DbParamAttributeModel? ReadDbParamAttribute(IParameterSymbol parameter)
    {
        var attribute = parameter.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbParamAttribute));
        if (attribute is null)
        {
            return null;
        }

        var directionValue = attribute.NamedArguments.FirstOrDefault(kv => kv.Key == "Direction").Value.Value;
        var direction = directionValue switch
        {
            1 => DbParamDirectionModel.Output,
            2 => DbParamDirectionModel.InputOutput,
            _ => DbParamDirectionModel.Input,
        };

        var dbTypeString = attribute.NamedArguments.FirstOrDefault(kv => kv.Key == "DbType").Value.Value?.ToString();
        var dbType = Enum.TryParse<System.Data.DbType>(dbTypeString, out var parsedDbType) ? parsedDbType : (System.Data.DbType?)null;

        var size = attribute.NamedArguments.FirstOrDefault(kv => kv.Key == "Size").Value.Value as int?;

        return new DbParamAttributeModel(direction, dbType, size);
    }

    private static DatabaseDialect ResolveDialect(AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (optionsProvider.GlobalOptions.TryGetValue(DialectPropertyName, out var value))
        {
            if (string.Equals(value, "PostgreSql", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseDialect.PostgreSql;
            }
        }

        return DatabaseDialect.SqlServer;
    }

    private sealed record RepositoryModel(
        string? Namespace,
        string InterfaceName,
        string InterfaceQualifiedName,
        string ImplementationName,
        IReadOnlyList<RepositoryMethodModel> Methods,
        bool CaseSensitive);

    private sealed record UnitOfWorkModel(
        string? Namespace,
        string InterfaceQualifiedName,
        string ImplementationName,
        IReadOnlyList<UnitOfWorkRepositoryPropertyModel> RepositoryProperties);

    private sealed record UnitOfWorkRepositoryPropertyModel(
        string Name,
        string TypeName,
        string RepositoryImplementationName);

    private sealed record RepositoryMethodModel(
        string Name,
        string ReturnTypeName,
        bool IsAsync,
        bool ReturnsEnumerable,
        bool ReturnsProcedureResult,
        bool IsTaskWithoutResult,
        string? ElementTypeName,
        RepositoryOperationKind OperationKind,
        IReadOnlyList<MethodParameterModel> Parameters,
        string CancellationTokenParameterName,
        EntityModel? Entity,
        QueryMetadata QueryMetadata);

    private sealed record MethodParameterModel(
        string Name,
        string TypeName,
        string ParameterName,
        DbParamAttributeModel? DbParamAttribute,
        bool IsCancellationToken);

    private sealed record EntityModel(
        string ClrTypeName,
        string? Schema,
        bool IsSchemaExplicit,
        string TableName,
        IReadOnlyList<EntityPropertyModel> Properties,
        EntityPropertyModel? KeyProperty);

    private sealed record EntityPropertyModel(
        string PropertyName,
        string ColumnName,
        string TypeName,
        bool IsKey);

    private sealed record QueryMetadata(
        string? From,
        string? Schema,
        bool IsSchemaExplicit,
        string? Where,
        string? OrderByColumn,
        OrderByDirectionModel OrderByDirection,
        string? Join,
        IReadOnlyList<QueryJoinModel> Joins)
    {
        public StoredProcedureMetadata? StoredProcedure { get; init; }
    }

    private sealed record QueryJoinModel(
        string JoinType,
        string TableName,
        string? TableSchema,
        bool IsSchemaExplicit,
        string Alias,
        string LeftColumn,
        string RightColumn,
        string? Where);

    private sealed record StoredProcedureMetadata(
        string Name,
        string? Schema,
        bool IsSchemaExplicit);

    private sealed record DbParamAttributeModel(DbParamDirectionModel Direction, System.Data.DbType? DbType, int? Size);

    private enum DbParamDirectionModel
    {
        Input,
        Output,
        InputOutput,
    }

    private enum OrderByDirectionModel
    {
        Asc,
        Desc,
    }

    private enum RepositoryOperationKind
    {
        Unknown,
        Insert,
        Update,
        Delete,
        GetById,
        GetAll,
        GetPage,
        Query,
        StoredProcedure,
    }

    private enum DatabaseDialect
    {
        SqlServer,
        PostgreSql,
    }

    private sealed record MethodShape(
        bool IsSupported,
        bool IsAsync,
        bool IsTaskWithoutResult,
        bool ReturnsEnumerable,
        bool IsProcedureResult,
        ITypeSymbol? ElementType)
    {
        public static MethodShape FromReturnType(ITypeSymbol returnType)
        {
            if (returnType is INamedTypeSymbol named && named.Name == "Task")
            {
                if (named.IsGenericType)
                {
                    var inner = named.TypeArguments[0];
                    var nested = FromReturnType(inner);
                    return nested with { IsAsync = true };
                }

                return new MethodShape(true, true, true, false, false, null);
            }

            if (returnType is INamedTypeSymbol generic
                && generic.IsGenericType
                && generic.Name is "IEnumerable" or "IReadOnlyList" or "List")
            {
                return new MethodShape(true, false, false, true, false, generic.TypeArguments[0]);
            }

            if (returnType is INamedTypeSymbol procedureResult
                && procedureResult.IsGenericType
                && procedureResult.Name == "GeneratedProcedureResult")
            {
                return new MethodShape(true, false, false, false, true, procedureResult.TypeArguments[0]);
            }

            if (returnType.SpecialType is SpecialType.System_Int32)
            {
                return new MethodShape(true, false, false, false, false, null);
            }

            return returnType is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct }
                ? new MethodShape(true, false, false, false, false, returnType)
                : new MethodShape(false, false, false, false, false, null);
        }
    }
}
