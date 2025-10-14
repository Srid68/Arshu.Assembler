using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;

var servers = new List<ServerConfig>
{
    new ServerConfig("C# AssemblerWeb", "csharp/AssemblerWeb", "dotnet run --skipIdleTracking", 5275),
    new ServerConfig("C# AssemblerWebJs", "csharp/AssemblerWebJs", "dotnet run --skipIdleTracking", 5000),
    new ServerConfig("Rust AssemblerWeb", "rust/AssemblerWeb", "cargo run --release -- --skipIdleTracking", 8090),
    new ServerConfig("Go AssemblerWeb", "go/AssemblerWeb", "go run . --skipIdleTracking", 8080),
    new ServerConfig("Node AssemblerWeb", "node/AssemblerWeb", "node index.js --skipIdleTracking", 8095),
    new ServerConfig("PHP AssemblerWeb", "php/AssemblerWeb", "php -S localhost:8085 index.php --skipIdleTracking", 8085)
};

Console.WriteLine("Starting all AssemblerWeb servers...\n");

foreach (var server in servers)
{
    try
    {
        var process = new Process();
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"[{server.Name}]\" cmd /k \"cd /d {server.WorkingDirectory} && {server.Command}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.StartInfo = startInfo;
        process.Start();

        // Give it a moment to spawn the actual window
        System.Threading.Thread.Sleep(500);

        Console.WriteLine($"✓ Started {server.Name} on port {server.Port}");
        Console.WriteLine($"  URL: http://localhost:{server.Port}");
        Console.WriteLine($"  Working Directory: {server.WorkingDirectory}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Failed to start {server.Name}: {ex.Message}\n");
    }
}

Console.WriteLine("\n" + new string('=', 80));
Console.WriteLine("All servers started successfully!");
Console.WriteLine(new string('=', 80));
Console.WriteLine("\nServer URLs:");
foreach (var server in servers)
{
    Console.WriteLine($"  {server.Name,-25} http://localhost:{server.Port}");
}
Console.WriteLine("\nUse 'dotnet web_stop.cs' to stop all servers.\n");

record ServerConfig(string Name, string WorkingDirectory, string Command, int Port);
