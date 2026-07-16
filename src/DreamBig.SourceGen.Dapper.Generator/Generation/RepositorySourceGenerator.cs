using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    private const string DbOperationAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbOperationAttribute";
    private const string DbRowVersionAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbRowVersionAttribute";
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
        var repositoryInterfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DbRepositoryAttribute,
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var unitOfWorkInterfaces = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DbUnitOfWorkAttribute,
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var dialect = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ResolveDialect(provider));

        var payload = repositoryInterfaces
            .Combine(unitOfWorkInterfaces)
            .Combine(dialect);

        context.RegisterSourceOutput(payload, static (spc, payload) =>
        {
            var ((repositoryCandidates, unitOfWorkCandidates), dialect) = payload;
            var generatedRepositories = new Dictionary<string, RepositoryModel>(StringComparer.Ordinal);
            var failedRepositories = new HashSet<string>(StringComparer.Ordinal);
            var generatedUnitsOfWork = new List<UnitOfWorkModel>();
            var processedRepositories = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var processedUnitsOfWork = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var usedHintNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var interfaceSymbol in repositoryCandidates)
            {
                if (!processedRepositories.Add(interfaceSymbol))
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
                spc.AddSource(BuildHintName(usedHintNames, repository.Namespace, repository.ImplementationName), SourceText.From(source, Encoding.UTF8));
                generatedRepositories[interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = repository;
            }

            foreach (var interfaceSymbol in unitOfWorkCandidates)
            {
                if (!processedUnitsOfWork.Add(interfaceSymbol))
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
                spc.AddSource(BuildHintName(usedHintNames, unitOfWork.Namespace, unitOfWork.ImplementationName), SourceText.From(source, Encoding.UTF8));
                generatedUnitsOfWork.Add(unitOfWork);
            }

            var diSource = RenderServiceRegistration(generatedRepositories.Values, generatedUnitsOfWork);
            spc.AddSource("DreamBigDapperGeneratedServiceCollectionExtensions.g.cs", SourceText.From(diSource, Encoding.UTF8));
        });
    }

    private static string BuildHintName(ISet<string> usedHintNames, string? ns, string implementationName)
    {
        var baseName = string.IsNullOrWhiteSpace(ns) ? implementationName : $"{ns}.{implementationName}";
        var hintName = $"{baseName}.g.cs";
        var suffix = 1;
        while (!usedHintNames.Add(hintName))
        {
            hintName = $"{baseName}_{suffix++}.g.cs";
        }

        return hintName;
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

        if (HasAmbiguousOperationName(method.Name, operationKind))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.AmbiguousOperationName,
                method.Locations.FirstOrDefault(),
                method.Name,
                operationKind.ToString()));
        }

        var operationParameters = parameters.Where(static p => !p.IsCancellationToken).ToList();

        var operationAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbOperationAttribute));
        var returnsIdentity = operationKind == RepositoryOperationKind.Insert
            && (ReadNamedAttributeBool(operationAttribute, "ReturnIdentity") ?? false);

        INamedTypeSymbol? entityCandidateType = ReadNamedAttributeType(operationAttribute, "Entity");

        if (entityCandidateType is null)
        {
            switch (operationKind)
            {
                case RepositoryOperationKind.Insert:
                case RepositoryOperationKind.Update:
                    if (operationParameters.Count == 1)
                    {
                        var writeParameterType = method.Parameters.First(p => p.Name == operationParameters[0].Name).Type;
                        if (TryGetEnumerableElementType(writeParameterType, out var batchElement)
                            && batchElement is INamedTypeSymbol batchEntity)
                        {
                            entityCandidateType = batchEntity;
                        }
                        else if (writeParameterType is INamedTypeSymbol entityType)
                        {
                            entityCandidateType = entityType;
                        }
                    }

                    break;
                case RepositoryOperationKind.GetById:
                case RepositoryOperationKind.GetAll:
                case RepositoryOperationKind.GetPage:
                case RepositoryOperationKind.GetBy:
                case RepositoryOperationKind.Query:
                case RepositoryOperationKind.StoredProcedure:
                    entityCandidateType = methodShape.ElementType as INamedTypeSymbol;
                    break;
                case RepositoryOperationKind.Delete:
                    entityCandidateType = TryResolveEntityFromName(method, "Delete");
                    break;
                case RepositoryOperationKind.Count:
                    entityCandidateType = TryResolveEntityFromName(method, "Count");
                    break;
                case RepositoryOperationKind.Exists:
                    entityCandidateType = TryResolveEntityFromName(method, "Exists");
                    break;
                default:
                    break;
            }
        }

        EntityModel? entity = null;
        if (entityCandidateType is not null)
        {
            entity = BuildEntityModel(entityCandidateType, method, diagnostics);
            if (entity is null)
            {
                return null;
            }
        }

        if (entity is null
            && operationKind is RepositoryOperationKind.Insert
                or RepositoryOperationKind.Update
                or RepositoryOperationKind.Delete
                or RepositoryOperationKind.GetById
                or RepositoryOperationKind.GetAll
                or RepositoryOperationKind.GetPage
                or RepositoryOperationKind.GetBy
                or RepositoryOperationKind.Count
                or RepositoryOperationKind.Exists)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.EntityUnresolved,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        var filters = new List<ConventionFilterModel>();
        if (entity is not null
            && operationKind is RepositoryOperationKind.GetBy
                or RepositoryOperationKind.Count
                or RepositoryOperationKind.Exists
                or RepositoryOperationKind.Delete)
        {
            IReadOnlyList<string>? filterProperties = null;
            if (TryParseByClause(method.Name, out _, out var byProperties)
                && !(operationKind == RepositoryOperationKind.Delete
                    && byProperties.Count == 1
                    && byProperties[0].Equals("Id", StringComparison.OrdinalIgnoreCase)))
            {
                filterProperties = byProperties;
            }

            if (filterProperties is null
                && operationKind is RepositoryOperationKind.GetBy or RepositoryOperationKind.Count or RepositoryOperationKind.Exists
                && operationParameters.Count > 0)
            {
                filterProperties = operationParameters.Select(static p => p.Name).ToList();
            }

            if (filterProperties is not null)
            {
                if (filterProperties.Count != operationParameters.Count)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedSignature,
                        method.Locations.FirstOrDefault(),
                        method.Name));
                    return null;
                }

                for (var i = 0; i < filterProperties.Count; i++)
                {
                    var propertyName = filterProperties[i];
                    var parameterModel = operationParameters[i];
                    var parameterIsEnumerable = TryGetEnumerableElementType(
                        method.Parameters.First(p => p.Name == parameterModel.Name).Type,
                        out _);

                    if (TryResolveColumn(entity, propertyName, out var filterColumn))
                    {
                        filters.Add(new ConventionFilterModel(filterColumn, parameterModel.Name, IsIn: parameterIsEnumerable));
                        continue;
                    }

                    // A plural property with an enumerable parameter binds the singular column via IN,
                    // e.g. DeleteCustomersByIds(IEnumerable<int> ids) -> WHERE [Id] IN @ids.
                    if (parameterIsEnumerable
                        && propertyName.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                        && TryResolveColumn(entity, propertyName.Substring(0, propertyName.Length - 1), out var singularColumn))
                    {
                        filters.Add(new ConventionFilterModel(singularColumn, parameterModel.Name, IsIn: true));
                        continue;
                    }

                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.ConventionPropertyInvalid,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        propertyName,
                        entity.ClrTypeName));
                    return null;
                }
            }
        }

        if (operationKind == RepositoryOperationKind.GetBy && filters.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if ((operationKind is RepositoryOperationKind.Insert or RepositoryOperationKind.Update && operationParameters.Count != 1)
            || (operationKind is RepositoryOperationKind.Delete or RepositoryOperationKind.GetById && operationParameters.Count == 0)
            || (operationKind is RepositoryOperationKind.GetPage && operationParameters.Count != 2))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (operationKind is RepositoryOperationKind.GetPage
            && !TryResolveGetPageParameters(operationParameters, out _, out _))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.GetPageParametersUnrecognized,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (methodShape.IsTaskWithoutResult
            && (operationKind is RepositoryOperationKind.GetById
                or RepositoryOperationKind.GetAll
                or RepositoryOperationKind.GetPage
                or RepositoryOperationKind.GetBy
                or RepositoryOperationKind.Count
                or RepositoryOperationKind.Exists
                or RepositoryOperationKind.Query
                or RepositoryOperationKind.StoredProcedure
                || returnsIdentity))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (operationKind == RepositoryOperationKind.Count
            && (methodShape.ReturnsEnumerable
                || methodShape.IsProcedureResult
                || methodShape.IsPagedResult
                || (methodShape.ElementType is not null && methodShape.ElementType.SpecialType != SpecialType.System_Int64)))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (operationKind == RepositoryOperationKind.Exists
            && (methodShape.ReturnsEnumerable
                || methodShape.IsProcedureResult
                || methodShape.IsPagedResult
                || methodShape.ElementType?.SpecialType != SpecialType.System_Boolean))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (methodShape.IsPagedResult && operationKind != RepositoryOperationKind.GetPage)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (methodShape.IsAsyncStream
            && operationKind is not (RepositoryOperationKind.GetAll
                or RepositoryOperationKind.GetBy
                or RepositoryOperationKind.Query))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if (returnsIdentity
            && operationParameters.Count == 1
            && TryGetEnumerableElementType(method.Parameters.First(p => p.Name == operationParameters[0].Name).Type, out _))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        if ((operationKind == RepositoryOperationKind.Update
                || (operationKind == RepositoryOperationKind.Delete && filters.Count == 0)
                || operationKind == RepositoryOperationKind.GetById
                || returnsIdentity)
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

        if (entity is not null
            && ((operationKind == RepositoryOperationKind.Insert && !entity.Properties.Any(static p => (!p.IsKey || !p.IsDbGenerated) && !p.IsRowVersion))
                || (operationKind == RepositoryOperationKind.Update && !entity.Properties.Any(static p => !p.IsKey && !p.IsRowVersion))))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NoWritableColumns,
                method.Locations.FirstOrDefault(),
                entity.ClrTypeName,
                operationKind.ToString()));
            return null;
        }

        StoredProcedureMetadata? storedProcedureMetadata = null;
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

            var spSchemaExplicit = TryReadNamedAttributeString(spAttribute, "Schema", out var spSchema);
            if (!spSchemaExplicit)
            {
                spSchema = null;
            }

            storedProcedureMetadata = new StoredProcedureMetadata(spName!, spSchema, spSchemaExplicit);
        }

        var queryMetadata = operationKind == RepositoryOperationKind.GetPage
            ? ReadGetPageOrdering(method, entity, diagnostics)
            : ReadQueryMetadata(method, entity, diagnostics, reportUnusedParameters: operationKind == RepositoryOperationKind.Query);
        if (storedProcedureMetadata is not null)
        {
            queryMetadata = queryMetadata with { StoredProcedure = storedProcedureMetadata };
        }

        if (operationKind == RepositoryOperationKind.Query
            && entity is null
            && string.IsNullOrWhiteSpace(queryMetadata.From))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.EntityUnresolved,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        return new RepositoryMethodModel(
            Name: method.Name,
            ReturnTypeName: method.ReturnType.ToDisplayString(NullableAwareTypeFormat),
            IsAsync: methodShape.IsAsync,
            ReturnsEnumerable: methodShape.ReturnsEnumerable,
            ReturnsProcedureResult: methodShape.IsProcedureResult,
            ReturnsPagedResult: methodShape.IsPagedResult,
            ReturnsAsyncStream: methodShape.IsAsyncStream,
            IsTaskWithoutResult: methodShape.IsTaskWithoutResult,
            ElementTypeName: methodShape.ElementType?.ToDisplayString(NullableAwareTypeFormat),
            OperationKind: operationKind,
            Parameters: parameters,
            CancellationTokenParameterName: cancellationTokenParameter.Name,
            Entity: entity,
            QueryMetadata: queryMetadata,
            Filters: filters,
            ReturnsIdentity: returnsIdentity);
    }

    private static QueryMetadata ReadQueryMetadata(IMethodSymbol method, EntityModel? entity, List<Diagnostic> diagnostics, bool reportUnusedParameters = false)
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
        var joinAttributes = method.GetAttributes()
            .Where(a => IsAttribute(a.AttributeClass, DbJoinAttribute))
            .ToList();

        var nodes = new Dictionary<string, QueryNodeModel>(StringComparer.OrdinalIgnoreCase);
        var joins = new List<QueryJoinModel>();
        var whereFragments = new List<string>();
        var rawParameterExpressions = new List<string?> { where, orderBy, joinOverride };
        string? configuredOrderBy = null;
        var configuredOrderByDirection = orderByDirection;
        string? baseAlias = null;

        foreach (var joinAttribute in joinAttributes)
        {
            var joinTypeValue = ReadNamedAttributeInt(joinAttribute, "JoinType") ?? 0;
            var joinType = joinTypeValue switch
            {
                1 => "Left",
                2 => "Right",
                3 => "Full",
                _ => "Inner",
            };

            var joinTableTypeA = ReadNamedAttributeType(joinAttribute, "JoinTableA");
            var joinTableTypeB = ReadNamedAttributeType(joinAttribute, "JoinTableB");
            var joinColumnA = ReadNamedAttributeString(joinAttribute, "JoinColumnA");
            var joinColumnB = ReadNamedAttributeString(joinAttribute, "JoinColumnB");
            var aliasA = ReadNamedAttributeString(joinAttribute, "AliasA");
            var aliasB = ReadNamedAttributeString(joinAttribute, "AliasB");
            var joinWhere = ReadNamedAttributeString(joinAttribute, "Where");
            var joinOn = ReadNamedAttributeString(joinAttribute, "On");
            var joinOrderBy = ReadNamedAttributeString(joinAttribute, "OrderBy");
            rawParameterExpressions.Add(joinWhere);
            rawParameterExpressions.Add(joinOn);
            rawParameterExpressions.Add(joinOrderBy);
            var joinOrderByDirectionValue = ReadNamedAttributeInt(joinAttribute, "OrderByDirection") ?? 0;
            var joinOrderByDirection = joinOrderByDirectionValue == 1 ? OrderByDirectionModel.Desc : OrderByDirectionModel.Asc;
            var schemaAExplicit = TryReadNamedAttributeString(joinAttribute, "SchemaA", out var joinSchemaA);
            var schemaBExplicit = TryReadNamedAttributeString(joinAttribute, "SchemaB", out var joinSchemaB);
            if (!schemaAExplicit)
            {
                joinSchemaA = null;
            }

            if (!schemaBExplicit)
            {
                joinSchemaB = null;
            }

            if (joinTableTypeA is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.JoinPropertyMissing,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    "JoinTableA"));
            }

            if (joinTableTypeB is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.JoinPropertyMissing,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    "JoinTableB"));
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

            var joinEntityA = joinTableTypeA is null ? null : BuildEntityModel(joinTableTypeA, method, diagnostics);
            var joinEntityB = joinTableTypeB is null ? null : BuildEntityModel(joinTableTypeB, method, diagnostics);
            var resolvedAliasA = ResolveQueryAlias(aliasA, joinEntityA?.TableName, joinEntityA?.ClrTypeName, from);
            var resolvedAliasB = ResolveQueryAlias(aliasB, joinEntityB?.TableName, joinEntityB?.ClrTypeName, joinEntityB?.TableName);

            var leftNode = RegisterOrResolveJoinSource(
                nodes,
                resolvedAliasA,
                joinEntityA,
                schemaAExplicit ? joinSchemaA : joinEntityA?.Schema,
                schemaAExplicit || (joinEntityA?.IsSchemaExplicit ?? false),
                method,
                diagnostics,
                ref baseAlias);

            var rightNode = RegisterJoinNode(
                nodes,
                resolvedAliasB,
                joinEntityB,
                schemaBExplicit ? joinSchemaB : joinEntityB?.Schema,
                schemaBExplicit || (joinEntityB?.IsSchemaExplicit ?? false),
                method,
                diagnostics);

            var leftColumn = ResolveJoinColumn(leftNode?.Entity ?? joinEntityA, joinColumnA, method, diagnostics, isLeft: true);
            var rightColumn = ResolveJoinColumn(rightNode?.Entity ?? joinEntityB, joinColumnB, method, diagnostics, isLeft: false);
            var rewrittenOn = RewriteQueryExpression(joinOn, nodes, method, diagnostics, dialect: null, caseSensitive: true);

            joins.Add(new QueryJoinModel(
                joinType,
                rightNode?.TableName ?? "Unknown",
                rightNode?.TableSchema,
                rightNode?.IsSchemaExplicit ?? false,
                leftNode?.Alias ?? resolvedAliasA,
                rightNode?.Alias ?? resolvedAliasB,
                leftColumn,
                rightColumn,
                rewrittenOn));

            var rewrittenJoinWhere = RewriteQueryExpression(joinWhere, nodes, method, diagnostics, dialect: null, caseSensitive: true);
            if (!string.IsNullOrWhiteSpace(rewrittenJoinWhere))
            {
                whereFragments.Add(rewrittenJoinWhere!);
            }

            if (!string.IsNullOrWhiteSpace(joinOrderBy))
            {
                if (!string.IsNullOrWhiteSpace(configuredOrderBy))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.OrderByConflict,
                        method.Locations.FirstOrDefault(),
                        method.Name));
                }
                else
                {
                    configuredOrderBy = RewriteQueryExpression(joinOrderBy, nodes, method, diagnostics, dialect: null, caseSensitive: true);
                    configuredOrderByDirection = joinOrderByDirection;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(baseAlias))
        {
            baseAlias = ResolveQueryAlias(explicitAlias: null, entity?.TableName, entity?.ClrTypeName, from);
            _ = RegisterJoinNode(
                nodes,
                baseAlias,
                entity,
                entity?.Schema ?? querySchema,
                entity?.IsSchemaExplicit ?? schemaExplicit,
                method,
                diagnostics,
                allowExisting: true);
        }

        var rewrittenWhere = RewriteQueryExpression(where, nodes, method, diagnostics, dialect: null, caseSensitive: true);
        if (!string.IsNullOrWhiteSpace(rewrittenWhere))
        {
            whereFragments.Insert(0, rewrittenWhere!);
        }

        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            if (!string.IsNullOrWhiteSpace(configuredOrderBy))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.OrderByConflict,
                    method.Locations.FirstOrDefault(),
                    method.Name));
            }
            else
            {
                configuredOrderBy = RewriteQueryExpression(orderBy, nodes, method, diagnostics, dialect: null, caseSensitive: true);
            }
        }

        var combinedWhere = whereFragments.Count == 0
            ? null
            : string.Join(" AND ", whereFragments.Select(static fragment => $"({fragment})"));

        ValidateQueryParameterReferences(method, rawParameterExpressions, diagnostics, reportUnusedParameters);

        return new QueryMetadata(
            from,
            querySchema,
            schemaExplicit,
            baseAlias ?? "querySource",
            combinedWhere,
            configuredOrderBy,
            configuredOrderByDirection,
            joinOverride,
            joins);
    }

    private static QueryMetadata ReadGetPageOrdering(IMethodSymbol method, EntityModel? entity, List<Diagnostic> diagnostics)
    {
        var queryAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbQueryAttribute));
        var orderBy = ReadNamedAttributeString(queryAttribute, "OrderBy");
        var orderByDirectionValue = ReadNamedAttributeInt(queryAttribute, "OrderByDirection") ?? 0;
        var orderByDirection = orderByDirectionValue == 1 ? OrderByDirectionModel.Desc : OrderByDirectionModel.Asc;

        string? orderByColumn = null;
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            if (entity is not null && TryResolveColumn(entity, orderBy!.Trim(), out var resolvedColumn))
            {
                orderByColumn = resolvedColumn;
            }
            else
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.OrderByColumnInvalid,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    orderBy,
                    entity?.ClrTypeName ?? "<unknown>"));
            }
        }

        return new QueryMetadata(
            From: null,
            Schema: null,
            IsSchemaExplicit: false,
            BaseAlias: "querySource",
            WhereSql: null,
            OrderByExpression: orderByColumn,
            OrderByDirection: orderByDirection,
            Join: null,
            Joins: Array.Empty<QueryJoinModel>());
    }

    private static bool TryResolveGetPageParameters(
        IReadOnlyList<MethodParameterModel> operationParameters,
        out string skipParameterName,
        out string takeParameterName)
    {
        skipParameterName = string.Empty;
        takeParameterName = string.Empty;
        if (operationParameters.Count != 2)
        {
            return false;
        }

        var first = operationParameters[0].Name;
        var second = operationParameters[1].Name;

        if (IsGetPageSkipName(first) && IsGetPageTakeName(second))
        {
            skipParameterName = first;
            takeParameterName = second;
            return true;
        }

        if (IsGetPageTakeName(first) && IsGetPageSkipName(second))
        {
            skipParameterName = second;
            takeParameterName = first;
            return true;
        }

        return false;
    }

    private static bool IsGetPageSkipName(string name)
        => name.Equals("skip", StringComparison.OrdinalIgnoreCase)
            || name.Equals("offset", StringComparison.OrdinalIgnoreCase);

    private static bool IsGetPageTakeName(string name)
        => name.Equals("take", StringComparison.OrdinalIgnoreCase)
            || name.Equals("limit", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pageSize", StringComparison.OrdinalIgnoreCase)
            || name.Equals("fetch", StringComparison.OrdinalIgnoreCase);

    private static void ValidateQueryParameterReferences(
        IMethodSymbol method,
        IEnumerable<string?> expressions,
        List<Diagnostic> diagnostics,
        bool reportUnusedParameters)
    {
        var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in method.Parameters)
        {
            if (!IsCancellationTokenType(parameter.Type))
            {
                parameterNames.Add(parameter.Name);
            }
        }

        var reportedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var expression in expressions)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            _ = RewriteOutsideLiterals(expression!, segment =>
            {
                // The lookbehind skips @@server_variables and PostgreSQL operators such as <@.
                foreach (Match match in Regex.Matches(segment, "(?<![@<:$!])@(?<name>[A-Za-z_][A-Za-z0-9_]*)"))
                {
                    var name = match.Groups["name"].Value;
                    usedNames.Add(name);
                    if (!parameterNames.Contains(name) && reportedNames.Add(name))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticDescriptors.QueryParameterUnknown,
                            method.Locations.FirstOrDefault(),
                            method.Name,
                            name));
                    }
                }

                return segment;
            });
        }

        if (!reportUnusedParameters)
        {
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            if (!IsCancellationTokenType(parameter.Type) && !usedNames.Contains(parameter.Name))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.QueryParameterUnused,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    parameter.Name));
            }
        }
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

            var keyAttribute = property.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbKeyAttribute));
            var isKey = keyAttribute is not null || isConfiguredKey;
            var isDbGenerated = keyAttribute is null || (ReadNamedAttributeBool(keyAttribute, "Generated") ?? true);
            var isRowVersion = HasAttribute(property, DbRowVersionAttribute);

            properties.Add(new EntityPropertyModel(
                PropertyName: property.Name,
                ColumnName: resolvedColumnName,
                TypeName: property.Type.ToDisplayString(NullableAwareTypeFormat),
                IsKey: isKey,
                IsDbGenerated: isDbGenerated,
                IsRowVersion: isRowVersion));
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
            KeyProperty: properties.FirstOrDefault(static p => p.IsKey),
            RowVersionProperty: properties.FirstOrDefault(static p => p.IsRowVersion));
    }

    private static RepositoryOperationKind ResolveOperationKind(IMethodSymbol method)
    {
        if (HasAttribute(method, DbStoredProcedureAttribute))
        {
            return RepositoryOperationKind.StoredProcedure;
        }

        var operationAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbOperationAttribute));
        if (operationAttribute is not null
            && operationAttribute.ConstructorArguments.Length > 0
            && operationAttribute.ConstructorArguments[0].Value is int operationValue)
        {
            return operationValue switch
            {
                0 => RepositoryOperationKind.Insert,
                1 => RepositoryOperationKind.Update,
                2 => RepositoryOperationKind.Delete,
                3 => RepositoryOperationKind.GetById,
                4 => RepositoryOperationKind.GetAll,
                5 => RepositoryOperationKind.GetPage,
                6 => RepositoryOperationKind.GetBy,
                7 => RepositoryOperationKind.Count,
                8 => RepositoryOperationKind.Exists,
                _ => RepositoryOperationKind.Unknown,
            };
        }

        if (method.Name.StartsWith("GetPage", StringComparison.OrdinalIgnoreCase)
            && IsOrderingOnlyQueryAttribute(method))
        {
            return RepositoryOperationKind.GetPage;
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

        if (method.Name.StartsWith("Count", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.Count;
        }

        if (method.Name.StartsWith("Exists", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryOperationKind.Exists;
        }

        if (method.Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
            && TryParseByClause(method.Name, out _, out var byProperties))
        {
            return byProperties.Count == 1 && byProperties[0].Equals("Id", StringComparison.OrdinalIgnoreCase)
                ? RepositoryOperationKind.GetById
                : RepositoryOperationKind.GetBy;
        }

        return RepositoryOperationKind.Unknown;
    }

    /// <summary>
    /// Splits a convention method name at the last "By" (followed by an uppercase letter) into a stem
    /// and "And"-separated property names. An "Async" suffix is stripped from the stem either way.
    /// </summary>
    private static bool TryParseByClause(string methodName, out string stem, out IReadOnlyList<string> properties)
    {
        var name = methodName;
        if (name.EndsWith("Async", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - "Async".Length);
        }

        stem = name;
        properties = Array.Empty<string>();

        var index = -1;
        for (var i = name.Length - 3; i > 0; i--)
        {
            if (name[i] == 'B' && name[i + 1] == 'y' && char.IsUpper(name[i + 2]))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        stem = name.Substring(0, index);
        var clause = name.Substring(index + 2);

        var parts = new List<string>();
        var start = 0;
        var position = 1;
        while (position + 3 < clause.Length)
        {
            if (clause[position] == 'A'
                && clause[position + 1] == 'n'
                && clause[position + 2] == 'd'
                && char.IsUpper(clause[position + 3]))
            {
                parts.Add(clause.Substring(start, position - start));
                position += 3;
                start = position;
                continue;
            }

            position++;
        }

        parts.Add(clause.Substring(start));
        properties = parts;
        return true;
    }

    private static bool IsOrderingOnlyQueryAttribute(IMethodSymbol method)
    {
        var queryAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbQueryAttribute));
        if (queryAttribute is null || method.GetAttributes().Any(a => IsAttribute(a.AttributeClass, DbJoinAttribute)))
        {
            return false;
        }

        return queryAttribute.NamedArguments.Length > 0
            && queryAttribute.NamedArguments.All(static argument => argument.Key is "OrderBy" or "OrderByDirection");
    }

    private static INamedTypeSymbol? TryResolveEntityFromName(IMethodSymbol method, string prefix)
    {
        if (!method.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var parameter in method.Parameters)
        {
            if (IsCancellationTokenType(parameter.Type))
            {
                continue;
            }

            if (parameter.Type is INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } namedType
                && !IsSystemNamespace(namedType.ContainingNamespace))
            {
                return namedType;
            }
        }

        _ = TryParseByClause(method.Name, out var stem, out _);
        var candidateName = stem.Substring(prefix.Length);

        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return null;
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

    private static bool IsSystemNamespace(INamespaceSymbol? ns)
    {
        if (ns is null || ns.IsGlobalNamespace)
        {
            return false;
        }

        var display = ns.ToDisplayString();
        return display == "System" || display.StartsWith("System.", StringComparison.Ordinal);
    }

    private static bool HasAmbiguousOperationName(string methodName, RepositoryOperationKind operationKind)
    {
        var stem = operationKind switch
        {
            RepositoryOperationKind.Insert => "Insert",
            RepositoryOperationKind.Update => "Update",
            RepositoryOperationKind.Delete => "Delete",
            RepositoryOperationKind.GetById => "GetById",
            RepositoryOperationKind.GetAll => "GetAll",
            RepositoryOperationKind.GetPage => "GetPage",
            _ => null,
        };

        if (stem is null || methodName.Length <= stem.Length)
        {
            return false;
        }

        var remainder = methodName.Substring(stem.Length);
        foreach (var otherStem in new[] { "Insert", "Update", "Delete" })
        {
            var index = remainder.IndexOf(otherStem, StringComparison.Ordinal);
            while (index >= 0)
            {
                var end = index + otherStem.Length;
                if (end >= remainder.Length || char.IsUpper(remainder[end]))
                {
                    return true;
                }

                index = remainder.IndexOf(otherStem, end, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static string TrimNullableSuffix(string typeName)
        => typeName.EndsWith("?", StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - 1)
            : typeName;

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

        var sqlConstants = BuildSqlConstants(model, dialect);
        if (sqlConstants.Count > 0)
        {
            sb.AppendLine("    /// <summary>SQL text executed by the generated repository methods.</summary>");
            sb.AppendLine("    public static class Sql");
            sb.AppendLine("    {");
            for (var i = 0; i < sqlConstants.Count; i++)
            {
                var entry = sqlConstants[i];
                if (i > 0)
                {
                    sb.AppendLine();
                }

                sb.AppendLine($"        /// <summary>SQL executed by <c>{entry.MethodName}</c>.</summary>");
                sb.AppendLine($"        public const string {entry.ConstName} = \"{EscapeSql(entry.Sql)}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine();
        }

        for (var i = 0; i < model.Methods.Count; i++)
        {
            var method = model.Methods[i];
            var sqlConstName = sqlConstants.FirstOrDefault(entry => entry.MethodIndex == i)?.ConstName;
            sb.Append(RenderMethod(method, sqlConstName));
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static IReadOnlyList<SqlConstantEntry> BuildSqlConstants(RepositoryModel model, DatabaseDialect dialect)
    {
        var entries = new List<SqlConstantEntry>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < model.Methods.Count; i++)
        {
            var method = model.Methods[i];
            var sql = BuildMethodSql(method, dialect, model.CaseSensitive);
            if (sql is null)
            {
                continue;
            }

            var constName = method.Name;
            var suffix = 2;
            while (!usedNames.Add(constName))
            {
                constName = $"{method.Name}_{suffix++}";
            }

            entries.Add(new SqlConstantEntry(i, method.Name, constName, sql));
        }

        return entries;
    }

    private static string? BuildMethodSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
        => method.OperationKind switch
        {
            RepositoryOperationKind.Insert => BuildInsertSql(method, dialect, caseSensitive),
            RepositoryOperationKind.Update => BuildUpdateSql(method, dialect, caseSensitive),
            RepositoryOperationKind.Delete => BuildDeleteSql(method, dialect, caseSensitive),
            RepositoryOperationKind.GetById => BuildGetByIdSql(method, dialect, caseSensitive),
            RepositoryOperationKind.GetAll => BuildGetAllSql(method, dialect, caseSensitive),
            RepositoryOperationKind.GetPage => BuildGetPageSql(method, dialect, caseSensitive),
            RepositoryOperationKind.GetBy => BuildGetBySql(method, dialect, caseSensitive),
            RepositoryOperationKind.Count => BuildCountSql(method, dialect, caseSensitive),
            RepositoryOperationKind.Exists => BuildCountSql(method, dialect, caseSensitive),
            RepositoryOperationKind.Query => BuildQuerySql(method, dialect, caseSensitive),
            RepositoryOperationKind.StoredProcedure => BuildStoredProcedureSql(method, dialect, caseSensitive),
            _ => null,
        };

    private static string RenderMethod(RepositoryMethodModel method, string? sqlConstName)
    {
        var sb = new StringBuilder();
        var parameterList = string.Join(", ", method.Parameters.Select(static p => $"{p.TypeName} {p.Name}"));

        // Streaming methods return IAsyncEnumerable<T> directly and must not be declared async.
        sb.AppendLine(method.ReturnsAsyncStream
            ? $"    public {method.ReturnTypeName} {method.Name}({parameterList})"
            : $"    public async {method.ReturnTypeName} {method.Name}({parameterList})");
        sb.AppendLine("    {");

        switch (method.OperationKind)
        {
            case RepositoryOperationKind.Insert:
                RenderInsert(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.Update:
                RenderUpdate(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.Delete:
                RenderDelete(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.GetById:
                RenderGetById(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.GetAll:
                RenderGetAll(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.GetPage:
                RenderGetPage(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.GetBy:
                RenderGetBy(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.Count:
                RenderCount(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.Exists:
                RenderExists(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.Query:
                RenderQuery(method, sb, sqlConstName);
                break;
            case RepositoryOperationKind.StoredProcedure:
                RenderStoredProcedure(method, sb, sqlConstName);
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

        sb.AppendLine($"public sealed partial class {model.ImplementationName} : {model.InterfaceQualifiedName}, IGeneratedTransactionContext, IDisposable, IAsyncDisposable");
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
        sb.AppendLine("        var connection = GetOrCreateConnection();");
        sb.AppendLine("        if (connection is System.Data.Common.DbConnection dbConnection)");
        sb.AppendLine("        {");
        sb.AppendLine("            _currentTransaction = await dbConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            _currentTransaction = connection.BeginTransaction(isolationLevel);");
        sb.AppendLine("        }");
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
        sb.AppendLine("    public void Dispose()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_isDisposed)");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        _isDisposed = true;");
        sb.AppendLine();
        sb.AppendLine("        _currentTransaction?.Dispose();");
        sb.AppendLine("        _currentTransaction = null;");
        sb.AppendLine("        _connection?.Dispose();");
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
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
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
            sb.AppendLine($"        services.TryAddScoped<{repository.InterfaceQualifiedName}, {implementation}>();");
        }

        foreach (var unitOfWork in unitOfWorks.OrderBy(static u => u.InterfaceQualifiedName, StringComparer.Ordinal))
        {
            var implementation = GetQualifiedImplementationName(unitOfWork.Namespace, unitOfWork.ImplementationName);
            sb.AppendLine($"        services.TryAddScoped<{unitOfWork.InterfaceQualifiedName}, {implementation}>();");
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
            && char.IsLetter(interfaceName[1]))
        {
            interfaceName = interfaceName.Substring(1);
        }

        return interfaceName + "Generated";
    }

    private static string? BuildInsertSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || operationParameters.Count != 1)
        {
            return null;
        }

        var writeColumns = method.Entity.Properties.Where(static p => (!p.IsKey || !p.IsDbGenerated) && !p.IsRowVersion).ToList();
        var columnsSql = string.Join(", ", writeColumns.Select(p => Quote(dialect, p.ColumnName, caseSensitive)));
        var valuesSql = string.Join(", ", writeColumns.Select(p => "@" + p.PropertyName));
        var table = QualifiedTable(dialect, method.Entity, caseSensitive);

        if (method.ReturnsIdentity)
        {
            if (method.Entity.KeyProperty is null)
            {
                return null;
            }

            var keyColumn = Quote(dialect, method.Entity.KeyProperty.ColumnName, caseSensitive);
            return dialect switch
            {
                DatabaseDialect.PostgreSql or DatabaseDialect.Sqlite => $"INSERT INTO {table} ({columnsSql}) VALUES ({valuesSql}) RETURNING {keyColumn};",
                _ => $"INSERT INTO {table} ({columnsSql}) OUTPUT INSERTED.{keyColumn} VALUES ({valuesSql});",
            };
        }

        return $"INSERT INTO {table} ({columnsSql}) VALUES ({valuesSql});";
    }

    private static void RenderInsert(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || operationParameters.Count != 1 || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Insert signature is invalid.\");");
            return;
        }

        var entityParameter = operationParameters[0].Name;
        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");

        if (method.ReturnsIdentity && method.Entity.KeyProperty is not null)
        {
            sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<{method.Entity.KeyProperty.TypeName}>(Sql.{sqlConstName}, {entityParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
            sb.AppendLine("        return rows.FirstOrDefault();");
            return;
        }

        var executeExpression = $"await _connection.ExecuteGeneratedAsync(Sql.{sqlConstName}, {entityParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static string? BuildUpdateSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count != 1)
        {
            return null;
        }

        var writeColumns = method.Entity.Properties.Where(static p => !p.IsKey && !p.IsRowVersion).ToList();
        var setSql = string.Join(", ", writeColumns.Select(p => $"{Quote(dialect, p.ColumnName, caseSensitive)} = @{p.PropertyName}"));
        var whereSql = $"{Quote(dialect, method.Entity.KeyProperty.ColumnName, caseSensitive)} = @{method.Entity.KeyProperty.PropertyName}";
        if (method.Entity.RowVersionProperty is { } rowVersion)
        {
            whereSql += $" AND {Quote(dialect, rowVersion.ColumnName, caseSensitive)} = @{rowVersion.PropertyName}";
        }

        return $"UPDATE {QualifiedTable(dialect, method.Entity, caseSensitive)} SET {setSql} WHERE {whereSql};";
    }

    private static void RenderUpdate(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count != 1 || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Update signature is invalid.\");");
            return;
        }

        var entityParameter = operationParameters[0].Name;
        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var executeExpression = $"await _connection.ExecuteGeneratedAsync(Sql.{sqlConstName}, {entityParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static string BuildFilterWhereClause(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
        => string.Join(" AND ", method.Filters.Select(f =>
            f.IsIn
                ? $"{Quote(dialect, f.ColumnName, caseSensitive)} IN @{f.ParameterName}"
                : $"{Quote(dialect, f.ColumnName, caseSensitive)} = @{f.ParameterName}"));

    private static string BuildFilterParamExpression(RepositoryMethodModel method)
        => "new { " + string.Join(", ", method.Filters.Select(static f => f.ParameterName)) + " }";

    private static string? BuildDeleteSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (operationParameters.Count == 0 || method.Entity is null)
        {
            return null;
        }

        var table = QualifiedTable(dialect, method.Entity, caseSensitive);

        if (method.Filters.Count > 0)
        {
            return $"DELETE FROM {table} WHERE {BuildFilterWhereClause(method, dialect, caseSensitive)};";
        }

        if (method.Entity.KeyProperty is null)
        {
            return null;
        }

        var keyColumn = method.Entity.KeyProperty.ColumnName;
        var paramName = operationParameters[0].Name;
        var isEntityParameter = string.Equals(
            TrimNullableSuffix(operationParameters[0].TypeName),
            TrimNullableSuffix(method.Entity.ClrTypeName),
            StringComparison.Ordinal);
        var sqlParamName = isEntityParameter ? method.Entity.KeyProperty.PropertyName : paramName;
        var whereSql = $"{Quote(dialect, keyColumn, caseSensitive)} = @{sqlParamName}";
        if (isEntityParameter && method.Entity.RowVersionProperty is { } rowVersion)
        {
            whereSql += $" AND {Quote(dialect, rowVersion.ColumnName, caseSensitive)} = @{rowVersion.PropertyName}";
        }

        return $"DELETE FROM {table} WHERE {whereSql};";
    }

    private static void RenderDelete(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (operationParameters.Count == 0
            || method.Entity is null
            || (method.Filters.Count == 0 && method.Entity.KeyProperty is null)
            || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Delete signature is invalid.\");");
            return;
        }

        string paramExpression;
        if (method.Filters.Count > 0)
        {
            paramExpression = BuildFilterParamExpression(method);
        }
        else
        {
            var paramName = operationParameters[0].Name;
            var isEntityParameter = string.Equals(
                TrimNullableSuffix(operationParameters[0].TypeName),
                TrimNullableSuffix(method.Entity.ClrTypeName),
                StringComparison.Ordinal);
            paramExpression = isEntityParameter ? paramName : $"new {{ {paramName} }}";
        }

        sb.AppendLine($"        EnsureTransactionRequired(\"{method.Name}\");");
        sb.AppendLine("        var transaction = ResolveTransaction();");
        var executeExpression = $"await _connection.ExecuteGeneratedAsync(Sql.{sqlConstName}, {paramExpression}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false)";

        if (method.IsTaskWithoutResult)
        {
            sb.AppendLine($"        {executeExpression};");
            sb.AppendLine("        return;");
            return;
        }

        sb.AppendLine($"        return {executeExpression};");
    }

    private static string? BuildGetByIdSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count == 0)
        {
            return null;
        }

        var keyParameter = operationParameters[0].Name;
        var selectSql = BuildEntitySelect(method.Entity, dialect, caseSensitive);
        return $"{selectSql} WHERE {Quote(dialect, method.Entity.KeyProperty.ColumnName, caseSensitive)} = @{keyParameter};";
    }

    private static void RenderGetById(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null || method.Entity.KeyProperty is null || operationParameters.Count == 0 || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetById signature is invalid.\");");
            return;
        }

        var keyParameter = operationParameters[0].Name;
        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, new {{ {keyParameter} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
        sb.AppendLine("        return rows.FirstOrDefault();");
    }

    private static string? BuildGetAllSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
        => method.Entity is null ? null : BuildEntitySelect(method.Entity, dialect, caseSensitive) + ";";

    private static void RenderGetAll(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (method.Entity is null || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetAll signature is invalid.\");");
            return;
        }

        sb.AppendLine("        var transaction = ResolveTransaction();");

        if (method.ReturnsAsyncStream)
        {
            sb.AppendLine($"        return _connection.QueryStreamGenerated<{method.ElementTypeName}>(Sql.{sqlConstName}, transaction: transaction, cancellationToken: {method.CancellationTokenParameterName});");
            return;
        }

        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, transaction: transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static string? BuildGetPageSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null
            || !TryResolveGetPageParameters(operationParameters, out var skipParameter, out var takeParameter))
        {
            return null;
        }

        var orderByColumn = method.QueryMetadata.OrderByExpression
            ?? method.Entity.KeyProperty?.ColumnName
            ?? method.Entity.Properties.FirstOrDefault()?.ColumnName;
        if (orderByColumn is null)
        {
            return null;
        }

        var orderBySql = $"{BuildEntitySelect(method.Entity, dialect, caseSensitive)} ORDER BY {Quote(dialect, orderByColumn, caseSensitive)} {ToSql(method.QueryMetadata.OrderByDirection)}";
        var pageSql = dialect switch
        {
            DatabaseDialect.PostgreSql or DatabaseDialect.Sqlite => $"{orderBySql} LIMIT @{takeParameter} OFFSET @{skipParameter};",
            _ => $"{orderBySql} OFFSET @{skipParameter} ROWS FETCH NEXT @{takeParameter} ROWS ONLY;",
        };

        if (method.ReturnsPagedResult)
        {
            var table = QualifiedTable(dialect, method.Entity, caseSensitive);
            return $"{pageSql} SELECT {CountExpression(dialect)} FROM {table};";
        }

        return pageSql;
    }

    private static string CountExpression(DatabaseDialect dialect)
        => dialect == DatabaseDialect.SqlServer ? "COUNT_BIG(*)" : "COUNT(*)";

    private static void RenderGetPage(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        if (method.Entity is null
            || !TryResolveGetPageParameters(operationParameters, out var skipParameter, out var takeParameter)
            || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetPage signature is invalid.\");");
            return;
        }

        sb.AppendLine("        var transaction = ResolveTransaction();");

        if (method.ReturnsPagedResult)
        {
            sb.AppendLine($"        return await _connection.QueryPagedGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, new {{ {skipParameter}, {takeParameter} }}, {skipParameter}, {takeParameter}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
            return;
        }

        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, new {{ {skipParameter}, {takeParameter} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static string? BuildGetBySql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        if (method.Entity is null || method.Filters.Count == 0)
        {
            return null;
        }

        return $"{BuildEntitySelect(method.Entity, dialect, caseSensitive)} WHERE {BuildFilterWhereClause(method, dialect, caseSensitive)};";
    }

    private static void RenderGetBy(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (method.Entity is null || method.Filters.Count == 0 || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetBy signature is invalid.\");");
            return;
        }

        sb.AppendLine("        var transaction = ResolveTransaction();");

        if (method.ReturnsAsyncStream)
        {
            sb.AppendLine($"        return _connection.QueryStreamGenerated<{method.ElementTypeName}>(Sql.{sqlConstName}, {BuildFilterParamExpression(method)}, transaction, cancellationToken: {method.CancellationTokenParameterName});");
            return;
        }

        if (method.ReturnsEnumerable)
        {
            sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, {BuildFilterParamExpression(method)}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
            return;
        }

        sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, {BuildFilterParamExpression(method)}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
        sb.AppendLine("        return rows.FirstOrDefault();");
    }

    private static string? BuildCountSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        if (method.Entity is null)
        {
            return null;
        }

        var table = QualifiedTable(dialect, method.Entity, caseSensitive);
        var whereSql = method.Filters.Count == 0
            ? string.Empty
            : $" WHERE {BuildFilterWhereClause(method, dialect, caseSensitive)}";
        return $"SELECT {CountExpression(dialect)} FROM {table}{whereSql};";
    }

    private static void RenderCount(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (method.Entity is null || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Count signature is invalid.\");");
            return;
        }

        var paramExpression = method.Filters.Count == 0 ? "null" : BuildFilterParamExpression(method);
        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<long>(Sql.{sqlConstName}, {paramExpression}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");

        // A null element type means the method returns Task<int>; otherwise it returns Task<long>.
        sb.AppendLine(method.ElementTypeName is null
            ? "        return (int)rows.FirstOrDefault();"
            : "        return rows.FirstOrDefault();");
    }

    private static void RenderExists(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (method.Entity is null || sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Exists signature is invalid.\");");
            return;
        }

        var paramExpression = method.Filters.Count == 0 ? "null" : BuildFilterParamExpression(method);
        sb.AppendLine("        var transaction = ResolveTransaction();");
        sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<long>(Sql.{sqlConstName}, {paramExpression}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
        sb.AppendLine("        return rows.FirstOrDefault() > 0;");
    }

    private static string? BuildQuerySql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
    {
        var entity = method.Entity;
        var from = method.QueryMetadata.From;
        var baseAlias = method.QueryMetadata.BaseAlias;

        if (string.IsNullOrWhiteSpace(from))
        {
            from = entity is null ? QualifiedTable(dialect, "Unknown", caseSensitive) : QualifiedTable(dialect, entity, caseSensitive);
        }
        else if (!IsQuotedTableExpression(from!))
        {
            from = from!.IndexOf('.') >= 0
                ? string.Join(".", from.Split('.').Select(part => Quote(dialect, part.Trim(), caseSensitive)))
                : QualifiedTable(dialect, method.QueryMetadata.Schema, method.QueryMetadata.IsSchemaExplicit, from, caseSensitive);
        }

        var selectColumns = entity is null
            ? "*"
            : string.Join(", ", entity.Properties.Select(p => $"{baseAlias}.{Quote(dialect, p.ColumnName, caseSensitive)} AS {Quote(dialect, p.PropertyName, caseSensitive)}"));

        var joinSql = string.IsNullOrWhiteSpace(method.QueryMetadata.Join)
            ? BuildJoinClauses(method.QueryMetadata.Joins, dialect, caseSensitive)
            : " " + method.QueryMetadata.Join;

        var whereSql = string.IsNullOrWhiteSpace(method.QueryMetadata.WhereSql) ? string.Empty : $" WHERE {FinalizeQueryExpression(method.QueryMetadata.WhereSql!, dialect, caseSensitive)}";
        var orderBySql = method.QueryMetadata.OrderByExpression is null
            ? string.Empty
            : $" ORDER BY {FinalizeQueryExpression(method.QueryMetadata.OrderByExpression, dialect, caseSensitive)} {ToSql(method.QueryMetadata.OrderByDirection)}";
        return $"SELECT {selectColumns} FROM {from} {baseAlias}{joinSql}{whereSql}{orderBySql};";
    }

    private static void RenderQuery(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Query signature is invalid.\");");
            return;
        }

        var operationParameters = method.Parameters.Where(static p => !p.IsCancellationToken).ToList();
        var anonymousParam = operationParameters.Count == 0
            ? "null"
            : "new { " + string.Join(", ", operationParameters.Select(static p => p.Name)) + " }";

        sb.AppendLine("        var transaction = ResolveTransaction();");

        if (method.ReturnsAsyncStream)
        {
            sb.AppendLine($"        return _connection.QueryStreamGenerated<{method.ElementTypeName}>(Sql.{sqlConstName}, {anonymousParam}, transaction, cancellationToken: {method.CancellationTokenParameterName});");
            return;
        }

        sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(Sql.{sqlConstName}, {anonymousParam}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");
    }

    private static string? BuildStoredProcedureSql(RepositoryMethodModel method, DatabaseDialect dialect, bool caseSensitive)
        => ResolveStoredProcedureName(dialect, method.QueryMetadata.StoredProcedure, caseSensitive);

    private static void RenderStoredProcedure(RepositoryMethodModel method, StringBuilder sb, string? sqlConstName)
    {
        if (sqlConstName is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Stored procedure signature is invalid.\");");
            return;
        }

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

        sb.AppendLine($"        var result = await _connection.QueryStoredProcedureGeneratedAsync<{method.ElementTypeName ?? "dynamic"}>(Sql.{sqlConstName}, dynamicParameters, new[] {{ {string.Join(", ", outputNames)} }}, transaction, cancellationToken: {method.CancellationTokenParameterName}).ConfigureAwait(false);");

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

    private static string BuildJoinClauses(IReadOnlyList<QueryJoinModel> joins, DatabaseDialect dialect, bool caseSensitive)
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

            var onClause = $"{join.LeftAlias}.{Quote(dialect, join.LeftColumn, caseSensitive)} = {join.Alias}.{Quote(dialect, join.RightColumn, caseSensitive)}";
            if (!string.IsNullOrWhiteSpace(join.OnSql))
            {
                onClause += $" AND ({FinalizeQueryExpression(join.OnSql!, dialect, caseSensitive)})";
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
        // SQLite has no schemas; schema values (explicit or default) are ignored.
        if (dialect == DatabaseDialect.Sqlite)
        {
            return Quote(dialect, table, caseSensitive);
        }

        var resolvedSchema = isSchemaExplicit ? schema : ResolveDefaultSchema(dialect, schema);
        if (string.IsNullOrWhiteSpace(resolvedSchema))
        {
            return Quote(dialect, table, caseSensitive);
        }

        return $"{Quote(dialect, resolvedSchema!, caseSensitive)}.{Quote(dialect, table, caseSensitive)}";
    }

    private static bool IsQuotedTableExpression(string value)
        => value.IndexOf("[", StringComparison.Ordinal) >= 0
            || value.IndexOf("]", StringComparison.Ordinal) >= 0
            || value.IndexOf("\"", StringComparison.Ordinal) >= 0
            || value.IndexOf("`", StringComparison.Ordinal) >= 0;

    private static string Quote(DatabaseDialect dialect, string identifier, bool caseSensitive)
        => dialect switch
        {
            DatabaseDialect.PostgreSql or DatabaseDialect.Sqlite => caseSensitive
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

        if (dialect == DatabaseDialect.Sqlite)
        {
            return Quote(dialect, name!, caseSensitive);
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

    private static QueryNodeModel? RegisterOrResolveJoinSource(
        IDictionary<string, QueryNodeModel> nodes,
        string alias,
        EntityModel? entity,
        string? schema,
        bool isSchemaExplicit,
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        ref string? baseAlias)
    {
        if (string.IsNullOrWhiteSpace(baseAlias))
        {
            baseAlias = alias;
            return RegisterJoinNode(nodes, alias, entity, schema, isSchemaExplicit, method, diagnostics, allowExisting: true);
        }

        if (!nodes.TryGetValue(alias, out var node))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.JoinSourceInvalid,
                method.Locations.FirstOrDefault(),
                method.Name,
                alias));
            return RegisterJoinNode(nodes, alias, entity, schema, isSchemaExplicit, method, diagnostics, allowExisting: true);
        }

        return node;
    }

    private static QueryNodeModel? RegisterJoinNode(
        IDictionary<string, QueryNodeModel> nodes,
        string alias,
        EntityModel? entity,
        string? schema,
        bool isSchemaExplicit,
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        bool allowExisting = false)
    {
        var tableName = entity?.TableName ?? "Unknown";
        var node = new QueryNodeModel(alias, entity, tableName, schema, isSchemaExplicit);

        if (nodes.TryGetValue(alias, out var existing))
        {
            if (allowExisting)
            {
                return existing;
            }

            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.JoinAliasConflict,
                method.Locations.FirstOrDefault(),
                method.Name,
                alias));
            return existing;
        }

        nodes[alias] = node;
        return node;
    }

    private static string ResolveQueryAlias(string? explicitAlias, string? tableName, string? clrTypeName, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitAlias))
        {
            return explicitAlias!;
        }

        var source = !string.IsNullOrWhiteSpace(tableName)
            ? tableName
            : !string.IsNullOrWhiteSpace(clrTypeName)
                ? clrTypeName
                : fallback;

        return BuildDefaultAlias(source);
    }

    private static string BuildDefaultAlias(string? source)
    {
        var candidate = ExtractAliasSource(source);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "querySource";
        }

        var parts = Regex.Matches(candidate, "[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")
            .Cast<Match>()
            .Select(static match => match.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (parts.Count == 0)
        {
            parts = candidate
                .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        if (parts.Count == 0)
        {
            parts.Add(candidate);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var normalized = Regex.Replace(part, "[^A-Za-z0-9_]", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (i == 0)
            {
                sb.Append(char.ToLowerInvariant(normalized[0]));
                if (normalized.Length > 1)
                {
                    sb.Append(normalized.Substring(1));
                }
            }
            else
            {
                sb.Append(char.ToUpperInvariant(normalized[0]));
                if (normalized.Length > 1)
                {
                    sb.Append(normalized.Substring(1));
                }
            }
        }

        if (sb.Length == 0)
        {
            return "querySource";
        }

        if (!char.IsLetter(sb[0]) && sb[0] != '_')
        {
            sb.Insert(0, 't');
        }

        return sb.ToString();
    }

    private static string ExtractAliasSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var candidate = source!.Trim();
        if (candidate.Contains('.'))
        {
            candidate = candidate.Split('.').Last();
        }

        candidate = candidate.Trim('[', ']', '"', '`');
        return candidate;
    }

    private static string? RewriteQueryExpression(
        string? expression,
        IReadOnlyDictionary<string, QueryNodeModel> nodes,
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        DatabaseDialect? dialect,
        bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        return RewriteOutsideLiterals(expression!, segment => RewriteQueryExpressionSegment(segment, nodes, method, diagnostics, dialect, caseSensitive));
    }

    /// <summary>
    /// Applies <paramref name="rewrite"/> only to the portions of <paramref name="expression"/> that are
    /// outside SQL string literals ('...'), bracketed identifiers ([...]), and quoted identifiers ("..." or `...`).
    /// </summary>
    private static string RewriteOutsideLiterals(string expression, Func<string, string> rewrite)
    {
        var sb = new StringBuilder();
        var segmentStart = 0;
        var i = 0;

        while (i < expression.Length)
        {
            var c = expression[i];
            if (c is '\'' or '[' or '"' or '`')
            {
                sb.Append(rewrite(expression.Substring(segmentStart, i - segmentStart)));

                var close = c switch
                {
                    '[' => ']',
                    _ => c,
                };

                var j = i + 1;
                while (j < expression.Length)
                {
                    if (expression[j] == close)
                    {
                        // '' inside a string literal is an escaped quote, not a terminator.
                        if (close == '\'' && j + 1 < expression.Length && expression[j + 1] == '\'')
                        {
                            j += 2;
                            continue;
                        }

                        break;
                    }

                    j++;
                }

                var end = j < expression.Length ? j + 1 : expression.Length;
                sb.Append(expression, i, end - i);
                i = end;
                segmentStart = i;
                continue;
            }

            i++;
        }

        sb.Append(rewrite(expression.Substring(segmentStart)));
        return sb.ToString();
    }

    private static string RewriteQueryExpressionSegment(
        string expression,
        IReadOnlyDictionary<string, QueryNodeModel> nodes,
        IMethodSymbol method,
        List<Diagnostic> diagnostics,
        DatabaseDialect? dialect,
        bool caseSensitive)
    {
        if (expression.Length == 0)
        {
            return expression;
        }

        var rewritten = Regex.Replace(
            expression,
            "(?<![@.\\[\"`])(?<alias>[A-Za-z_][A-Za-z0-9_]*)\\.(?<property>[A-Za-z_][A-Za-z0-9_]*)",
            match =>
            {
                var alias = match.Groups["alias"].Value;
                var property = match.Groups["property"].Value;
                if (!nodes.TryGetValue(alias, out var node) || node.Entity is null)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.QueryReferenceInvalid,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        match.Value));
                    return match.Value;
                }

                if (!TryResolveColumn(node.Entity, property, out var columnName))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.QueryReferenceInvalid,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        match.Value));
                    return match.Value;
                }

                return $"{alias}.{FormatQueryColumn(columnName, dialect, caseSensitive)}";
            });

        rewritten = Regex.Replace(
            rewritten,
            "(?<![@.\\[\"`])\\b(?<token>[A-Za-z_][A-Za-z0-9_]*)\\b",
            match =>
            {
                var token = match.Groups["token"].Value;
                if (nodes.ContainsKey(token) || IsSqlKeyword(token))
                {
                    return token;
                }

                var matches = nodes.Values
                    .Where(static n => n.Entity is not null)
                    .Select(node => new
                    {
                        node.Alias,
                        Entity = node.Entity!,
                    })
                    .Where(x => TryResolveColumn(x.Entity, token, out _))
                    .ToList();

                if (matches.Count == 0)
                {
                    return token;
                }

                if (matches.Count > 1)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticDescriptors.QueryReferenceAmbiguous,
                        method.Locations.FirstOrDefault(),
                        method.Name,
                        token));
                    return token;
                }

                _ = TryResolveColumn(matches[0].Entity, token, out var columnName);
                return $"{matches[0].Alias}.{FormatQueryColumn(columnName, dialect, caseSensitive)}";
            });

        return rewritten;
    }

    private static string FinalizeQueryExpression(string expression, DatabaseDialect dialect, bool caseSensitive)
        => RewriteOutsideLiterals(expression, segment => Regex.Replace(
            segment,
            "(?<![@.\\[\"`])(?<alias>[A-Za-z_][A-Za-z0-9_]*)\\.(?<column>[A-Za-z_][A-Za-z0-9_]*)",
            match => $"{match.Groups["alias"].Value}.{Quote(dialect, match.Groups["column"].Value, caseSensitive)}"));

    private static string FormatQueryColumn(string columnName, DatabaseDialect? dialect, bool caseSensitive)
        => dialect is null ? columnName : Quote(dialect.Value, columnName, caseSensitive);

    private static bool IsSqlKeyword(string token)
        => token.ToUpperInvariant() is
            "AND" or
            "OR" or
            "NOT" or
            "IS" or
            "NULL" or
            "LIKE" or
            "IN" or
            "EXISTS" or
            "BETWEEN" or
            "ASC" or
            "DESC" or
            "ON" or
            "CASE" or
            "WHEN" or
            "THEN" or
            "ELSE" or
            "END" or
            "AS" or
            "SELECT" or
            "FROM" or
            "WHERE" or
            "GROUP" or
            "BY" or
            "HAVING" or
            "ORDER" or
            "DISTINCT" or
            "TOP" or
            "JOIN" or
            "INNER" or
            "LEFT" or
            "RIGHT" or
            "FULL" or
            "OUTER" or
            "CROSS" or
            "UNION" or
            "ALL" or
            "ANY" or
            "SOME" or
            "LIMIT" or
            "OFFSET" or
            "FETCH" or
            "NEXT" or
            "ROWS" or
            "ONLY" or
            "COUNT" or
            "SUM" or
            "MIN" or
            "MAX" or
            "AVG" or
            "COALESCE" or
            "NULLIF" or
            "CAST" or
            "CONVERT" or
            "TRUE" or
            "FALSE";

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

    private static bool TryGetEnumerableElementType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.Name is "IEnumerable" or "IReadOnlyList" or "IReadOnlyCollection" or "IList" or "ICollection" or "List"
            && named.ContainingNamespace is { IsGlobalNamespace: false } ns
            && ns.ToDisplayString() == "System.Collections.Generic")
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        return false;
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

            if (string.Equals(value, "Sqlite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "SQLite3", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseDialect.Sqlite;
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
        bool ReturnsPagedResult,
        bool ReturnsAsyncStream,
        bool IsTaskWithoutResult,
        string? ElementTypeName,
        RepositoryOperationKind OperationKind,
        IReadOnlyList<MethodParameterModel> Parameters,
        string CancellationTokenParameterName,
        EntityModel? Entity,
        QueryMetadata QueryMetadata,
        IReadOnlyList<ConventionFilterModel> Filters,
        bool ReturnsIdentity);

    private sealed record ConventionFilterModel(
        string ColumnName,
        string ParameterName,
        bool IsIn = false);

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
        EntityPropertyModel? KeyProperty,
        EntityPropertyModel? RowVersionProperty);

    private sealed record EntityPropertyModel(
        string PropertyName,
        string ColumnName,
        string TypeName,
        bool IsKey,
        bool IsDbGenerated,
        bool IsRowVersion);

    private sealed record QueryMetadata(
        string? From,
        string? Schema,
        bool IsSchemaExplicit,
        string BaseAlias,
        string? WhereSql,
        string? OrderByExpression,
        OrderByDirectionModel OrderByDirection,
        string? Join,
        IReadOnlyList<QueryJoinModel> Joins)
    {
        public StoredProcedureMetadata? StoredProcedure { get; init; }
    }

    private sealed record QueryNodeModel(
        string Alias,
        EntityModel? Entity,
        string TableName,
        string? TableSchema,
        bool IsSchemaExplicit);

    private sealed record QueryJoinModel(
        string JoinType,
        string TableName,
        string? TableSchema,
        bool IsSchemaExplicit,
        string LeftAlias,
        string Alias,
        string LeftColumn,
        string RightColumn,
        string? OnSql);

    private sealed record StoredProcedureMetadata(
        string Name,
        string? Schema,
        bool IsSchemaExplicit);

    private sealed record SqlConstantEntry(
        int MethodIndex,
        string MethodName,
        string ConstName,
        string Sql);

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
        GetBy,
        Count,
        Exists,
        Query,
        StoredProcedure,
    }

    private enum DatabaseDialect
    {
        SqlServer,
        PostgreSql,
        Sqlite,
    }

    private sealed record MethodShape(
        bool IsSupported,
        bool IsAsync,
        bool IsTaskWithoutResult,
        bool ReturnsEnumerable,
        bool IsProcedureResult,
        ITypeSymbol? ElementType,
        bool IsPagedResult = false,
        bool IsAsyncStream = false)
    {
        public static MethodShape FromReturnType(ITypeSymbol returnType)
        {
            if (returnType is INamedTypeSymbol asyncStream
                && asyncStream.IsGenericType
                && asyncStream.Name == "IAsyncEnumerable"
                && IsInNamespace(asyncStream, "System.Collections.Generic"))
            {
                return new MethodShape(true, true, false, true, false, asyncStream.TypeArguments[0], IsAsyncStream: true);
            }

            if (returnType is INamedTypeSymbol named
                && named.Name is "Task" or "ValueTask"
                && IsInNamespace(named, "System.Threading.Tasks"))
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
                && generic.Name is "IEnumerable" or "IReadOnlyList" or "List"
                && IsInNamespace(generic, "System.Collections.Generic"))
            {
                return new MethodShape(true, false, false, true, false, generic.TypeArguments[0]);
            }

            if (returnType is INamedTypeSymbol procedureResult
                && procedureResult.IsGenericType
                && procedureResult.Name == "GeneratedProcedureResult"
                && IsInNamespace(procedureResult, "DreamBig.SourceGen.Dapper.Internal"))
            {
                return new MethodShape(true, false, false, false, true, procedureResult.TypeArguments[0]);
            }

            if (returnType is INamedTypeSymbol pagedResult
                && pagedResult.IsGenericType
                && pagedResult.Name == "PagedResult"
                && IsInNamespace(pagedResult, "DreamBig.SourceGen.Dapper.Internal"))
            {
                return new MethodShape(true, false, false, false, false, pagedResult.TypeArguments[0], IsPagedResult: true);
            }

            if (returnType.SpecialType is SpecialType.System_Int32)
            {
                return new MethodShape(true, false, false, false, false, null);
            }

            return returnType is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct }
                ? new MethodShape(true, false, false, false, false, returnType)
                : new MethodShape(false, false, false, false, false, null);
        }

        private static bool IsInNamespace(INamedTypeSymbol symbol, string expectedNamespace)
            => symbol.ContainingNamespace is { } ns
                && !ns.IsGlobalNamespace
                && ns.ToDisplayString() == expectedNamespace;
    }
}
