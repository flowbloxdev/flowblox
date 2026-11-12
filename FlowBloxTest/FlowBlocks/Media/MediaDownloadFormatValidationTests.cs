using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Downloads;
using FlowBloxTest.Services;

namespace FlowBloxTest.FlowBlocks.Media
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class MediaDownloadFormatValidationTests
    {
        [TestMethod]
        public void UnsupportedFormat_IsRejectedByDataAnnotations()
        {
            var block = new MediaDownloadFlowBlock
            {
                Provider = new RecordingMediaDownloadProvider
                {
                    FormatSupport = format => format == MediaDownloadFormat.Mp3
                },
                Format = MediaDownloadFormat.Opus
            };
            var errors = new List<ValidationResult>();

            var valid = Validator.TryValidateProperty(
                block.Format,
                new ValidationContext(block) { MemberName = nameof(block.Format) },
                errors);

            Assert.IsFalse(valid);
            Assert.HasCount(1, errors);
            CollectionAssert.Contains(errors[0].MemberNames.ToArray(), nameof(block.Format));
        }

        [TestMethod]
        public void ChangingProvider_RevalidatesPreviouslySupportedFormat()
        {
            var block = new MediaDownloadFlowBlock
            {
                Provider = new RecordingMediaDownloadProvider(),
                Format = MediaDownloadFormat.Opus
            };
            var context = new ValidationContext(block) { MemberName = nameof(block.Format) };
            Assert.IsTrue(Validator.TryValidateProperty(block.Format, context, []));

            block.Provider = new RecordingMediaDownloadProvider { FormatSupport = _ => false };

            Assert.IsFalse(Validator.TryValidateProperty(block.Format, context, []));
        }

        [TestMethod]
        [DataRow(MediaDownloadFormat.Mp3, true)]
        [DataRow(MediaDownloadFormat.Opus, false)]
        public void SelectedFormatField_IsValidatedInsteadOfConfiguredLiteral(MediaDownloadFormat selectedFormat, bool expectedValid)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "OutputFormat");
            field.StringValue = selectedFormat.ToString();
            var block = new MediaDownloadFlowBlock
            {
                Provider = new RecordingMediaDownloadProvider
                {
                    FormatSupport = format => format == MediaDownloadFormat.Mp3
                },
                Format = MediaDownloadFormat.Video
            };
            block.SetPropertySelectedField(nameof(block.Format), field);

            var valid = Validator.TryValidateProperty(
                block.Format,
                new ValidationContext(block) { MemberName = nameof(block.Format) },
                []);

            Assert.AreEqual(expectedValid, valid);
            Assert.AreEqual(MediaDownloadFormat.Video, block.Format);
        }

        [TestMethod]
        public async Task UnsupportedFormat_DoesNotStartProviderWhenCalledWithoutFlowBlockValidation()
        {
            var provider = new RecordingMediaDownloadProvider
            {
                FormatSupport = format => format == MediaDownloadFormat.Mp3
            };

            await Assert.ThrowsAsync<ValidationException>(() => provider.DownloadAsync(
                new MediaDownloadRequest
                {
                    Url = "https://example.test/podcast.mp3",
                    OutputDirectory = Path.GetTempPath(),
                    Format = MediaDownloadFormat.Opus
                },
                new TransferProgressReporter(_ => { }),
                CancellationToken.None));

            Assert.AreEqual(0, provider.InvocationCount);
        }
    }
}
