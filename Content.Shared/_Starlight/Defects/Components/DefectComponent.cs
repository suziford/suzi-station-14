namespace Content.Shared._Starlight.Defects.Components;

public abstract partial class DefectComponent : Component
{
    [DataField]
    public float Prob = 1.0f;

    [DataField]
    public string DefectLabel = string.Empty;
}
