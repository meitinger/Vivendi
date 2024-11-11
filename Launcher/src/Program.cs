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

using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Security.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace AufBauWerk.Vivendi.Launcher;

static class Program
{
    private static readonly ManualResetEvent LoginReadyEvent = new(false);

    public static bool IsSigned(FileStream file)
    {
        FileSignatureInfo info = FileSignatureInfo.GetFromFileStream(file);
        return
            info.State is SignatureState.SignedAndTrusted &&
            string.Equals(info.SigningCertificate.Thumbprint, "BF0993A78683E1FF5C86C18B1A3CD1CDF53C729A", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunSplashScreen(object? _)
    {
        // display the splash screen
        SplashScreen splashScreen = new(@"res\SplashScreen.png");
        splashScreen.Show(autoClose: false);
        LoginReadyEvent.WaitOne();
        splashScreen.Close(TimeSpan.Zero);
    }

    public static MessageBoxResult ShowMessage(string text, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult = MessageBoxResult.None) => MessageBox.Show(text, "Vivendi Launcher", button, icon, defaultResult);

    private static bool ShowRunningInstance()
    {
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        foreach (Process vivendi in Process.GetProcessesByName("Vivendi"))
        {
            if (vivendi.SessionId == currentSessionId)
            {
                return vivendi.MainWindowHandle.SetForeground();
            }
        }
        return false;
    }

    private static async Task SingleSignOnAsync(Process vivendi, Credentials credentials, CancellationToken cancellationToken)
    {
        for (int numberOfTry = 0; numberOfTry < 600; numberOfTry++)
        {
            foreach (ProcessThread thread in vivendi.Threads)
            {
                nint loginWindow = 0;
                thread.EnumerateWindows((window, e) =>
                {
                    if (window.GetText() is "Login")
                    {
                        loginWindow = window;
                        e.Cancel = true;
                    }
                });
                if (loginWindow is 0) { continue; }
                LoginReadyEvent.Set();
                nint userNameWindow = 0, passwordWindow = 0, buttonWindow = 0;
                loginWindow.EnumerateTree((window, e) =>
                {
                    if (!window.IsVisible()) { return; }
                    if (window.GetClassName() is not string className) { return; }
                    string[] classNameParts = className.Split('.');
                    if (classNameParts.Length < 2 || classNameParts[0] is not "WindowsForms10") { return; }
                    switch (classNameParts[1])
                    {
                        case "Button":
                            if (window.GetText() is "OK") { buttonWindow = window; }
                            break;
                        case "Edit":
                            if (window.IsPassword()) { passwordWindow = window; }
                            else { userNameWindow = window; }
                            break;
                        default:
                            return;
                    }
                    if (FoundAll()) { e.Cancel = true; }
                });
                if (!FoundAll()) { continue; }
                if (!userNameWindow.SetText(credentials.UserName)) { continue; }
                if (!passwordWindow.SetText(credentials.Password)) { continue; }
                buttonWindow.Click();
                return;

                bool FoundAll() => userNameWindow is not 0 && passwordWindow is not 0 && buttonWindow is not 0;
            }
            await Task.Delay(100, cancellationToken);
        }
        vivendi.ShowError("Die automatische Anmeldung konnte nicht durchgeführt werden.");
    }

    public static async Task Main()
    {
        using CancellationTokenSource cts = new();
        try
        {
            // perform minimal initialization
            ThreadPool.QueueUserWorkItem(RunSplashScreen);
            Settings.Load();

            // verify the Vivendi executable and maintain a lock
            string vivendiPath = Path.Combine(AppContext.BaseDirectory, Settings.Instance.VivendiPath);
            using FileStream vivendiStream = new(vivendiPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!IsSigned(vivendiStream))
            {
                ShowMessage("Die Signatur der Vivendi-Anwendung ist ungültig.", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // create the process instance (but don't start it yet)
            using Process process = new()
            {
                StartInfo = new()
                {
                    FileName = vivendiPath,
                    UseShellExecute = false,
                },
                EnableRaisingEvents = true,
            };
            process.Exited += (s, e) => cts.Cancel();

            // create the local app using WAM
            IPublicClientApplication app = PublicClientApplicationBuilder
                .Create(Settings.Instance.ApplicationId)
                .WithTenantId(Settings.Instance.TenantId)
                .WithDefaultRedirectUri()
                .WithDesktopAsParent()
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Vivendi-Anmeldung" })
                .Build();

            // setup a token cache in %APPDATA%
            MsalCacheHelper cache = await MsalCacheHelper.CreateAsync(new StorageCreationPropertiesBuilder(
                cacheFileName: "token.cache",
                cacheDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VivendiLauncher")
            ).Build());
            cache.RegisterCache(app.UserTokenCache);

            // open the local TCP server socket
            using Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 1433));
                listener.Listen();
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse)
            {
                if (!ShowRunningInstance())
                {
                    ShowMessage(ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            // authenticate with Entra ID and fetch the credentials
            using Credentials? credentials = await app.LoginAsync(cts.Token);
            if (credentials is null) { return; }

            // launch Vivendi
            process.Start();

            // forward SQL traffic and sign in to Vivendi
            ConcurrentDictionary<Task, string> tasks = new();
            RegisterTask(listener.RunServerAsync(credentials, process, (task, client) => RegisterTask(task, $"Client({client.RemoteEndPoint})"), cts.Token), "Server");
            RegisterTask(SingleSignOnAsync(process, credentials, cts.Token), "Login");
            while (!tasks.IsEmpty)
            {
                using Task task = await Task.WhenAny(tasks.Keys);
                tasks.Remove(task, out string? name);
                if (task.Exception is null)
                {
                    Console.WriteLine($"[Task] {name} completed");
                }
                else
                {
                    Console.WriteLine($"[Task] {name} failed: {task.Exception}");
                    process.ShowError(task.Exception.Message);
                }
            }

            // helper function
            void RegisterTask(Task task, string name)
            {
                if (!tasks.TryAdd(task, name))
                {
                    throw new ArgumentException($"Task '{name}' wurde bereits hinzugefügt.", nameof(task));
                }
                Console.WriteLine($"[Task] {name} registered");
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            ShowMessage(ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
