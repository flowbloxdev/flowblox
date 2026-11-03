using FlowBlox.Core.Actions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.DeepCopier;

namespace FlowBlox.Core.Actions
{
    public class FlowBloxEditAction : FlowBloxBaseAction
    {
        private DynamicDeepCopier _restoreDeepCopier;
        private DynamicDeepCopier _repetitionDeepCopier;

        public object Target { get; set; }
        public object RestoreObject { get; private set; }
        public object RepetitionObject { get; private set; }

        public void CaptureRestoreState()
        {
            _restoreDeepCopier = new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(Target));
            RestoreObject = _restoreDeepCopier.Copy(Target);
        }

        public void CaptureRepetitionState()
        {
            _repetitionDeepCopier = new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(Target));
            RepetitionObject = _repetitionDeepCopier.Copy(Target);
        }

        public void CaptureRepetitionState(Action<object, DynamicDeepCopier> change)
        {
            if (change == null)
                throw new ArgumentNullException(nameof(change));

            _repetitionDeepCopier = new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(Target));
            RepetitionObject = _repetitionDeepCopier.Copy(Target);
            change(RepetitionObject, _repetitionDeepCopier);
        }

        public override void Undo()
        {
            _restoreDeepCopier.PropertyActions = FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(RestoreObject);
            _restoreDeepCopier.ClearRecopyCache();
            _restoreDeepCopier.SpecialMode = DeepCopierSpecialMode.Recopy;
            RestoreSnapshot(_restoreDeepCopier, RestoreObject);
            OnAfterExecute();
            base.Undo();
        }

        public override void Invoke()
        {
            _repetitionDeepCopier.PropertyActions = FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(RepetitionObject);
            _repetitionDeepCopier.ClearRecopyCache();
            _repetitionDeepCopier.SpecialMode = DeepCopierSpecialMode.Recopy;
            RestoreSnapshot(_repetitionDeepCopier, RepetitionObject);
            OnAfterExecute();
            base.Invoke();
        }

        private void RestoreSnapshot(DynamicDeepCopier copier, object snapshot)
        {
            var flowBlock = Target as BaseFlowBlock;
            flowBlock?.SuppressInputBehaviorSynchronization();
            try
            {
                copier.Copy(snapshot, Target);
            }
            finally
            {
                flowBlock?.RestoreInputBehaviorSynchronization();
            }
        }

        private void OnAfterExecute()
        {
            if (Target is BaseFlowBlock flowBlock)
                flowBlock.OnAfterSave();
        }
    }
}
