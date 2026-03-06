using System.Globalization;
using System.Text;
using DreamBig.SourceGen.Dapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DreamBig.SourceGen.Dapper.Generator.Generation;

/// <summary>
/// Generates Dapper repository implementations from DreamBig repository attributes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RepositorySourceGenerator : IIncrementalGenerator
{
    private const string DbRepositoryAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbRepositoryAttribute";
    private const string DbTableAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbTableAttribute";
    private const string DbColumnAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbColumnAttribute";
    private const string DbKeyAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbKeyAttribute";
    private const string DbIgnoreAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbIgnoreAttribute";
    private const string DbQueryAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbQueryAttribute";
    private const string DbJoinAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbJoinAttribute";
    private const string DbStoredProcedureAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbStoredProcedureAttribute";
    private const string DbParamAttribute = "DreamBig.SourceGen.Dapper.Attributes.DbParamAttribute";

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

        var compilationAndInterfaces = context.CompilationProvider.Combine(interfaces.Collect());

        context.RegisterSourceOutput(compilationAndInterfaces, static (spc, payload) =>
        {
            var (compilation, candidates) = payload;
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
                    continue;
                }

                var source = RenderRepository(repository);
                spc.AddSource($"{repository.ImplementationName}.g.cs", SourceText.From(source, Encoding.UTF8));
            }
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

        return new RepositoryModel(
            Namespace: ns,
            InterfaceName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            InterfaceQualifiedName: interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ImplementationName: interfaceSymbol.Name + "Generated",
            Methods: methods);
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
            TypeName: p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ParameterName: ResolveDbParamName(p),
            DbParamAttribute: ReadDbParamAttribute(p))).ToList();

        var methodShape = MethodShape.FromReturnType(method.ReturnType);
        if (!methodShape.IsSupported)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedSignature,
                method.Locations.FirstOrDefault(),
                method.Name));
            return null;
        }

        EntityModel? entity = null;

        switch (operationKind)
        {
            case RepositoryOperationKind.Insert:
            case RepositoryOperationKind.Update:
                if (method.Parameters.Length == 1 && method.Parameters[0].Type is INamedTypeSymbol entityType)
                {
                    entity = BuildEntityModel(entityType, method, diagnostics);
                }

                break;
            case RepositoryOperationKind.GetById:
            case RepositoryOperationKind.GetAll:
            case RepositoryOperationKind.GetPage:
            case RepositoryOperationKind.Query:
                var entityCandidate = methodShape.ElementType;
                if (entityCandidate is not null)
                {
                    entity = BuildEntityModel(entityCandidate, method, diagnostics);
                }

                break;
            case RepositoryOperationKind.StoredProcedure:
                var spEntity = methodShape.ElementType;
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

        var queryMetadata = ReadQueryMetadata(method, diagnostics);
        if (operationKind == RepositoryOperationKind.StoredProcedure)
        {
            var spAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbStoredProcedureAttribute));
            var spName = spAttribute?.ConstructorArguments.Length > 0
                ? spAttribute.ConstructorArguments[0].Value as string
                : null;
            var spSchema = ReadNamedAttributeString(spAttribute, "Schema") ?? "dbo";
            queryMetadata = queryMetadata with { StoredProcedure = $"{Quote(spSchema)}.{Quote(spName ?? string.Empty)}" };
        }

        return new RepositoryMethodModel(
            Name: method.Name,
            ReturnTypeName: method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsAsync: methodShape.IsAsync,
            ReturnsEnumerable: methodShape.ReturnsEnumerable,
            ReturnsProcedureResult: methodShape.IsProcedureResult,
            ElementTypeName: methodShape.ElementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            OperationKind: operationKind,
            Parameters: parameters,
            Entity: entity,
            QueryMetadata: queryMetadata);
    }

    private static QueryMetadata ReadQueryMetadata(IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var queryAttribute = method.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbQueryAttribute));
        var from = ReadNamedAttributeString(queryAttribute, "From");
        var where = ReadNamedAttributeString(queryAttribute, "Where");
        var orderBy = ReadNamedAttributeString(queryAttribute, "OrderBy");
        var joinOverride = ReadNamedAttributeString(queryAttribute, "Join");

        var joins = method.GetAttributes()
            .Where(a => IsAttribute(a.AttributeClass, DbJoinAttribute))
            .Select(static a =>
            {
                var joinType = a.ConstructorArguments[0].Value switch
                {
                    1 => "Left",
                    2 => "Right",
                    3 => "Full",
                    _ => "Inner",
                };
                var table = a.ConstructorArguments[1].Value?.ToString() ?? string.Empty;
                var left = a.ConstructorArguments[2].Value?.ToString() ?? string.Empty;
                var right = a.ConstructorArguments[3].Value?.ToString() ?? string.Empty;
                var alias = ReadNamedAttributeString(a, "Alias");
                return new QueryJoinModel(joinType, table, left, right, alias);
            })
            .ToList();

        foreach (var join in joins)
        {
            if (string.IsNullOrWhiteSpace(join.Left)
                || string.IsNullOrWhiteSpace(join.Right)
                || join.Left.IndexOf('.') < 0
                || join.Right.IndexOf('.') < 0)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.JoinDefinitionInvalid,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    $"{join.Left} = {join.Right}"));
            }
        }

        return new QueryMetadata(from, where, orderBy, joinOverride, joins);
    }

    private static EntityModel? BuildEntityModel(INamedTypeSymbol type, IMethodSymbol method, List<Diagnostic> diagnostics)
    {
        var tableAttribute = type.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbTableAttribute));
        var tableName = tableAttribute?.ConstructorArguments.Length > 0
            ? tableAttribute.ConstructorArguments[0].Value?.ToString()
            : null;

        var schema = ReadNamedAttributeString(tableAttribute, "Schema") ?? "dbo";
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
                TypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
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
            ClrTypeName: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Schema: schema,
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

    private static string RenderRepository(RepositoryModel model)
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
        sb.AppendLine();
        sb.AppendLine($"    public {model.ImplementationName}(IDbConnection connection, IDbTransaction? transaction = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        _connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine("        _transaction = transaction;");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var method in model.Methods)
        {
            sb.Append(RenderMethod(method));
            sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string RenderMethod(RepositoryMethodModel method)
    {
        var sb = new StringBuilder();
        var parameterList = string.Join(", ", method.Parameters.Select(static p => $"{p.TypeName} {p.Name}"));

        if (!string.IsNullOrEmpty(parameterList))
        {
            parameterList += ", ";
        }

        parameterList += "CancellationToken cancellationToken = default";

        sb.AppendLine($"    public {method.ReturnTypeName} {method.Name}({parameterList})");
        sb.AppendLine("    {");

        if (!method.IsAsync)
        {
            sb.AppendLine("        _ = cancellationToken;");
        }

        switch (method.OperationKind)
        {
            case RepositoryOperationKind.Insert:
                RenderInsert(method, sb);
                break;
            case RepositoryOperationKind.Update:
                RenderUpdate(method, sb);
                break;
            case RepositoryOperationKind.Delete:
                RenderDelete(method, sb);
                break;
            case RepositoryOperationKind.GetById:
                RenderGetById(method, sb);
                break;
            case RepositoryOperationKind.GetAll:
                RenderGetAll(method, sb);
                break;
            case RepositoryOperationKind.GetPage:
                RenderGetPage(method, sb);
                break;
            case RepositoryOperationKind.Query:
                RenderQuery(method, sb);
                break;
            case RepositoryOperationKind.StoredProcedure:
                RenderStoredProcedure(method, sb);
                break;
            default:
                sb.AppendLine("        throw new NotSupportedException(\"Unsupported generated method.\");");
                break;
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void RenderInsert(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Entity is null || method.Parameters.Count != 1)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Insert signature is invalid.\");");
            return;
        }

        var entityParameter = method.Parameters[0].Name;
        var writeColumns = method.Entity.Properties.Where(static p => !p.IsKey).ToList();
        var columnsSql = string.Join(", ", writeColumns.Select(p => Quote(p.ColumnName)));
        var valuesSql = string.Join(", ", writeColumns.Select(p => "@" + p.PropertyName));
        var sql = $"INSERT INTO {QualifiedTable(method.Entity)} ({columnsSql}) VALUES ({valuesSql});";
        var returnExpression = method.IsAsync
            ? $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", {entityParameter}, _transaction).ConfigureAwait(false)"
            : $"_connection.ExecuteGenerated(\"{EscapeSql(sql)}\", {entityParameter}, _transaction)";

        sb.AppendLine($"        return {returnExpression};");
    }

    private static void RenderUpdate(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Entity is null || method.Entity.KeyProperty is null || method.Parameters.Count != 1)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Update signature is invalid.\");");
            return;
        }

        var entityParameter = method.Parameters[0].Name;
        var writeColumns = method.Entity.Properties.Where(static p => !p.IsKey).ToList();
        var setSql = string.Join(", ", writeColumns.Select(p => $"{Quote(p.ColumnName)} = @{p.PropertyName}"));
        var sql = $"UPDATE {QualifiedTable(method.Entity)} SET {setSql} WHERE {Quote(method.Entity.KeyProperty.ColumnName)} = @{method.Entity.KeyProperty.PropertyName};";
        var returnExpression = method.IsAsync
            ? $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", {entityParameter}, _transaction).ConfigureAwait(false)"
            : $"_connection.ExecuteGenerated(\"{EscapeSql(sql)}\", {entityParameter}, _transaction)";

        sb.AppendLine($"        return {returnExpression};");
    }

    private static void RenderDelete(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Parameters.Count == 0)
        {
            sb.AppendLine("        throw new NotSupportedException(\"Delete signature is invalid.\");");
            return;
        }

        var table = method.Entity is null ? "[dbo].[Unknown]" : QualifiedTable(method.Entity);
        var keyColumn = method.Entity?.KeyProperty?.ColumnName ?? method.Parameters[0].Name;
        var paramName = method.Parameters[0].Name;
        var sql = $"DELETE FROM {table} WHERE {Quote(keyColumn)} = @{paramName};";
        var returnExpression = method.IsAsync
            ? $"await _connection.ExecuteGeneratedAsync(\"{EscapeSql(sql)}\", new {{ {paramName} }}, _transaction).ConfigureAwait(false)"
            : $"_connection.ExecuteGenerated(\"{EscapeSql(sql)}\", new {{ {paramName} }}, _transaction)";

        sb.AppendLine($"        return {returnExpression};");
    }

    private static void RenderGetById(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Entity is null || method.Entity.KeyProperty is null || method.Parameters.Count == 0)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetById signature is invalid.\");");
            return;
        }

        var keyParameter = method.Parameters[0].Name;
        var selectSql = BuildEntitySelect(method.Entity);
        var sql = $"{selectSql} WHERE {Quote(method.Entity.KeyProperty.ColumnName)} = @{keyParameter};";

        if (method.IsAsync)
        {
            sb.AppendLine($"        var rows = await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ {keyParameter} }}, _transaction, cancellationToken: cancellationToken).ConfigureAwait(false);");
            sb.AppendLine("        return rows.FirstOrDefault();");
            return;
        }

        sb.AppendLine($"        return _connection.QueryGenerated<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ {keyParameter} }}, _transaction).FirstOrDefault();");
    }

    private static void RenderGetAll(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Entity is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetAll signature is invalid.\");");
            return;
        }

        var sql = BuildEntitySelect(method.Entity) + ";";

        if (method.IsAsync)
        {
            sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", transaction: _transaction, cancellationToken: cancellationToken).ConfigureAwait(false);");
            return;
        }

        sb.AppendLine($"        return _connection.QueryGenerated<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", transaction: _transaction);");
    }

    private static void RenderGetPage(RepositoryMethodModel method, StringBuilder sb)
    {
        if (method.Entity is null)
        {
            sb.AppendLine("        throw new NotSupportedException(\"GetPage signature is invalid.\");");
            return;
        }

        var orderBy = method.Entity.KeyProperty?.ColumnName ?? method.Entity.Properties.FirstOrDefault()?.ColumnName ?? "Id";
        var sql = $"{BuildEntitySelect(method.Entity)} ORDER BY {Quote(orderBy)} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";

        if (method.IsAsync)
        {
            sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ skip, take }}, _transaction, cancellationToken: cancellationToken).ConfigureAwait(false);");
            return;
        }

        sb.AppendLine($"        return _connection.QueryGenerated<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", new {{ skip, take }}, _transaction);");
    }

    private static void RenderQuery(RepositoryMethodModel method, StringBuilder sb)
    {
        var entity = method.Entity;
        var from = method.QueryMetadata.From;

        if (string.IsNullOrWhiteSpace(from))
        {
            from = entity is null ? "[dbo].[Unknown]" : QualifiedTable(entity);
        }

        var selectColumns = entity is null
            ? "*"
            : string.Join(", ", entity.Properties.Select(p => $"{Quote(p.ColumnName)} AS [{p.PropertyName}]"));

        var joinSql = string.IsNullOrWhiteSpace(method.QueryMetadata.Join)
            ? BuildJoinClauses(method.QueryMetadata.Joins)
            : " " + method.QueryMetadata.Join;

        var whereSql = string.IsNullOrWhiteSpace(method.QueryMetadata.Where) ? string.Empty : $" WHERE {method.QueryMetadata.Where}";
        var orderBySql = string.IsNullOrWhiteSpace(method.QueryMetadata.OrderBy) ? string.Empty : $" ORDER BY {method.QueryMetadata.OrderBy}";
        var sql = $"SELECT {selectColumns} FROM {from}{joinSql}{whereSql}{orderBySql};";

        var anonymousParam = method.Parameters.Count == 0
            ? "null"
            : "new { " + string.Join(", ", method.Parameters.Select(static p => p.Name)) + " }";

        if (method.IsAsync)
        {
            sb.AppendLine($"        return await _connection.QueryGeneratedAsync<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", {anonymousParam}, _transaction, cancellationToken: cancellationToken).ConfigureAwait(false);");
            return;
        }

        sb.AppendLine($"        return _connection.QueryGenerated<{method.ElementTypeName}>(\"{EscapeSql(sql)}\", {anonymousParam}, _transaction);");
    }

    private static void RenderStoredProcedure(RepositoryMethodModel method, StringBuilder sb)
    {
        var spAttribute = method.QueryMetadata.StoredProcedure;
        var procedureName = spAttribute ?? "[dbo].[UnknownProcedure]";

        sb.AppendLine("        var dynamicParameters = new DynamicParameters();");
        foreach (var parameter in method.Parameters)
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
            .Where(static p => p.DbParamAttribute is { Direction: DbParamDirectionModel.Output or DbParamDirectionModel.InputOutput })
            .Select(static p => $"\"{p.ParameterName}\"")
            .ToList();

        sb.AppendLine($"        var result = _connection.QueryStoredProcedureGenerated<{method.ElementTypeName ?? "dynamic"}>(\"{EscapeSql(procedureName)}\", dynamicParameters, new[] {{ {string.Join(", ", outputNames)} }}, _transaction);");

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

    private static string BuildEntitySelect(EntityModel entity)
    {
        var selectColumns = string.Join(", ", entity.Properties.Select(p => $"{Quote(p.ColumnName)} AS [{p.PropertyName}]"));
        return $"SELECT {selectColumns} FROM {QualifiedTable(entity)}";
    }

    private static string BuildJoinClauses(IReadOnlyList<QueryJoinModel> joins)
    {
        if (joins.Count == 0)
        {
            return string.Empty;
        }

        var clauses = joins.Select(static join =>
        {
            var keyword = join.JoinType switch
            {
                "Left" => "LEFT OUTER JOIN",
                "Right" => "RIGHT OUTER JOIN",
                "Full" => "FULL OUTER JOIN",
                _ => "INNER JOIN",
            };

            return $" {keyword} {join.Table} ON {join.Left} = {join.Right}";
        });

        return string.Concat(clauses);
    }

    private static string QualifiedTable(EntityModel entity)
        => $"{Quote(entity.Schema)}.{Quote(entity.TableName)}";

    private static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]")}]";

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
        if (attributeData is null)
        {
            return null;
        }

        foreach (var argument in attributeData.NamedArguments)
        {
            if (argument.Key == argumentName)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    private static string ResolveDbParamName(IParameterSymbol parameter)
    {
        var attribute = parameter.GetAttributes().FirstOrDefault(a => IsAttribute(a.AttributeClass, DbParamAttribute));
        var configured = attribute?.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Value as string
            : null;

        return string.IsNullOrWhiteSpace(configured) ? "@" + parameter.Name : configured!;
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

    private sealed record RepositoryModel(
        string? Namespace,
        string InterfaceName,
        string InterfaceQualifiedName,
        string ImplementationName,
        IReadOnlyList<RepositoryMethodModel> Methods);

    private sealed record RepositoryMethodModel(
        string Name,
        string ReturnTypeName,
        bool IsAsync,
        bool ReturnsEnumerable,
        bool ReturnsProcedureResult,
        string? ElementTypeName,
        RepositoryOperationKind OperationKind,
        IReadOnlyList<MethodParameterModel> Parameters,
        EntityModel? Entity,
        QueryMetadata QueryMetadata);

    private sealed record MethodParameterModel(
        string Name,
        string TypeName,
        string ParameterName,
        DbParamAttributeModel? DbParamAttribute);

    private sealed record EntityModel(
        string ClrTypeName,
        string Schema,
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
        string? Where,
        string? OrderBy,
        string? Join,
        IReadOnlyList<QueryJoinModel> Joins)
    {
        public string? StoredProcedure { get; init; }
    }

    private sealed record QueryJoinModel(string JoinType, string Table, string Left, string Right, string? Alias);

    private sealed record DbParamAttributeModel(DbParamDirectionModel Direction, System.Data.DbType? DbType, int? Size);

    private enum DbParamDirectionModel
    {
        Input,
        Output,
        InputOutput,
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

    private sealed record MethodShape(
        bool IsSupported,
        bool IsAsync,
        bool ReturnsEnumerable,
        bool IsProcedureResult,
        INamedTypeSymbol? ElementType)
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

                return new MethodShape(true, true, false, false, null);
            }

            if (returnType is INamedTypeSymbol generic
                && generic.IsGenericType
                && generic.Name is "IEnumerable" or "IReadOnlyList" or "List")
            {
                return new MethodShape(true, false, true, false, generic.TypeArguments[0] as INamedTypeSymbol);
            }

            if (returnType is INamedTypeSymbol procedureResult
                && procedureResult.IsGenericType
                && procedureResult.Name == "GeneratedProcedureResult")
            {
                return new MethodShape(true, false, false, true, procedureResult.TypeArguments[0] as INamedTypeSymbol);
            }

            if (returnType.SpecialType is SpecialType.System_Int32)
            {
                return new MethodShape(true, false, false, false, null);
            }

            return returnType as INamedTypeSymbol is { TypeKind: TypeKind.Class or TypeKind.Struct }
                ? new MethodShape(true, false, false, false, returnType as INamedTypeSymbol)
                : new MethodShape(false, false, false, false, null);
        }
    }
}
