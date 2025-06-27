using Firebase.Database;
using Firebase.Database.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FirebaseAdminPanel
{
    public partial class MainWindow : Window
    {
        private FirebaseClient firebase;
        private const int OnlineTimeoutSeconds = 20; // если lastSeen обновлялся за последние 20 сек — ПК online
        private DispatcherTimer refreshTimer;

        public MainWindow()
        {
            InitializeComponent();
            firebase = new FirebaseClient("https://test-qstem-default-rtdb.firebaseio.com/");
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshMachinesList();
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromSeconds(10);
            refreshTimer.Tick += async (s, ev) => await RefreshMachinesList();
            refreshTimer.Start();
        }

        private async Task RefreshMachinesList()
        {
            try
            {
                var machines = await firebase.Child("machines").OnceAsync<dynamic>();
                var now = DateTime.UtcNow;
                var onlineMachines = new List<string>();

                foreach (var m in machines)
                {
                    string name = m.Key;
                    var lastSeenStr = m.Object?.lastSeen;
                    if (lastSeenStr != null)
                    {
                        DateTime lastSeen;
                        if (DateTime.TryParse(lastSeenStr.ToString(), out lastSeen))
                        {
                            if ((now - lastSeen).TotalSeconds < OnlineTimeoutSeconds)
                                onlineMachines.Add(name);
                        }
                    }
                }

                var prev = MachineComboBox.SelectedItem as string;
                MachineComboBox.ItemsSource = onlineMachines;
                if (onlineMachines.Any())
                {
                    if (prev != null && onlineMachines.Contains(prev))
                        MachineComboBox.SelectedItem = prev;
                    else
                        MachineComboBox.SelectedIndex = 0;
                }
                else
                {
                    MachineComboBox.SelectedIndex = -1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке списка ПК: " + ex.Message);
            }
        }

        private async void SendCommand_Click(object sender, RoutedEventArgs e)
        {
            string command = CommandBox.Text.Trim();
            string machine = MachineComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(machine))
            {
                MessageBox.Show("Выберите ПК и введите команду!");
                return;
            }

            ResultBlock.Text = "Отправка команды...";
            try
            {
                await firebase.Child("machines").Child(machine).Child("cmd").PutAsync<string>(command);
                ResultBlock.Text = "Ожидание результата...";

                string result = "";
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(1000);
                    result = await firebase.Child("machines").Child(machine).Child("result").OnceSingleAsync<string>();
                    if (!string.IsNullOrEmpty(result))
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                        MessageBox.Show($"[{machine}]\nКоманда выполнена агентом!", "Уведомление", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(result))
                {
                    ResultBlock.Text = result;
                }
                else
                {
                    ResultBlock.Text = "Нет ответа от агента.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
                ResultBlock.Text = "Ошибка соединения с базой!";
            }
        }
    }
}
