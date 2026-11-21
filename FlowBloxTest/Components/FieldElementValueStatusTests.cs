using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.DeepCopier;
using SkiaSharp;

namespace FlowBloxTest.Components;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FieldElementValueStatusTests
{
    [TestMethod]
    public void RepeatedEditorCommits_NotifyStatusWithCurrentStartupRequirement()
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            IsRequiredAtRuntimeStartup = true
        };
        SKImage? lastNotifiedIcon = null;
        bool? lastNotifiedRequirement = null;
        field.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FieldElement.ValueStatusIcon))
            {
                lastNotifiedIcon = field.ValueStatusIcon;
                lastNotifiedRequirement = field.IsRequiredAtRuntimeStartup;
            }
        };

        foreach (var required in new[] { false, true, false, true })
        {
            var copier = new DynamicDeepCopier();
            var edited = (FieldElement)copier.Copy(field);
            edited.IsRequiredAtRuntimeStartup = required;
            lastNotifiedIcon = null;
            lastNotifiedRequirement = null;

            copier.SpecialMode = DeepCopierSpecialMode.Recopy;
            copier.Copy(edited, field);

            Assert.AreEqual(required, lastNotifiedRequirement);
            Assert.IsNotNull(lastNotifiedIcon);
            Assert.AreEqual(field.ValueStatusIcon.UniqueId, lastNotifiedIcon.UniqueId);
        }
    }
}
