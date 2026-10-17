using System.Reflection;
using System.Runtime.InteropServices;

// csc turns these into the Win32 version resource. Without them Windows has no
// display name for the exe and falls back to the bare filename ("zones").
// The version is not here: build.ps1 writes it from ..\VERSION into
// obj\Version.g.cs, so the Mac app and the release script read the same one.
[assembly: AssemblyTitle("Zones")]
[assembly: AssemblyProduct("Zones")]
[assembly: AssemblyDescription("Snap windows into your own zones")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: ComVisible(false)]
