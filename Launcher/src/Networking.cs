/*
 * AufBauWerk Erweiterungen für Vivendi
 * Copyright (C) 2024  Manuel Meitinger
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace AufBauWerk.Vivendi.Launcher;

public static partial class Extensions
{
    private static async Task RunClientAsync(Socket client, Credentials credentials, CancellationToken cancellationToken)
    {
        EndPoint? endPoint = client.RemoteEndPoint;
        try
        {
            X509Certificate2 certificate = await credentials.GetCertificateAsync(cancellationToken);
            using NetworkStream clientStream = new(client, ownsSocket: true);
            using Socket server = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            server.EnableTcpKeepAlive(idleTime: TimeSpan.FromSeconds(30), interval: TimeSpan.FromSeconds(1));
            await server.ConnectAsync(Settings.Instance.Database.HostName, Settings.Instance.Database.Port);
            Console.WriteLine($"[{endPoint}] connected to server");
            using NetworkStream innerServerStream = new(server, ownsSocket: true);
            using SslStream serverStream = new(innerServerStream, leaveInnerStreamOpen: false);
            await serverStream.AuthenticateAsClientAsync(new()
            {
                TargetHost = Settings.Instance.Database.HostName,
                ClientCertificates = new() { certificate },
            }, cancellationToken);
            Console.WriteLine($"[{endPoint}] authenticated with server (mutually={serverStream.IsMutuallyAuthenticated})");
            await Task.WhenAll(UpAsync(), DownAsync());

            async Task UpAsync()
            {
                byte[] buffer = new byte[81920];
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await clientStream.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read is 0)
                    {
                        Console.WriteLine($"[{endPoint}] end of client stream");
                        await serverStream.ShutdownAsync();
                        break;
                    }
                    await serverStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            async Task DownAsync()
            {
                byte[] buffer = new byte[81920];
                while (!cancellationToken.IsCancellationRequested)
                {
                    int read = await serverStream.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read is 0)
                    {
                        Console.WriteLine($"[{endPoint}] end of server stream");
                        client.Shutdown(SocketShutdown.Send);
                        break;
                    }
                    await clientStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
        }
        catch (SocketException ex) { Console.WriteLine($"[{endPoint}] socket exception: {ex.Message}"); }
        catch (IOException ex) when (ex.InnerException is SocketException) { Console.WriteLine($"[{endPoint}] IO exception: {ex.Message}"); }
        finally { client.Dispose(); }
    }

    public static async Task RunServerAsync(this Socket server, Credentials credentials, Process vivendi, Action<Task, Socket> registerClient, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client = await server.AcceptAsync(cancellationToken);
            try
            {
                int processId = client.GetRemoteProcessId();
                if (processId is 0)
                {
                    Fail($"Nicht identifiziertes Netzwerk {client.RemoteEndPoint}");
                    continue;
                }
                if (processId != vivendi.Id)
                {
                    Process process;
                    try { process = Process.GetProcessById(processId); }
                    catch (ArgumentException)
                    {
                        Fail($"Prozess #{processId}");
                        continue;
                    }
                    if (process.MainModule?.FileName is not string path)
                    {
                        Fail($"'{process.ProcessName}'");
                        continue;
                    }
                    using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (!Program.IsSigned(file))
                    {
                        Fail($"'{path}'");
                        continue;
                    }
                }
            }
            catch
            {
                client.Dispose();
                throw;
            }
            registerClient(RunClientAsync(client, credentials, cancellationToken), client);

            void Fail(string source)
            {
                client.Dispose();
                vivendi.ShowWarning($"{source} hat versucht auf die Datenbank zuzugreifen.");
            }
        }
    }
}
