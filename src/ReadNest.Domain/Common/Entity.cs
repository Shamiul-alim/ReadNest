namespace ReadNest.Domain.Common;

public abstract class Entity
{
    /// <summary>Time-ordered GUIDs (v7) keep B-tree indexes compact.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
