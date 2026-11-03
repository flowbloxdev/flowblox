namespace FlowBlox.Core.Enums;

/// <summary>Built-in source identifiers reserved for FlowBlox field placeholders.</summary>
public enum FlowBloxSourceId
{
    /// <summary>Properties and paths of the active project, including legacy property aliases.</summary>
    Project,

    /// <summary>Application options explicitly enabled for use as placeholders.</summary>
    Options,

    /// <summary>User-defined input and memory fields belonging to a project.</summary>
    User,

    /// <summary>Context values supplied when a generation strategy builds its prompt.</summary>
    GenerationStrategy,

    /// <summary>Paths of the managed input file whose command is being executed.</summary>
    InputFile,

    /// <summary>Reserved test-definition context excluded from generation strategy field substitution.</summary>
    TestDefinition
}
