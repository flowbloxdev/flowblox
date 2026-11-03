using FlowBlox.Core.Interfaces;

namespace FlowBlox.AvaloniaDesktop.Services
{
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public class FlowBloxUIEvaluationService : IFlowBloxUIEvaluationService
    {
        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <returns><inheritdoc/></returns>
        public bool IsUISupported()
        {
            return true;
        }
    }
}
