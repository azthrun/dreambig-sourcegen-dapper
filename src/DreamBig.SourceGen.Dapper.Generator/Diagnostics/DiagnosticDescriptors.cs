using Microsoft.CodeAnalysis;

namespace DreamBig.SourceGen.Dapper.Generator.Diagnostics;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingKey = new(
        id: "DBSGD001",
        title: "Entity key is missing",
        messageFormat: "Entity '{0}' must declare a [DbKey] property for generated '{1}' operations.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedSignature = new(
        id: "DBSGD002",
        title: "Unsupported repository method signature",
        messageFormat: "Repository method '{0}' is not recognized. Use Insert*, Update*, Delete*, GetById*, GetAll*, GetPage*, [DbQuery], or [DbStoredProcedure].",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingColumnMapping = new(
        id: "DBSGD003",
        title: "Conflicting column mapping",
        messageFormat: "Entity '{0}' maps multiple properties to SQL column '{1}'.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoredProcedureNameMissing = new(
        id: "DBSGD004",
        title: "Stored procedure name is missing",
        messageFormat: "Stored procedure method '{0}' has an empty [DbStoredProcedure] name.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinDefinitionInvalid = new(
        id: "DBSGD005",
        title: "Join definition is invalid",
        messageFormat: "Method '{0}' contains an invalid join expression '{1}'.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinPropertyMissing = new(
        id: "DBSGD013",
        title: "Join property is missing",
        messageFormat: "Method '{0}' contains a join missing required property '{1}'.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinEntityMissing = new(
        id: "DBSGD014",
        title: "Join entity is missing",
        messageFormat: "Method '{0}' cannot resolve the {1}-side join entity for validation.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinColumnInvalid = new(
        id: "DBSGD015",
        title: "Join column is invalid",
        messageFormat: "Method '{0}' contains join column '{1}' that is not a property on '{2}' ({3} side).",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrderByColumnInvalid = new(
        id: "DBSGD016",
        title: "OrderBy column is invalid",
        messageFormat: "Method '{0}' contains OrderBy column '{1}' that is not a property on '{2}'.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryReferenceInvalid = new(
        id: "DBSGD017",
        title: "Query reference is invalid",
        messageFormat: "Method '{0}' contains query reference '{1}' that cannot be resolved.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryReferenceAmbiguous = new(
        id: "DBSGD018",
        title: "Query reference is ambiguous",
        messageFormat: "Method '{0}' contains query reference '{1}' that matches multiple joined tables.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinAliasConflict = new(
        id: "DBSGD019",
        title: "Join alias is duplicated",
        messageFormat: "Method '{0}' contains duplicate join alias '{1}'. Provide explicit aliases to disambiguate repeated tables.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor JoinSourceInvalid = new(
        id: "DBSGD020",
        title: "Join source is invalid",
        messageFormat: "Method '{0}' contains join source alias '{1}' that has not been introduced by an earlier join.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrderByConflict = new(
        id: "DBSGD021",
        title: "Multiple ORDER BY clauses are configured",
        messageFormat: "Method '{0}' defines multiple ORDER BY expressions. Only one ORDER BY may be configured per query.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AsyncReturnTypeRequired = new(
        id: "DBSGD006",
        title: "Async return type is required",
        messageFormat: "Repository method '{0}' must return Task or Task<T>.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CancellationTokenRequired = new(
        id: "DBSGD007",
        title: "CancellationToken parameter is required",
        messageFormat: "Repository method '{0}' must declare a CancellationToken parameter.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnitOfWorkMemberInvalid = new(
        id: "DBSGD008",
        title: "Unit of Work member is invalid",
        messageFormat: "Unit of Work interface '{0}' contains unsupported member '{1}'. Only read-only repository properties are allowed.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnitOfWorkRepositoryTypeInvalid = new(
        id: "DBSGD009",
        title: "Unit of Work repository property type is invalid",
        messageFormat: "Property '{0}' on Unit of Work interface '{1}' must be an interface marked with [DbRepository].",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnitOfWorkContainsNoRepositories = new(
        id: "DBSGD010",
        title: "Unit of Work contains no repository properties",
        messageFormat: "Unit of Work interface '{0}' must define at least one read-only repository property.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnitOfWorkRepositoryGenerationFailed = new(
        id: "DBSGD011",
        title: "Unit of Work references repository that failed generation",
        messageFormat: "Property '{0}' on Unit of Work interface '{1}' references repository '{2}', but repository generation failed due to diagnostics.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnitOfWorkDuplicateProperty = new(
        id: "DBSGD012",
        title: "Unit of Work contains duplicate repository property names",
        messageFormat: "Unit of Work interface '{0}' defines duplicate repository property name '{1}'.",
        category: "DreamBig.SourceGen.Dapper",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
