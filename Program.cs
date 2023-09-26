using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GoXLRProfileSwitcher;
public class Program
{
    public static void Main(string[] args)
    {
        // Check if there are enough arguments
        if (args.Length < 4)
        {
            Console.WriteLine("Not enough arguments provided.");
            return;
        }

        // Parse the command-line arguments
        var device = "";
        var profile = "";

        for (int i = 0; i < args.Length; i += 2)
        {
            string arg = args[i];
            string value = args[i + 1];

            switch (arg)
            {
                case "--device":
                case "-d":
                    device = value;
                    break;
                case "--profile":
                case "-p":
                    profile = value;
                    break;
                default:
                    Console.WriteLine($"Unknown argument: {arg}");
                    return;
            }
        }

        // Check if the required arguments are provided
        if (string.IsNullOrEmpty(device) ||
            string.IsNullOrWhiteSpace(profile))
        {
            Console.WriteLine("Missing required arguments.");
            return;
        }

        Console.WriteLine($"Checking for goxlr-daemon...");
        if (!IsDaemonProcessRunning() && !IsUserServiceRunning())
        {
            Console.WriteLine($"Failed to find goxlr-daemon");
            return;
        }

        var clientSocket = new Socket(
                AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        clientSocket.Connect(new UnixDomainSocketEndPoint("/tmp/goxlr.socket"));

        var command = new GoXLRCommand
        {
            Command = new List<object>
            {
                $"{device}",
                new Dictionary<string, object>
                {
                    {
                        "LoadProfile", new List<object>
                        {
                            $"{profile}",
                            true
                        }
                    }
                }
            }
        };

        var commandString = JsonSerializer.Serialize(command);

        // Grab the Message Bytes, and the bytes length..
        var messageBytes = Encoding.UTF8.GetBytes(commandString);
        var messageLength = BitConverter.GetBytes((Int32)messageBytes.Length);

        // If we're on a little endian system, we need to reverse the length to BigEndian
        if (BitConverter.IsLittleEndian) Array.Reverse(messageLength);

        clientSocket.Send(messageLength);
        clientSocket.Send(messageBytes);

        var lengthBytes = new byte[4];
        var bytesRead = clientSocket.Receive(lengthBytes, 0, 4, SocketFlags.None);

        if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);

        if (bytesRead != 4)
        {
            Console.WriteLine($"Failed to read response length");
            return;
        }

        var responseLength = BitConverter.ToInt32(lengthBytes, 0);

        Console.WriteLine($"Response Length is: {responseLength}");

        var responseData = new byte[responseLength];
        bytesRead = clientSocket.Receive(
                responseData, 0, responseLength, SocketFlags.None);

        if (bytesRead != responseLength)
        {
            Console.WriteLine($"Error reading message");
            return;
        }

        var responseMessage = Encoding.UTF8.GetString(responseData);

        Console.WriteLine($"Got Response: {responseMessage}");
    }

    public static bool IsDaemonProcessRunning()
    {
        return (Process.GetProcessesByName("goxlr-daemon")).Length != 0;
    }

    public static bool IsUserServiceRunning()
    {
        var process = new Process();
        process.StartInfo.FileName = "systemctl";
        process.StartInfo.Arguments = "--user status goxlr-daemon.service";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output.Contains("Active: active");
    }
}
