namespace OpenIddictUI.Api;

public enum OperationClassification
{
    ReadOnly,
    Write
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OperationClassificationAttribute(OperationClassification classification) : Attribute
{
    public OperationClassification Classification { get; } = classification;
}
