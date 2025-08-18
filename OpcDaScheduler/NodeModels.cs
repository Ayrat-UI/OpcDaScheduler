using System;
using System.Collections.ObjectModel;

namespace OpcDaScheduler
{
    // Узел дерева для XAML
    public class Node
    {
        public string Name { get; set; } = "";
        public string? ItemId { get; set; }              // если не null — это лист
        public bool IsLeaf => ItemId != null;
        public bool IsChecked { get; set; }              // галка для листьев
        public ObservableCollection<Node> Children { get; } = new();
    }

    // Строка результата чтения
    public class ReadRow
    {
        public string Tag { get; set; } = "";
        public string? Value { get; set; }
        public short Quality { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
