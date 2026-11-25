using System.Runtime.CompilerServices;

// The editor-only route benchmark measures FlightSession's window build, so it
// times the real internal pair instead of a copy that can drift from it. The
// EditMode suite reads the same diagnostics to show what prefetch filled and
// what the drag had to build.
[assembly: InternalsVisibleTo("TimeToExplore.Editor")]
[assembly: InternalsVisibleTo("TimeToExplore.Tests.EditMode")]
