using System.Text;

namespace AirportSmith.Models;

// The six sign-type letters of the SDK's taxiway sign grammar. Colors per the
// SDK's Scenery Editor "TaxiwaySign Objects" page: Location yellow on black;
// Direction/Information/Unknown black on yellow; Mandatory/Runway white on red.
public enum TaxiwaySignSegmentKind
{
    Location,
    Direction,
    Mandatory,
    Information,
    Runway,
    Unknown,
}

// One run of a sign label: its type letter's kind plus the raw content
// characters after it (brackets included). DisplayText is what the diagram
// and the grid preview draw — brackets dropped (they only add a border in the
// sim), arrow characters swapped for real arrows.
public sealed record TaxiwaySignSegment(TaxiwaySignSegmentKind Kind, string Content)
{
    public string DisplayText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var c in Content)
            {
                switch (c)
                {
                    case '[' or ']': break;
                    case '_': sb.Append(' '); break;
                    case '>': sb.Append('→'); break;
                    case '<': sb.Append('←'); break;
                    case '^': sb.Append('↑'); break;
                    case 'v': sb.Append('↓'); break;
                    case '\'': sb.Append('↗'); break;
                    case '`': sb.Append('↖'); break;
                    case '/': sb.Append('↙'); break;
                    case '\\': sb.Append('↘'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}

