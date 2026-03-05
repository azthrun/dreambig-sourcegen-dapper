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
        messageFormat: "Repository method '{0}' has an unsupported signature for generation.",
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
}
