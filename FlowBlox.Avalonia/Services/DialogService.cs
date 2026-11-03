using FlowBlox.Core.Attributes;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Models.DialogService;
using FlowBlox.UICore.Views;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using System.Collections;
using System.Windows;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public class DialogService : IDialogService
    {
        public bool? ShowWPFDialog(Window window, bool isModal = true)
        {
            var ownerService = FlowBloxServiceLocator.Instance.GetService<IOwnerService>();
            var owner = ownerService.GetCurrentOwner();
            return WpfDialogHost.ShowDialog(window, owner);
        }

        public InsertTextOrFieldResult InvokeInsertTextOrField(BaseFlowBlock flowBlock, string parameterName, Window window)
        {
            var insertTextOrFieldDialog = new InsertTextOrFieldWindow(flowBlock, parameterName)
            {
                Owner = window,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (insertTextOrFieldDialog.ShowDialog() == true)
            {
                return new InsertTextOrFieldResult
                {
                    Success = true,
                    InsertedValue = insertTextOrFieldDialog.SelectedValue,
                    IsSelectedFieldRequired = insertTextOrFieldDialog.IsSelectedFieldRequired(),
                    SelectedField = insertTextOrFieldDialog.GetSelectedField()
                };
            }

            return new InsertTextOrFieldResult();
        }

        public EditValueResult InvokeEditValue(EditValueRequest editValueRequest, Window window)
        {
            var editValueWindow = new EditValueWindow(editValueRequest.Value, editValueRequest.IsRegex, editValueRequest.IsMultiline)
            {
                Owner = window,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            editValueWindow.SetMode(editValueRequest.EditMode);
            editValueWindow.SetParameterName(editValueRequest.ParameterName.Trim('%'));

            if (editValueWindow.ShowDialog() == true)
            {
                return new EditValueResult
                {
                    Success = true,
                    Value = editValueWindow.GetValue(),
                    IsMaskedRegexString = editValueWindow.IsMaskedRegexString()
                };
            }

            return new EditValueResult();
        }
    }
}