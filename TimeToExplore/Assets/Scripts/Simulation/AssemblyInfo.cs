using System.Runtime.CompilerServices;

// The editor-only route benchmark measures FlightSession's window build, so it
// times the real internal pair instead of a copy that can drift from it.
[assembly: InternalsVisibleTo("TimeToExplore.Editor")]
