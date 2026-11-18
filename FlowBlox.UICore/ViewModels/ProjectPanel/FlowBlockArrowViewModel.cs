using FlowBlox.UICore.Utilities;
using System.Windows;
using System.Windows.Media;

namespace FlowBlox.UICore.ViewModels.ProjectPanel
{
    public sealed class FlowBlockArrowViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private const double StartDotRadius = 3.5d;
        private bool _isSelected;

        public FlowBlockArrowViewModel(FlowBlockNodeViewModel from, FlowBlockNodeViewModel to, string kind, double offset = 0d, string label = null)
        {
            From = from;
            To = to;
            Kind = kind;
            Offset = offset;
            Label = label;
        }

        public FlowBlockNodeViewModel From { get; }
        public FlowBlockNodeViewModel To { get; }
        public string Kind { get; }
        public double Offset { get; }
        public string Label { get; }
        public bool CanRemove
        {
            get
            {
                return Kind == "invoke" || 
                       Kind == "recursive" || 
                       (Kind == "iteration" && From.InternalFlowBlock.AssociatedIterationContext != null);
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        private Point FromCenter => new(From.X + (From.Width / 2d), From.Y + (From.Height / 2d));
        private Point ToCenter => new(To.X + (To.Width / 2d), To.Y + (To.Height / 2d));
        private Rect FromBounds => new(From.X, From.Y, From.Width, From.Height);
        private Rect ToBounds => new(To.X, To.Y, To.Width, To.Height);
        private Point BaseStartPoint => ArrowGeometryHelper.GetEdgePoint(FromBounds, FromCenter, ToCenter);
        private Point BaseEndPoint => ArrowGeometryHelper.GetEdgePoint(ToBounds, ToCenter, FromCenter);
        private Vector OffsetVector => ArrowGeometryHelper.GetPerpendicularOffset(BaseStartPoint, BaseEndPoint, Offset);
        private Point StartPoint => BaseStartPoint + OffsetVector;
        private Point EndPoint => BaseEndPoint + OffsetVector;

        public double X1 => StartPoint.X;
        public double Y1 => StartPoint.Y;
        public double X2 => EndPoint.X;
        public double Y2 => EndPoint.Y;
        public double LabelX => (X1 + X2) / 2d;
        public double LabelY => (Y1 + Y2) / 2d;
        public double StartDotX => X1 - StartDotRadius;
        public double StartDotY => Y1 - StartDotRadius;
        public double StartDotSize => StartDotRadius * 2d;
        public PointCollection ArrowHeadPoints => ArrowGeometryHelper.CreateArrowHeadPoints(StartPoint, EndPoint);

        public bool HasSameIdentity(FlowBlockArrowViewModel other)
            => other != null &&
               ReferenceEquals(From.InternalFlowBlock, other.From.InternalFlowBlock) &&
               ReferenceEquals(To.InternalFlowBlock, other.To.InternalFlowBlock) &&
               Kind == other.Kind &&
               Offset.Equals(other.Offset);

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public void NotifyGeometryChanged()
        {
            OnPropertyChanged(nameof(X1));
            OnPropertyChanged(nameof(Y1));
            OnPropertyChanged(nameof(X2));
            OnPropertyChanged(nameof(Y2));
            OnPropertyChanged(nameof(LabelX));
            OnPropertyChanged(nameof(LabelY));
            OnPropertyChanged(nameof(StartDotX));
            OnPropertyChanged(nameof(StartDotY));
            OnPropertyChanged(nameof(StartDotSize));
            OnPropertyChanged(nameof(ArrowHeadPoints));
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}
