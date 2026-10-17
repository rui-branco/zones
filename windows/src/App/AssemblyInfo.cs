using System.Reflection;
using System.Runtime.InteropServices;

// csc turns these into the Win32 version resource. Without them Windows has no
// display name for the exe and falls back to the bare filename ("zones").
[assembly: AssemblyTitle("Zones")]
[assembly: AssemblyProduct("Zones")]
[assembly: AssemblyDescription("Snap windows into your own zones")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: ComVisible(false)]
