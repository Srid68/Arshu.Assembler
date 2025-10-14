using System;
using System.Diagnostics;
using System.Linq;

Console.WriteLine("Stopping all AssemblerWeb servers...\n");

int totalKilled = 0;

// Kill cmd.exe windows with server titles
try
{
    var cmdProcesses = Process.GetProcessesByName("cmd");
    foreach (var proc in cmdProcesses)
    {
        try
        {
            if (proc.MainWindowTitle.Contains("C# AssemblerWeb") ||
                proc.MainWindowTitle.Contains("Rust AssemblerWeb") ||
                proc.MainWindowTitle.Contains("Go AssemblerWeb") ||
                proc.MainWindowTitle.Contains("Node AssemblerWeb") ||
                proc.MainWindowTitle.Contains("PHP AssemblerWeb"))
            {
                Console.WriteLine($"✓ Stopping: {proc.MainWindowTitle}");
                proc.Kill(true);
                totalKilled++;
            }
        }
        catch { }
    }
}
catch { }

// Kill all dotnet processes (simplified - kills all dotnet processes)
try
{
    var dotnetProcesses = Process.GetProcessesByName("dotnet");
    foreach (var proc in dotnetProcesses)
    {
        try
        {
            // Skip the current process
            if (proc.Id != Environment.ProcessId)
            {
                Console.WriteLine($"✓ Stopping dotnet process: {proc.Id}");
                proc.Kill(true);
                totalKilled++;
            }
        }
        catch { }
    }
}
catch { }

// Kill Rust assembler_web processes
try
{
    var rustProcesses = Process.GetProcessesByName("assembler_web");
    foreach (var proc in rustProcesses)
    {
        try
        {
            Console.WriteLine($"✓ Stopping Rust AssemblerWeb: {proc.Id}");
            proc.Kill(true);
            totalKilled++;
        }
        catch { }
    }
}
catch { }

// Kill Go assemblerweb processes
try
{
    var goProcesses = Process.GetProcessesByName("assemblerweb");
    foreach (var proc in goProcesses)
    {
        try
        {
            Console.WriteLine($"✓ Stopping Go AssemblerWeb: {proc.Id}");
            proc.Kill(true);
            totalKilled++;
        }
        catch { }
    }
}
catch { }

// Kill all Node processes
try
{
    var nodeProcesses = Process.GetProcessesByName("node");
    foreach (var proc in nodeProcesses)
    {
        try
        {
            Console.WriteLine($"✓ Stopping Node process: {proc.Id}");
            proc.Kill(true);
            totalKilled++;
        }
        catch { }
    }
}
catch { }

// Kill all PHP processes
try
{
    var phpProcesses = Process.GetProcessesByName("php");
    foreach (var proc in phpProcesses)
    {
        try
        {
            Console.WriteLine($"✓ Stopping PHP process: {proc.Id}");
            proc.Kill(true);
            totalKilled++;
        }
        catch { }
    }
}
catch { }

Console.WriteLine($"\n{new string('=', 80)}");
if (totalKilled > 0)
{
    Console.WriteLine($"Successfully stopped {totalKilled} server process(es).");
}
else
{
    Console.WriteLine("No running server processes found.");
}
Console.WriteLine(new string('=', 80) + "\n");
