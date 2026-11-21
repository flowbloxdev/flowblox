using FlowBlox.Core;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Components;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace FlowBlox.UICore.ViewModels.FieldView
{
    public sealed class FieldEntryViewModel : INotifyPropertyChanged
    {
        private const int DefaultShortenedTextLength = 4000;

        private string _sourceName;
        private string _fieldName;
        private string _fieldValue;
        private string _shortenedText;
        private string _rawFieldValue;
        private bool _isPendingValue;
        private bool _singleLineFieldValues;
        private bool _isFlowBlockSelected;
        private bool _isAutoSelected;
        private int _maxDisplayLength = DefaultShortenedTextLength;

        public FieldElement FieldElement { get; }

        public string SourceName
        {
            get => _sourceName;
            private set
            {
                if (_sourceName == value)
                    return;

                _sourceName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullyQualifiedName));
            }
        }

        public string FieldName
        {
            get => _fieldName;
            private set
            {
                if (_fieldName == value)
                    return;

                _fieldName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullyQualifiedName));
            }
        }

        public string FieldValue
        {
            get => _fieldValue;
            private set
            {
                if (_fieldValue == value)
                    return;

                _fieldValue = value;
                OnPropertyChanged();
                ShortenedText = GetShortenedText(_fieldValue, _maxDisplayLength);
            }
        }

        public string ShortenedText
        {
            get => _shortenedText;
            private set
            {
                if (_shortenedText == value)
                    return;

                _shortenedText = value;
                OnPropertyChanged();
            }
        }

        public string FullyQualifiedName => FieldElement?.FullyQualifiedName ?? string.Empty;

        public bool IsUserField => FieldElement?.UserField == true;

        public PackIconMaterialKind IconKind => IsUserField ? PackIconMaterialKind.Account : PackIconMaterialKind.LinkVariant;

        public ImageSource IconSource { get; private set; }

        public bool IsFlowBlockSelected
        {
            get => _isFlowBlockSelected;
            set
            {
                if (_isFlowBlockSelected == value)
                    return;

                _isFlowBlockSelected = value;
                OnPropertyChanged();
            }
        }

        public bool IsAutoSelected
        {
            get => _isAutoSelected;
            set
            {
                if (_isAutoSelected == value)
                    return;

                _isAutoSelected = value;
                OnPropertyChanged();
            }
        }

        public FieldEntryViewModel(FieldElement fieldElement)
        {
            FieldElement = fieldElement;
            _sourceName = fieldElement?.FlowBlockName ?? string.Empty;
            _fieldName = fieldElement?.Name ?? string.Empty;
            _rawFieldValue = fieldElement?.StringValue ?? string.Empty;
            _isPendingValue = fieldElement?.Pending == true;
            _fieldValue = GetDisplayFieldValue(_rawFieldValue, _isPendingValue, _singleLineFieldValues, fieldElement?.IsPassword == true);
            _shortenedText = GetShortenedText(_fieldValue, _maxDisplayLength);

            if (IsUserField)
            {
                IconSource = SkiaWpfImageHelper.ConvertToImageSource(fieldElement.Icon16);
            }
            else
            {
                var icon16 = fieldElement?.Source?.Icon16;
                IconSource = icon16 != null
                    ? SkiaWpfImageHelper.ConvertToImageSource(icon16)
                    : WpfIconHelper.CreateMaterialIcon(PackIconMaterialKind.CubeOutline, 14);
            }
        }

        public void UpdateName()
        {
            SourceName = FieldElement?.FlowBlockName ?? string.Empty;
            FieldName = FieldElement?.Name ?? string.Empty;
        }

        public void UpdateValue(string value, bool pending)
        {
            _rawFieldValue = value ?? string.Empty;
            _isPendingValue = pending;
            if (IsUserField)
            {
                IconSource = SkiaWpfImageHelper.ConvertToImageSource(FieldElement.Icon16);
                OnPropertyChanged(nameof(IconSource));
            }
            FieldValue = GetDisplayFieldValue(_rawFieldValue, _isPendingValue, _singleLineFieldValues, FieldElement?.IsPassword == true);
        }

        public void SetSingleLineFieldValues(bool enabled)
        {
            if (_singleLineFieldValues == enabled)
                return;

            _singleLineFieldValues = enabled;
            FieldValue = GetDisplayFieldValue(_rawFieldValue, _isPendingValue, _singleLineFieldValues, FieldElement?.IsPassword == true);
        }

        public void SetMaxDisplayLength(int maxDisplayLength)
        {
            if (maxDisplayLength <= 0)
                maxDisplayLength = DefaultShortenedTextLength;

            if (_maxDisplayLength == maxDisplayLength)
                return;

            _maxDisplayLength = maxDisplayLength;
            ShortenedText = GetShortenedText(_fieldValue, _maxDisplayLength);
        }

        private static string GetDisplayFieldValue(string value, bool pending, bool singleLine, bool isPassword)
        {
            if (pending)
                return FlowBloxTexts.FieldElement_PendingValue;

            if (isPassword)
                return GetMaskedFieldValue(value);

            if (!singleLine)
                return value ?? string.Empty;

            var input = value ?? string.Empty;
            var normalized = Regex.Replace(input, @"\s*[\r\n]+\s*", " ");
            normalized = Regex.Replace(normalized, @"[ ]{2,}", " ").Trim();
            return normalized;
        }

        private static string GetMaskedFieldValue(string value)
        {
            return GlobalConstants.HiddenSensitiveValue;
        }

        private static string GetShortenedText(string value, int maxDisplayLength)
        {
            var text = value ?? string.Empty;
            if (text.Length <= maxDisplayLength)
                return text;

            return text.Substring(0, maxDisplayLength) + "...";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
