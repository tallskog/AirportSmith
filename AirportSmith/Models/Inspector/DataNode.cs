namespace AirportSmith.Models.Inspector;

// One node in the raw-data tree shown on the "Airport Data" tab — either a
// leaf (Text already carries "Name: value", Children empty) or a composite
// (Text is just the member/item name, Children populated), built by
// AirportDataTreeBuilder via reflection over AirportDetails and everything
// it references. A single bindable Text string (rather than separate
// Name/Value properties) keeps the TreeView's DataTemplate a one-liner.
// Plain, WPF-agnostic (built once per load, not mutated in place), so it's
// testable without a TreeView.
public class DataNode
{
    public required string Text { get; init; }
    public IReadOnlyList<DataNode> Children { get; init; } = [];
}
