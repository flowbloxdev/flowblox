namespace FlowBlox.Core.Actions
{
    public abstract class FlowBloxBaseAction
    {
        protected FlowBloxBaseAction()
        {
            AssociatedActions = new List<FlowBloxBaseAction>();
        }

        public List<FlowBloxBaseAction> AssociatedActions { get; internal set; }

        public virtual void Undo()
        {
            for (var index = AssociatedActions.Count - 1; index >= 0; index--)
                AssociatedActions[index].Undo();
        }

        public virtual void Invoke()
        {
            AssociatedActions.ForEach(x => x.Invoke());
        }
    }
}
