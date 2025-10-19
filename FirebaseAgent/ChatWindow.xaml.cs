using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Database.Streaming;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SharedChat
{
    public partial class ChatWindow : Window
    {
        private readonly FirebaseClient _firebase;
        private readonly string _roomId;
        private readonly string _senderName; // "admin" или имя машины
        private IDisposable _sub;
        private readonly ObservableCollection<MsgVm> _items = new ObservableCollection<MsgVm>();

        public ChatWindow(FirebaseClient firebase, string roomId, string senderName)
        {
            InitializeComponent();
            _firebase = firebase;
            _roomId = roomId;
            _senderName = senderName;
            MessagesList.ItemsSource = _items;

            Subscribe();
            Title = $"Chat • room: {_roomId} • me: {_senderName}";
        }

        private void Subscribe()
        {
            // Реактивная подписка на новые сообщения
            _sub = _firebase.Child("chat").Child(_roomId).Child("messages")
                .AsObservable<ChatMessage>()
                .Where(e => e.EventType == FirebaseEventType.InsertOrUpdate && e.Object != null)
                .Subscribe(e =>
                {
                    var m = e.Object;
                    var header = $"[{m.ts:HH:mm}] {m.sender}";
                    var body = string.IsNullOrWhiteSpace(m.translated)
                        ? m.text
                        : $"{m.text}\n({m.translated})";

                    Dispatcher.Invoke(() => _items.Add(new MsgVm { Header = header, Body = body }));
                });
        }

        private async Task SendAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var msg = new ChatMessage
            {
                sender = _senderName,
                text = text.Trim(),
                translated = TryTranslate(text.Trim()),
                ts = DateTime.UtcNow
            };

            await _firebase.Child("chat").Child(_roomId).Child("messages").PostAsync(msg);
        }

        // Простая демонстрационная “переводилка”.
        // Позже легко заменить на вызов любого API переводчика.
        private string TryTranslate(string s)
        {
            var t = s.Trim();
            // очень простой пример (EN <-> RU), чтобы сразу увидеть работу
            if (t.Equals("hello", StringComparison.OrdinalIgnoreCase)) return "привет";
            if (t.Equals("hi", StringComparison.OrdinalIgnoreCase)) return "привет";
            if (t.Equals("привет", StringComparison.OrdinalIgnoreCase)) return "hello";
            return ""; // пусто — значит, покажем только оригинал
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text;
            InputBox.Clear();
            await SendAsync(text);
        }

        private async void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                var text = InputBox.Text;
                InputBox.Clear();
                await SendAsync(text);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _sub?.Dispose();
        }

        private class MsgVm
        {
            public string Header { get; set; }
            public string Body { get; set; }
        }

        private class ChatMessage
        {
            public string sender { get; set; }
            public string text { get; set; }
            public string translated { get; set; }
            public DateTime ts { get; set; }
        }
    }
}
