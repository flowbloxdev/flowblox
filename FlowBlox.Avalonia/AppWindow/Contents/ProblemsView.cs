using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Views;


namespace FlowBlox.AppWindow.Contents
{
    public class ProblemsView : WpfPanel
    {
        private readonly ProblemsViewControl _problemsViewControl;

        public ProblemsView()
        {
            Text = FlowBloxResourceUtil.GetLocalizedString("ProblemsView_Text", typeof(FlowBloxMainUITexts));
            Name = Text;

            _problemsViewControl = new ProblemsViewControl();
            Child = _problemsViewControl;


        }

        internal void Append(ProblemTrace problemTrace)
        {
            _problemsViewControl.Append(problemTrace);
        }
    }
}