using FlowBlox.Core.Extensions;
using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using ICSharpCode.SharpZipLib.Zip;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "ZipArchiveObject_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("ZipArchiveObject_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class ZipArchiveObject : ManagedObject
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.folder_zip_outline, 16, new SKColor(14, 116, 144));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.folder_zip_outline, 32, new SKColor(14, 116, 144));
        [Display(Name = "PropertyNames_DataSource", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation]
        [FlowBloxItemsSource(Method = nameof(GetPossibleDataSources), DisplayMember = nameof(DataObjectBase.Name))]
        public DataObjectBase DataSource { get; set; }

        private IEnumerable<DataObjectBase> GetPossibleDataSources()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetManagedObjects<DataObjectBase>();
        }

        private byte[] _archiveContent;
        private string _defaultPassword;

        public FlowBlocks.Compression.ZipCompressionStrength CompressionStrength { get; private set; } = FlowBlocks.Compression.ZipCompressionStrength.Medium;

        public bool IsArchiveInitialized => _archiveContent != null;

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            _archiveContent = null;
            _defaultPassword = null;
        }

        private static byte[] CreateEmptyArchive()
        {
            using var ms = new MemoryStream();
            using (var zipOutput = new ZipOutputStream(ms))
            {
                zipOutput.SetLevel(0);
                zipOutput.Finish();
            }
            return ms.ToArray();
        }

        private static string NormalizeEntryPath(string entryPath)
        {
            return (entryPath ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private string ResolvePassword(string password)
        {
            if (!string.IsNullOrEmpty(password))
                return password;

            return _defaultPassword;
        }

        private void EnsureArchiveLoaded()
        {
            if (_archiveContent != null)
                return;

            if (DataSource == null)
                throw new InvalidOperationException("ZIP archive is not initialized. Use ZipArchiveCreator or configure a readable data source.");

            if (!DataSource.CanRead())
                throw new InvalidOperationException("The configured data source is not readable.");

            var content = DataSource.Content;
            if (content == null || content.Length == 0)
                throw new InvalidOperationException("No ZIP archive content found in the configured data source.");

            try
            {
                using var ms = new MemoryStream(content, writable: false);
                using var zipFile = new ZipFile(ms);
                _ = zipFile.Count;
                _archiveContent = content;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("The configured data source does not contain a valid ZIP archive.", e);
            }
        }

        public void CreateNewArchive(FlowBlocks.Compression.ZipCompressionStrength compressionStrength, string password)
        {
            CompressionStrength = compressionStrength;
            _defaultPassword = password ?? string.Empty;
            _archiveContent = CreateEmptyArchive();

            if (DataSource != null)
                DataSource.Content = _archiveContent;
        }

        public int GetCompressionLevel()
        {
            return this.Resolve(x => x.CompressionStrength) switch
            {
                FlowBlocks.Compression.ZipCompressionStrength.None => 0,
                FlowBlocks.Compression.ZipCompressionStrength.Low => 3,
                FlowBlocks.Compression.ZipCompressionStrength.Medium => 6,
                FlowBlocks.Compression.ZipCompressionStrength.High => 9,
                FlowBlocks.Compression.ZipCompressionStrength.VeryHigh => 9,
                _ => 6
            };
        }

        public void AppendOrReplaceEntry(string password, string entryPath, byte[] content)
        {
            EnsureArchiveLoaded();

            var normalizedPath = NormalizeEntryPath(entryPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                throw new InvalidOperationException("A valid ZIP entry path is required.");

            var resolvedPassword = ResolvePassword(password);
            var compressionLevel = GetCompressionLevel();

            var existingEntries = new List<(string Name, byte[] Content, DateTime DateTime)>();

            using (var inputStream = new MemoryStream(_archiveContent, writable: false))
            using (var zipInput = new ZipInputStream(inputStream))
            {
                if (!string.IsNullOrEmpty(resolvedPassword))
                    zipInput.Password = resolvedPassword;

                ZipEntry entry;
                while ((entry = zipInput.GetNextEntry()) != null)
                {
                    if (!entry.IsFile)
                        continue;

                    var currentName = NormalizeEntryPath(entry.Name);
                    if (string.Equals(currentName, normalizedPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var buffer = new MemoryStream();
                    zipInput.CopyTo(buffer);
                    existingEntries.Add((currentName, buffer.ToArray(), entry.DateTime));
                }
            }

            using var outputStream = new MemoryStream();
            using (var zipOutput = new ZipOutputStream(outputStream))
            {
                zipOutput.SetLevel(compressionLevel);

                if (!string.IsNullOrEmpty(resolvedPassword))
                    zipOutput.Password = resolvedPassword;

                foreach (var existingEntry in existingEntries)
                {
                    var outEntry = new ZipEntry(existingEntry.Name)
                    {
                        DateTime = existingEntry.DateTime
                    };

                    if (!string.IsNullOrEmpty(resolvedPassword))
                    {
                        outEntry.IsCrypted = true;
                        outEntry.AESKeySize = 256;
                    }

                    zipOutput.PutNextEntry(outEntry);
                    zipOutput.Write(existingEntry.Content, 0, existingEntry.Content.Length);
                    zipOutput.CloseEntry();
                }

                var newContent = content ?? Array.Empty<byte>();
                var newEntry = new ZipEntry(normalizedPath)
                {
                    DateTime = DateTime.Now
                };

                if (!string.IsNullOrEmpty(resolvedPassword))
                {
                    newEntry.IsCrypted = true;
                    newEntry.AESKeySize = 256;
                }

                zipOutput.PutNextEntry(newEntry);
                zipOutput.Write(newContent, 0, newContent.Length);
                zipOutput.CloseEntry();
                zipOutput.Finish();
            }

            SetArchiveContent(outputStream.ToArray());
        }

        public List<(string File, byte[] Content)> ReadFileEntries(string password)
        {
            EnsureArchiveLoaded();

            var resolvedPassword = ResolvePassword(password);
            var result = new List<(string File, byte[] Content)>();

            using var inputStream = new MemoryStream(_archiveContent, writable: false);
            using var zipInput = new ZipInputStream(inputStream);

            if (!string.IsNullOrEmpty(resolvedPassword))
                zipInput.Password = resolvedPassword;

            ZipEntry entry;
            while ((entry = zipInput.GetNextEntry()) != null)
            {
                if (!entry.IsFile)
                    continue;

                using var entryStream = new MemoryStream();
                zipInput.CopyTo(entryStream);

                var absoluteArchivePath = "/" + NormalizeEntryPath(entry.Name);
                result.Add((absoluteArchivePath, entryStream.ToArray()));
            }

            return result;
        }

        public void SetArchiveContent(byte[] archiveContent)
        {
            _archiveContent = archiveContent ?? Array.Empty<byte>();
            if (DataSource != null)
                DataSource.Content = _archiveContent;
        }

        public override List<string> GetDisplayableProperties()
            => [nameof(Name), nameof(DataSource)];

        public override void RegisterPropertyChangedEventHandlers()
        {
            if (DataSource != null)
                DataSource.AddDataSourceChangedListener(() => _archiveContent = null);

            if (!string.IsNullOrWhiteSpace(DataSource?.Name))
            {
                foreach (var field in FlowBloxFieldHelper.GetFieldElementsFromString(DataSource.Name))
                    field.OnValueChanged += (_, __, ___) => _archiveContent = null;
            }

            base.RegisterPropertyChangedEventHandlers();
        }
    }
}


