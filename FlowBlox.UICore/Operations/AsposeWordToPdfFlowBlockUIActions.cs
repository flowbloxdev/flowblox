using FlowBlox.Core.Constants;
using AsposeWordToPdfFlowBlockUIActionsResources = FlowBlox.UICore.Resources.ClassResources.AsposeWordToPdfFlowBlockUIActions;
using FlowBlox.Core;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Windows.Forms;

namespace FlowBlox.Grid.Elements.UI.CustomActions
{
    public class AsposeWordToPdfFlowBlockUIActions : ComponentUIActions<AsposeWordToPdfFlowBlock>
    {
        private readonly IFlowBloxMessageBoxService _messageBoxService;

        public AsposeWordToPdfFlowBlockUIActions(AsposeWordToPdfFlowBlock component) : base(component)
        {
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
        }

        public SKImage ImportLicenseIcon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.file_import, 16, SKColors.MediumSeaGreen);

        [Display(Name = "AsposeWordToPdfFlowBlockUIActions_ImportLicense", ResourceType = typeof(FlowBloxTexts))]
        public void ImportLicense()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Aspose license (*.lic;*.xml)|*.lic;*.xml|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                var options = FlowBloxOptions.GetOptionInstance();
                options.OptionCollection[OptionsConstants.AsposeWordsLicenseBase64].Value =
                    Convert.ToBase64String(File.ReadAllBytes(dialog.FileName));
                options.Save();

                _messageBoxService.ShowMessageBox(
                    FlowBloxResourceUtil.GetLocalizedString("ImportLicense_Success", typeof(AsposeWordToPdfFlowBlockUIActionsResources)),
                    FlowBloxResourceUtil.GetLocalizedString("ImportLicense_Title", typeof(AsposeWordToPdfFlowBlockUIActionsResources)),
                    FlowBloxMessageBoxTypes.Information);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                _messageBoxService.ShowMessageBox(
                    FlowBloxResourceUtil.GetLocalizedString("ImportLicense_Failed", typeof(AsposeWordToPdfFlowBlockUIActionsResources)),
                    FlowBloxResourceUtil.GetLocalizedString("ImportLicense_Title", typeof(AsposeWordToPdfFlowBlockUIActionsResources)),
                    FlowBloxMessageBoxTypes.Error);
            }
        }
    }
}
