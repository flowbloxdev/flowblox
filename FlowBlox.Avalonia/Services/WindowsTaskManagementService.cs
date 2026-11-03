using FlowBlox.Core.Enums;
using FlowBlox.Core.TaskManagement;
using FlowBlox.Core.Util;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using ThreadTask = System.Threading.Tasks.Task;
using SchedulerTask = Microsoft.Win32.TaskScheduler.Task;
using System.Threading.Tasks;

namespace FlowBlox.AvaloniaDesktop.Services
{
    internal sealed class WindowsTaskManagementService : ITaskManagementService
    {
        private const string FolderPath = @"\FlowBlox and contributors";
        private const string FolderName = "FlowBlox and contributors";
        private const string DescriptionPrefix = "FlowBloxScheduledTask:";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public Task<IReadOnlyList<FlowBloxScheduledTask>> GetTasksAsync(CancellationToken cancellationToken = default)
        {
            using var taskService = new TaskService();
            var folder = GetFolder(taskService, create: false);
            if (folder == null)
                return ThreadTask.FromResult<IReadOnlyList<FlowBloxScheduledTask>>(Array.Empty<FlowBloxScheduledTask>());

            var tasks = folder.Tasks
                .Where(IsFlowBloxTask)
                .Select(MapTask)
                .Where(x => x != null)
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ThreadTask.FromResult<IReadOnlyList<FlowBloxScheduledTask>>(tasks);
        }

        public ThreadTask CreateTaskAsync(FlowBloxScheduledTask task, CancellationToken cancellationToken = default)
        {
            RegisterTask(task);
            return ThreadTask.CompletedTask;
        }

        public ThreadTask UpdateTaskAsync(FlowBloxScheduledTask task, CancellationToken cancellationToken = default)
        {
            RegisterTask(task);
            return ThreadTask.CompletedTask;
        }

        public ThreadTask DeleteTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            using var taskService = new TaskService();
            var folder = GetFolder(taskService, create: false);
            folder?.DeleteTask(taskName, exceptionOnNotExists: false);
            return ThreadTask.CompletedTask;
        }

        public ThreadTask RunTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            GetTask(taskName)?.Run();
            return ThreadTask.CompletedTask;
        }

        public ThreadTask StopTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            GetTask(taskName)?.Stop();
            return ThreadTask.CompletedTask;
        }

        public System.Threading.Tasks.Task<bool> IsTaskRunningAsync(string taskName, CancellationToken cancellationToken = default)
        {
            return System.Threading.Tasks.Task.FromResult(GetTask(taskName)?.State == TaskState.Running);
        }

        public ThreadTask EnableTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            SetEnabled(taskName, true);
            return ThreadTask.CompletedTask;
        }

        public ThreadTask DisableTaskAsync(string taskName, CancellationToken cancellationToken = default)
        {
            SetEnabled(taskName, false);
            return ThreadTask.CompletedTask;
        }

        private static void RegisterTask(FlowBloxScheduledTask task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            ValidateTaskForRegistration(task);

            using var taskService = new TaskService();
            var folder = GetFolder(taskService, create: true);
            if (folder == null)
                throw new InvalidOperationException($"The Windows Task Scheduler folder '{FolderPath}' could not be opened or created.");

            var definition = taskService.NewTask();

            definition.RegistrationInfo.Author = "FlowBlox";
            definition.RegistrationInfo.Description = BuildDescription(task);
            definition.Principal.LogonType = TaskLogonType.InteractiveToken;
            definition.Principal.RunLevel = TaskRunLevel.LUA;
            definition.Settings.Enabled = task.IsEnabled;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
            definition.Settings.ExecutionTimeLimit = TimeSpan.Zero;

            AddTrigger(definition, task);

            var runnerHostCommand = RunnerHostResolver.Resolve();
            definition.Actions.Add(new ExecAction(
                runnerHostCommand.FileName,
                runnerHostCommand.BuildArguments(task.RequestFilePath, task.ResponseFilePathTemplate),
                AppContext.BaseDirectory));

            folder.RegisterTaskDefinition(task.TaskName.Trim(), definition);
        }

        private static void ValidateTaskForRegistration(FlowBloxScheduledTask task)
        {
            if (string.IsNullOrWhiteSpace(task.TaskName))
                throw new InvalidOperationException("The scheduled task has no task name.");

            if (!IsValidTaskName(task.TaskName))
                throw new InvalidOperationException($"The scheduled task name '{task.TaskName}' contains characters that are not supported by Windows Task Scheduler.");

            if (string.IsNullOrWhiteSpace(task.RequestFilePath))
                throw new InvalidOperationException($"The scheduled task '{task.TaskName}' has no request file path.");

            if (string.IsNullOrWhiteSpace(task.ResponseFilePathTemplate))
                throw new InvalidOperationException($"The scheduled task '{task.TaskName}' has no response file path.");
        }

        private static bool IsValidTaskName(string taskName)
        {
            if (string.IsNullOrWhiteSpace(taskName))
                return false;

            var trimmed = taskName.Trim();
            return string.Equals(taskName, trimmed, StringComparison.Ordinal) &&
                string.Equals(Path.GetFileName(taskName), taskName, StringComparison.Ordinal) &&
                trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static void AddTrigger(TaskDefinition definition, FlowBloxScheduledTask task)
        {
            switch (task.ScheduleType)
            {
                case FlowBloxTaskScheduleType.AtStartup:
                    definition.Triggers.Add(new BootTrigger
                    {
                        Enabled = true
                    });
                    break;
                case FlowBloxTaskScheduleType.Daily:
                    definition.Triggers.Add(new DailyTrigger
                    {
                        Enabled = true,
                        StartBoundary = task.StartAt ?? DateTime.Today.AddHours(8),
                        DaysInterval = 1
                    });
                    break;
                case FlowBloxTaskScheduleType.Interval:
                    var trigger = new TimeTrigger
                    {
                        Enabled = true,
                        StartBoundary = task.StartAt ?? DateTime.Now.AddMinutes(5)
                    };
                    trigger.Repetition.Interval = task.Interval ?? TimeSpan.FromHours(1);
                    definition.Triggers.Add(trigger);
                    break;
                case FlowBloxTaskScheduleType.Manual:
                default:
                    break;
            }
        }

        private static TaskFolder GetFolder(TaskService taskService, bool create)
        {
            var folder = TryGetFolder(taskService);
            if (folder != null || !create)
                return folder;

            taskService.RootFolder.CreateFolder(FolderName, sddlForm: null, exceptionOnExists: false);

            folder = TryGetFolder(taskService);
            if (folder != null)
                return folder;

            throw new UnauthorizedAccessException($"The Windows Task Scheduler folder '{FolderPath}' could not be opened or created. Please start FlowBlox as administrator and try again.");
        }

        private static TaskFolder TryGetFolder(TaskService taskService)
        {
            try
            {
                return taskService.GetFolder(FolderPath);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (COMException exception) when ((uint)exception.HResult == 0x80070002 || (uint)exception.HResult == 0x80070003)
            {
                return null;
            }
        }

        private static SchedulerTask GetTask(string taskName)
        {
            using var taskService = new TaskService();
            var folder = GetFolder(taskService, create: false);
            return folder?.Tasks.FirstOrDefault(x => string.Equals(x.Name, taskName, StringComparison.OrdinalIgnoreCase));
        }

        private static void SetEnabled(string taskName, bool enabled)
        {
            using var taskService = new TaskService();
            var folder = GetFolder(taskService, create: false);
            var task = folder?.Tasks.FirstOrDefault(x => string.Equals(x.Name, taskName, StringComparison.OrdinalIgnoreCase));
            if (task == null)
                return;

            task.Enabled = enabled;
        }

        private static bool IsFlowBloxTask(SchedulerTask task)
        {
            return task?.Definition?.RegistrationInfo?.Description?.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static FlowBloxScheduledTask MapTask(SchedulerTask task)
        {
            var model = ParseDescription(task.Definition.RegistrationInfo.Description);
            if (model == null)
                return null;

            model.TaskName = task.Name;
            model.IsEnabled = task.Enabled;
            model.NextRunTime = task.NextRunTime == DateTime.MinValue ? null : task.NextRunTime;
            model.LastRunTime = task.LastRunTime == DateTime.MinValue ? null : task.LastRunTime;
            model.LastResult = task.LastTaskResult;
            model.IsRunning = task.State == TaskState.Running;

            ApplyTrigger(model, task.Definition.Triggers.Cast<Trigger>().FirstOrDefault());
            return model;
        }

        private static void ApplyTrigger(FlowBloxScheduledTask model, Trigger trigger)
        {
            if (trigger == null)
            {
                model.ScheduleType = FlowBloxTaskScheduleType.Manual;
                return;
            }

            model.StartAt = trigger.StartBoundary;

            if (trigger is BootTrigger)
            {
                model.ScheduleType = FlowBloxTaskScheduleType.AtStartup;
                return;
            }

            if (trigger is DailyTrigger)
            {
                model.ScheduleType = FlowBloxTaskScheduleType.Daily;
                return;
            }

            if (trigger.Repetition?.Interval > TimeSpan.Zero)
            {
                model.ScheduleType = FlowBloxTaskScheduleType.Interval;
                model.Interval = trigger.Repetition.Interval;
                return;
            }

            model.ScheduleType = FlowBloxTaskScheduleType.Manual;
        }

        private static string BuildDescription(FlowBloxScheduledTask task)
        {
            return DescriptionPrefix + JsonSerializer.Serialize(task, JsonOptions);
        }

        private static FlowBloxScheduledTask ParseDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description) ||
                !description.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var json = description[DescriptionPrefix.Length..];
            return JsonSerializer.Deserialize<FlowBloxScheduledTask>(json, JsonOptions);
        }
    }
}
