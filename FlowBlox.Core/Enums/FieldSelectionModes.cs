namespace FlowBlox.Core.Enums
{
    [System.Flags]
    public enum FieldSelectionModes
    {
        Default = 0,
        Fields = 1,
        ProjectProperties = 2,
        Options = 4,
        InputFiles = 8,
        GenerationStrategyData = 16,
        Vault = 32
    }
}
