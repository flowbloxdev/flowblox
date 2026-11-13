using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Vault;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Texts = FlowBlox.UICore.Resources.VaultWindow;

namespace FlowBlox.UICore.Views;

public partial class VaultWindow : MetroWindow, INotifyPropertyChanged
{
    public ObservableCollection<VaultItem> Entries { get; } = new();
    public VaultItem SelectedEntry => EntriesList.SelectedItem as VaultItem;
    public bool HasSelection => SelectedEntry != null;
    public string NoSelectionText => FlowBloxResourceUtil.GetLocalizedString("NoSelection", typeof(Texts));
    private bool _isDirty;
    private bool _updatingPassword;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            _isDirty = value;
            Notify();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public VaultWindow()
    {
        InitializeComponent();
        foreach (var entry in FlowBloxVault.Load())
            Entries.Add(CreateItem(entry));
        DataContext = this;
        UpdateValueEditor();
    }

    private VaultItem CreateItem(VaultEntry entry)
    {
        var item = new VaultItem(entry);
        item.PropertyChanged += (_, args) =>
        {
            IsDirty = true;
            if (args.PropertyName == nameof(VaultItem.IsPassword))
                UpdateValueEditor();
        };
        return item;
    }

    private void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Notify(nameof(SelectedEntry));
        Notify(nameof(HasSelection));
        UpdateValueEditor();
    }

    private void UpdateValueEditor()
    {
        if (ValueEditor == null || PasswordEditor == null)
            return;
        var protectedValue = SelectedEntry?.IsPassword == true;
        ValueEditor.Visibility = protectedValue ? Visibility.Collapsed : Visibility.Visible;
        PasswordEditor.Visibility = protectedValue ? Visibility.Visible : Visibility.Collapsed;
        _updatingPassword = true;
        try
        {
            PasswordEditor.Password = SelectedEntry?.Value ?? string.Empty;
        }
        finally
        {
            _updatingPassword = false;
        }
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (!_updatingPassword && SelectedEntry?.IsPassword == true)
            SelectedEntry.Value = PasswordEditor.Password;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var index = 1;
        while (Entries.Any(entry => entry.Name.Equals("Value" + index, StringComparison.OrdinalIgnoreCase)))
            index++;
        var item = CreateItem(new VaultEntry { Name = "Value" + index });
        Entries.Add(item);
        EntriesList.SelectedItem = item;
        IsDirty = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry == null)
            return;
        Entries.Remove(SelectedEntry);
        IsDirty = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FlowBloxVault.Save(Entries.Select(entry => entry.Entry));
            IsDirty = false;
        }
        catch (ArgumentException)
        {
            ShowMessage("InvalidNames", FlowBloxMessageBoxTypes.Warning);
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to save local Vault entries.", exception);
            ShowMessage("SaveError", FlowBloxMessageBoxTypes.Error);
        }
    }

    private FlowBloxMessageBoxDialogResult ShowMessage(string key, FlowBloxMessageBoxTypes type) =>
        FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>().ShowMessageBox(
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts)),
            FlowBloxResourceUtil.GetLocalizedString("Title", typeof(Texts)), type);

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (IsDirty && ShowMessage("DiscardChanges", FlowBloxMessageBoxTypes.Question) != FlowBloxMessageBoxDialogResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed class VaultItem : INotifyPropertyChanged
    {
        internal VaultEntry Entry { get; }
        public VaultItem(VaultEntry entry) => Entry = entry;
        public string Name
        {
            get => Entry.Name;
            set => Set(Entry.Name, value, next => Entry.Name = next);
        }

        public string Description
        {
            get => Entry.Description;
            set => Set(Entry.Description, value, next => Entry.Description = next);
        }

        public string Value
        {
            get => Entry.Value;
            set => Set(Entry.Value, value, next => Entry.Value = next);
        }

        public bool IsPassword
        {
            get => Entry.IsPassword;
            set => Set(Entry.IsPassword, value, next => Entry.IsPassword = next);
        }

        public string Reference => Entry.Reference;
        public string Preview => IsPassword ? "••••••••" : Value;
        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(T previous, T value, Action<T> setter, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(previous, value))
                return;
            setter(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Reference)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }
}
