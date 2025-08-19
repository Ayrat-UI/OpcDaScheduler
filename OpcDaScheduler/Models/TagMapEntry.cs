namespace OpcDaScheduler
{
    public class TagMapEntry
    {
        public string OpcItemId { get; set; } = "";
        public string Alias { get; set; } = "";
        public int TargetTagId { get; set; }   // ID тега в вашей БД
        public string Formula { get; set; } = "x";
    }
}
