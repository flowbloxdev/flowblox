namespace FlowBlox.AIAssistant.Constants
{
    public static class AssistantConfigurationLimits
    {
        public const int MinToolRounds = 1;
        public const int MaxToolRounds = 200;

        public const int MinLatestMessages = 1;
        public const int MaxLatestMessages = 50;

        public const double MinSummaryCompactionRate = 0.1d;
        public const double MaxSummaryCompactionRate = 1.0d;

        public const int DefaultMaxContextTokens = 64000;
        public const int MinContextTokens = 0;
        public const int MinReservedResponseTokens = 0;

        public const int MinApproximateCharactersPerToken = 1;
        public const int MaxApproximateCharactersPerToken = 20;
    }
}
