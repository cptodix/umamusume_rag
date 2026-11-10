using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace UmamusumeRag.Infrastructure.Persistence;

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
