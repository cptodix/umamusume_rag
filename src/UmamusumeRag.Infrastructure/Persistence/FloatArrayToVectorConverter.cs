using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace UmamusumeRag.Infrastructure.Persistence;

// ===== RAG step 4/8 · STORE (type bridge) ==================================
// Converts the domain's float[] embedding to pgvector's Vector type on the way
// into the database, and back on the way out. This is the only place where the
// two representations meet.
//
// TWEAK: if you switch the entity to `Vector` directly (to get LINQ distance
//        functions), delete this converter and update ChunkConfiguration.
// ============================================================================

/// <summary>
/// Maps the domain's <c>float[]</c> embedding to pgvector's <see cref="Vector"/>
/// database type, keeping the Core project free of database-specific types.
/// </summary>
internal sealed class FloatArrayToVectorConverter : ValueConverter<float[], Vector>
{
    public FloatArrayToVectorConverter()
        : base(embedding => new Vector(embedding), vector => vector.ToArray())
    {
    }
}
