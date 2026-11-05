using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FlowBlox.UICore.ViewModels
{
    public sealed class ProjectPanelGridSettingsViewModel : INotifyPropertyChanged
    {
        public const int MinimumGridWidth = 2000;
        public const int MinimumGridHeight = 1000;

        private readonly FlowBloxProject _project;

        private int _gridWidth;
        private int _gridHeight;
        private bool _autoIncreaseGridSize;
        private bool _resetNotificationsOnRuntimeFinish;

        public ProjectPanelGridSettingsViewModel(FlowBloxProject project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));

            _gridWidth = Math.Max(MinimumGridWidth, project.GridSizeX);
            _gridHeight = Math.Max(MinimumGridHeight, project.GridSizeY);
            _autoIncreaseGridSize = project.AutoIncreaseGridSize;

            var options = FlowBloxOptions.GetOptionInstance();
            _resetNotificationsOnRuntimeFinish =
                options.GetOption(OptionsConstants.GridResetNotificationsOnRuntimeFinish)?.GetValueBoolean() ?? true;
        }

        public int GridWidth
        {
            get => _gridWidth;
            set
            {
                if (_gridWidth == value)
                    return;

                _gridWidth = value;
                OnPropertyChanged();
            }
        }

        public int GridHeight
        {
            get => _gridHeight;
            set
            {
                if (_gridHeight == value)
                    return;

                _gridHeight = value;
                OnPropertyChanged();
            }
        }

        public bool ResetNotificationsOnRuntimeFinish
        {
            get => _resetNotificationsOnRuntimeFinish;
            set
            {
                if (_resetNotificationsOnRuntimeFinish == value)
                    return;

                _resetNotificationsOnRuntimeFinish = value;
                OnPropertyChanged();
            }
        }

        public bool AutoIncreaseGridSize
        {
            get => _autoIncreaseGridSize;
            set
            {
                if (_autoIncreaseGridSize == value)
                    return;

                _autoIncreaseGridSize = value;
                OnPropertyChanged();
            }
        }

        public bool TrySave(out string validationMessage)
        {
            if (GridWidth < MinimumGridWidth)
            {
                validationMessage = string.Format(
                    Resources.ProjectPanelGridSettingsWindow.Validation_InvalidWidth_Message,
                    MinimumGridWidth);
                return false;
            }

            if (GridHeight < MinimumGridHeight)
            {
                validationMessage = string.Format(
                    Resources.ProjectPanelGridSettingsWindow.Validation_InvalidHeight_Message,
                    MinimumGridHeight);
                return false;
            }

            _project.GridSizeX = GridWidth;
            _project.GridSizeY = GridHeight;
            _project.AutoIncreaseGridSize = AutoIncreaseGridSize;

            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(OptionsConstants.GridResetNotificationsOnRuntimeFinish);
            if (option == null)
            {
                option = new OptionElement(
                    "Grid.ResetNotificationsOnRuntimeFinish",
                    "true",
                    "Controls whether flow-block notifications (warning/error) are reset when runtime execution finishes.",
                    OptionElement.OptionType.Boolean);
                options.OptionCollection[OptionsConstants.GridResetNotificationsOnRuntimeFinish] = option;
            }

            option.Value = ResetNotificationsOnRuntimeFinish.ToString().ToLowerInvariant();
            options.Save();

            validationMessage = string.Empty;
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}