using FlowBlox.UICore.Views;


namespace FlowBlox.AppWindow.Contents
{
    public class ManagedObjectsView : WpfPanel
    {
        private readonly ManagedObjectsViewControl _managedObjectsViewControl;

        public ManagedObjectsView()
        {
            Text = FlowBlox.UICore.Resources.ManagedObjectsView.Title;
            Name = nameof(ManagedObjectsView);

            _managedObjectsViewControl = new ManagedObjectsViewControl();
            Child = _managedObjectsViewControl;


        }

        internal void AfterProjectFullyInitialized()
        {
            _managedObjectsViewControl.AfterProjectFullyInitialized();
        }

        internal void UpdateUI()
        {
        }
    }
}