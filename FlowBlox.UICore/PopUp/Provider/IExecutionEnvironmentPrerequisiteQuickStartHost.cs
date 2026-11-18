namespace FlowBlox.UICore.PopUp.Provider;

/// <summary>
/// Dispatches setup guidance to the desktop UI and supplies the current dialog owner.
/// </summary>
public interface IExecutionEnvironmentPrerequisiteQuickStartHost
{
    void Post(Action action);
    void Show(IComponentPopupProvider provider);
}
