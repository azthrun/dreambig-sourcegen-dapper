namespace DreamBig.SourceGen.Dapper.Attributes;

/// <summary>
/// Repository operation kinds that can be declared explicitly with <see cref="DbOperationAttribute"/>.
/// </summary>
public enum DbOperationKind
{
    /// <summary>Insert a single entity.</summary>
    Insert,

    /// <summary>Update a single entity by key.</summary>
    Update,

    /// <summary>Delete rows by key or by filter properties.</summary>
    Delete,

    /// <summary>Select a single entity by key.</summary>
    GetById,

    /// <summary>Select all entities.</summary>
    GetAll,

    /// <summary>Select a page of entities.</summary>
    GetPage,

    /// <summary>Select entities filtered by one or more properties.</summary>
    GetBy,

    /// <summary>Count rows, optionally filtered by properties.</summary>
    Count,

    /// <summary>Check whether any rows exist, optionally filtered by properties.</summary>
    Exists,
}
