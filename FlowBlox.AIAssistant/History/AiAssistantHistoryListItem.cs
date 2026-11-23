namespace FlowBlox.AIAssistant.History
{
    public sealed class AiAssistantHistoryListItem
    {
        public Guid HistoryGuid { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public Guid ProjectGuid { get; set; }
        public string ComponentScope { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string Preview { get; set; } = string.Empty;
        public string DisplayTimestamp => UpdatedAt.LocalDateTime.ToString("dddd, dd.MM.yyyy HH:mm");
    }
}
