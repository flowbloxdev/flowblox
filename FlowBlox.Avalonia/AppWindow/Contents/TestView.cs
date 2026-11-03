using FlowBlox.UICore.Views;


namespace FlowBlox.AppWindow.Contents
{
    public class TestView : WpfPanel
    {
        private readonly TestViewControl _testViewControl;

        public TestView()
        {
            Text = FlowBlox.UICore.Resources.TestView.Title;
            Name = nameof(TestView);

            _testViewControl = new TestViewControl();
            Child = _testViewControl;


        }

        internal void AfterProjectFullyInitialized()
        {
            _testViewControl.AfterProjectFullyInitialized();
        }

        internal void UpdateUI()
        {
        }
    }
}