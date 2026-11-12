namespace FlowBlox.Core.Exceptions;

public sealed class ProjectSpaceLoginRequiredException : InvalidOperationException
{
    public ProjectSpaceLoginRequiredException()
        : base("The Project Space project is not accessible without signing in.")
    {
    }
}
