using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace FlowBlox.AppWindow.ContentFactories
{
    public abstract class DockContentFactoryBase<T> where T : DockContent
    {
        private static readonly object _dockSettingsSaveLock = new object();

        protected readonly DockPanel _dockPanel;
        private bool _ready;
        private bool _closing;
        private bool _dockPanelResizeInProgress;
        private Size _lastDockPanelClientSize;
        private bool _applyingAutoResizeFixedSize;
        private DockPanelLayoutDebouncer.DockPanelLayoutDebounceRegistration _layoutDebounceRegistration;

        public DockContentFactoryBase(DockPanel dockPanel)
        {
            _dockPanel = dockPanel;
            _lastDockPanelClientSize = dockPanel?.ClientSize ?? Size.Empty;
        }

        protected DockContentSettings LoadSettings(string key)
        {
            var dockSettingsValue = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.MainPanelDockSettings).Value;
            if (!string.IsNullOrEmpty(dockSettingsValue))
            {
                var dockSettings = JsonConvert.DeserializeObject<DockSettings>(dockSettingsValue);
                if (dockSettings.DockContentSettings.TryGetValue(key, out var dockContentSettings))
                {
                    return dockContentSettings;
                }
            }

            var defaults = GetDefaults();
            return defaults;
        }

        protected virtual DockContentSettings GetDefaults()
        {
            return new DockContentSettings
            {
                DockState = DockState.DockBottom,
                Visible = true
            };
        }

        protected void SaveSettings(string key, DockContentSettings settings)
        {
            lock (_dockSettingsSaveLock)
            {
                var dockSettingsOption = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.MainPanelDockSettings);
                var dockSettingsValue = dockSettingsOption.Value;
                var dockSettings = !string.IsNullOrEmpty(dockSettingsValue)
                    ? JsonConvert.DeserializeObject<DockSettings>(dockSettingsValue)
                    : new DockSettings();

                dockSettings.DockContentSettings[key] = settings;
                dockSettingsOption.Value = JsonConvert.SerializeObject(dockSettings);
                FlowBloxOptions.GetOptionInstance().Save();
            }
        }

        protected Task SaveSettingsAsync(string key, DockContentSettings settings)
        {
            return Task.Run(() => 
            { 
                SaveSettings(key, settings); 
            });
        }

        protected T Create(string key, T dockContent)
        {
            var settings = LoadSettings(key);
            ApplyDockContentIcon(dockContent);

            if (settings.Width != null)
                dockContent.Width = settings.Width.Value;

            if (settings.Height != null)
                dockContent.Height = settings.Height.Value;

            _layoutDebounceRegistration = DockPanelLayoutDebouncer.For(_dockPanel).Register(key);
            dockContent.Show(_dockPanel, settings.DockState);

            // Apply fixed orientation size immediately after docking so startup layout
            // uses persisted values instead of default DockPanel portions (0.25).
            TryApplyFixedSizeForAutoResize(dockContent, settings);

            if (_dockPanel.IsHandleCreated && !_dockPanel.IsDisposed)
            {
                _dockPanel.BeginInvoke(new MethodInvoker(() =>
                {
                    NotifyLayoutActivity("BeginInvoke");
                }));
            }
            else
            {
                NotifyLayoutActivity("NoHandle");
            }

            dockContent.HideOnClose = true;

            if (!settings.Visible)
                dockContent.Hide();

            dockContent.Layout += _dockPanel_Layout;
            _dockPanel.SizeChanged += DockPanel_SizeChanged;

            dockContent.FormClosing += DockContent_FormClosing;

            dockContent.DockStateChanged += (s, e) =>
            {
                if (!_ready)
                    return;

                if (_closing)
                    return;

                if (dockContent.DockState == DockState.Unknown ||
                    dockContent.DockState == DockState.Hidden)
                {
                    settings.Visible = false;
                }
                else
                {
                    settings.DockState = dockContent.DockState;
                    settings.Visible = true;
                }
                SaveSettings(key, settings);
            };

            dockContent.SizeChanged += async (s, e) =>
            {
                if (!_ready)
                    return;

                if (_closing)
                    return;

                if (IsLayoutSuppressed())
                {
                    NotifyLayoutActivity("SizeChanged");
                    return;
                }

                if (dockContent.DockState == DockState.Hidden || dockContent.DockState == DockState.Unknown)
                    return;

                if (IsAutoResizeChange())
                {
                    TryApplyFixedSizeForAutoResize(dockContent, settings);
                    return;
                }

                if (dockContent.Width == settings.Width && dockContent.Height == settings.Height)
                    return;

                if (dockContent.Width > 0 && dockContent.Height > 0)
                {
                    settings.Width = dockContent.Width;
                    settings.Height = dockContent.Height;
                    await SaveSettingsAsync(key, settings);
                }
            };

            return dockContent;
        }

        private static void ApplyDockContentIcon(DockContent dockContent)
        {
            if (dockContent == null)
                return;

            var image = DockContentIconResolver.Resolve(dockContent);
            if (image == null)
                return;

            using var resized = new Bitmap(image, new Size(16, 16));
            var iconHandle = resized.GetHicon();
            using var icon = Icon.FromHandle(iconHandle);
            dockContent.Icon = (Icon)icon.Clone();
        }

        private void DockContent_FormClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            _layoutDebounceRegistration?.Stop();
        }

        private void DockPanel_SizeChanged(object sender, System.EventArgs e)
        {
            _dockPanelResizeInProgress = true;
            NotifyLayoutActivity("DockPanel.SizeChanged");

            if (_dockPanel.IsHandleCreated && !_dockPanel.IsDisposed)
            {
                _dockPanel.BeginInvoke(new MethodInvoker(() =>
                {
                    _lastDockPanelClientSize = _dockPanel.ClientSize;
                    _dockPanelResizeInProgress = false;
                }));
            }
            else
            {
                _lastDockPanelClientSize = _dockPanel.ClientSize;
                _dockPanelResizeInProgress = false;
            }
        }

        private bool IsAutoResizeChange()
        {
            return _dockPanelResizeInProgress || _dockPanel.ClientSize != _lastDockPanelClientSize;
        }

        private bool IsLayoutSuppressed()
        {
            return _layoutDebounceRegistration?.IsSuppressed == true;
        }

        private void NotifyLayoutActivity(string reason)
        {
            _layoutDebounceRegistration?.NotifyActivity(reason);
        }

        private void _dockPanel_Layout(object sender, LayoutEventArgs e)
        {
            _ready = true;
            NotifyLayoutActivity($"Layout:{e?.AffectedProperty}");
        }

        private void TryApplyFixedSizeForAutoResize(DockContent dockContent, DockContentSettings settings)
        {
            if (_applyingAutoResizeFixedSize)
                return;

            _applyingAutoResizeFixedSize = true;
            try
            {
                switch (dockContent.DockState)
                {
                    case DockState.DockLeft:
                    case DockState.DockLeftAutoHide:
                        ApplyFixedWidthPortion(settings, _dockPanel.DockLeftPortion, value => _dockPanel.DockLeftPortion = value);
                        break;
                    case DockState.DockRight:
                    case DockState.DockRightAutoHide:
                        ApplyFixedWidthPortion(settings, _dockPanel.DockRightPortion, value => _dockPanel.DockRightPortion = value);
                        break;
                    case DockState.DockTop:
                    case DockState.DockTopAutoHide:
                        ApplyFixedHeightPortion(settings, _dockPanel.DockTopPortion, value => _dockPanel.DockTopPortion = value);
                        break;
                    case DockState.DockBottom:
                    case DockState.DockBottomAutoHide:
                        ApplyFixedHeightPortion(settings, _dockPanel.DockBottomPortion, value => _dockPanel.DockBottomPortion = value);
                        break;
                }
            }
            finally
            {
                _applyingAutoResizeFixedSize = false;
            }
        }

        private void ApplyFixedWidthPortion(DockContentSettings settings, double currentPortion, System.Action<double> applyPortion)
        {
            var width = GetFixedWidth(settings, currentPortion);
            if (width > 0)
                applyPortion(width);
        }

        private void ApplyFixedHeightPortion(DockContentSettings settings, double currentPortion, System.Action<double> applyPortion)
        {
            var height = GetFixedHeight(settings, currentPortion);
            if (height > 0)
                applyPortion(height);
        }

        private int GetFixedWidth(DockContentSettings settings, double currentPortion)
        {
            if (settings.Width.HasValue && settings.Width.Value > 0)
                return settings.Width.Value;

            var width = ConvertPortionToPixels(currentPortion, _dockPanel.ClientSize.Width);
            settings.Width = width;
            return width;
        }

        private int GetFixedHeight(DockContentSettings settings, double currentPortion)
        {
            if (settings.Height.HasValue && settings.Height.Value > 0)
                return settings.Height.Value;

            var height = ConvertPortionToPixels(currentPortion, _dockPanel.ClientSize.Height);
            settings.Height = height;
            return height;
        }

        private static int ConvertPortionToPixels(double portion, int availableSize)
        {
            if (portion > 1)
                return (int)System.Math.Round(portion);

            if (portion <= 0 || availableSize <= 0)
                return 0;

            return (int)System.Math.Round(portion * availableSize);
        }

        public abstract DockContent Create();
    }
}


