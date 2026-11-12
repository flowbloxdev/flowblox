using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Persistence;

[Display(Name = "CreateSqlLiteDbFlowBlock_DisplayName", Description = "CreateSqlLiteDbFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class CreateSqlLiteDbFlowBlock : BaseSingleResultFlowBlock
{
    [Required]
    [Display(Name = "CreateSqlLiteDbFlowBlock_DatabaseFile", Description = "CreateSqlLiteDbFlowBlock_DatabaseFile_Tooltip", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUI(Factory = UIFactory.Association, SelectionDisplayMember = nameof(FileObject.Name))]
    public FileObject DatabaseFile { get; set; }

    public override string DefaultResultFieldName => "ConnectionString";
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_plus_outline, 16, SKColors.SteelBlue);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_plus_outline, 32, SKColors.SteelBlue);
    public override FlowBlockCategory GetCategory() => FlowBlockCategory.Persistence;
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

    public override List<string> GetDisplayableProperties() => [.. base.GetDisplayableProperties(), nameof(DatabaseFile)];

    public override List<Type> NotificationTypes => [.. base.NotificationTypes, typeof(CreateSqlLiteDbNotifications)];

    public override bool Execute(BaseRuntime runtime, object data) => Invoke(runtime, data, () =>
    {
        try
        {
            var filePath = DatabaseFile?.GetRuntimeFilePath();
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ValidationException(FlowBloxResourceUtil.GetLocalizedString("CreateSqlLiteDbFlowBlock_PathRequired"));

            filePath = Path.GetFullPath(filePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            DbConnectionProvider.Instance.ReleaseSqliteConnections(filePath);
            File.Delete(filePath);
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                File.Delete(filePath + suffix);

            var options = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };
            using (var connection = new SqliteConnection(options.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "VACUUM;";
                command.ExecuteNonQuery();
            }

            options.Mode = SqliteOpenMode.ReadWrite;
            GenerateResult(runtime, options.ConnectionString);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or ValidationException)
        {
            CreateNotification(runtime, CreateSqlLiteDbNotifications.CreationFailed, exception);
            GenerateResult(runtime);
        }
    });

    public enum CreateSqlLiteDbNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "CreateSqlLiteDbFlowBlock_CreationFailed", ResourceType = typeof(FlowBloxTexts))]
        CreationFailed
    }
}
