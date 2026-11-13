namespace FlowBlox.Core.Attributes
{
    /// <summary>
    /// Object-management operations offered by association and collection editors.
    /// </summary>
    [Flags]
    public enum UIOperations
    {
        None = 0,
        Create = 1,
        Edit = 2,
        Delete = 4,
        Link = 8,
        Unlink = 16,
        All = Create | Edit | Delete | Link | Unlink
    }
}
