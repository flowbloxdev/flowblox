using FlowBlox.Core.Actions;
using FlowBlox.Core.Interfaces;
using FlowBlox.AvaloniaDesktop.Providers;
using System.Collections.Generic;
using System.Linq;

namespace FlowBlox.AvaloniaDesktop.Services
{
    internal sealed class FlowBloxActionHistoryService : IFlowBloxActionHistoryService
    {
        private readonly FlowBloxProjectComponentProvider _componentProvider;

        public FlowBloxActionHistoryService(FlowBloxProjectComponentProvider componentProvider)
        {
            _componentProvider = componentProvider;
        }

        public void RegisterAction(FlowBloxBaseAction action)
        {
            if (action != null)
                _componentProvider.GetCurrentChangelist()?.AddChange(action);
        }

        public void RegisterAutoLayoutMoves(IReadOnlyList<FlowBloxMoveAction> moveActions)
        {
            if (moveActions == null || moveActions.Count == 0)
                return;

            var rootAction = moveActions[0];
            foreach (var moveAction in moveActions.Skip(1))
                rootAction.AssociatedActions.Add(moveAction);

            _componentProvider.GetCurrentChangelist().AddChange(rootAction);
        }
    }
}
