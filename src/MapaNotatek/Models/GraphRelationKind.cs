namespace MapaNotatek.Models;

public enum GraphRelationKind
{
    Hierarchy,
    ProjectMembership,
    WikiLink,
    SystemMembership,
    ExplicitNoteRelation
}

public sealed record GraphConnectionResult(bool IsValid, string Message, GraphRelationKind? Kind = null);
