using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Database.Streaming;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SharedChat
{
    public partial class ChatWindow : Window
    {
        private readonly FirebaseClient _firebase;
        private readonly string _roomId;
        private readonly string _senderName;
        private IDisposable _sub;
        private readonly ObservableCollection<MsgVm> _items = new ObservableCollection<MsgVm>();

        public ChatWindow(FirebaseClient firebase, string roomId, string senderName)
        {
            InitializeComponent();
            _firebase = firebase;
            _roomId = roomId;
            _senderName = senderName;

            MessagesList.ItemsSource = _items;
            Title = $"Chat • room: {_roomId} • me: {_senderName}";

            _items.CollectionChanged += (s, e) => ScrollToEnd();

            Subscribe();
        }

        private void ScrollToEnd()
        {
            Dispatcher.InvokeAsync(() => Scroll.ScrollToEnd());
        }

        private void Subscribe()
        {
            _sub = _firebase.Child("chat").Child(_roomId).Child("messages")
                .AsObservable<ChatMessage>()
                .Where(e => e.EventType == FirebaseEventType.InsertOrUpdate && e.Object != null)
                .Subscribe(e =>
                {
                    ChatMessage m = e.Object;
                    string header = "[" + m.ts.ToLocalTime().ToString("HH:mm") + "] " + m.sender;
                    string body = string.IsNullOrWhiteSpace(m.translated)
                        ? m.text
                        : m.text + "\n(" + m.translated + ")";

                    var vm = new MsgVm
                    {
                        Header = header,
                        Body = body,
                        IsMine = string.Equals(m.sender, _senderName, StringComparison.OrdinalIgnoreCase)
                    };

                    Dispatcher.Invoke(() => _items.Add(vm));
                });
        }

        private async Task SendAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            string trimmed = text.Trim();
            var msg = new ChatMessage
            {
                sender = _senderName,
                text = trimmed,
                translated = TryTranslate(trimmed),
                ts = DateTime.UtcNow
            };

            await _firebase.Child("chat").Child(_roomId).Child("messages").PostAsync(msg);
        }

        // Демонстрационный мини-переводчик
        private string TryTranslate(string s)
        {
            if (string.Equals(s, "hello", StringComparison.OrdinalIgnoreCase)) return "привет";
            if (string.Equals(s, "hi", StringComparison.OrdinalIgnoreCase)) return "привет";
            if (string.Equals(s, "привет", StringComparison.OrdinalIgnoreCase)) return "hello";
            return "";
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string text = InputBox.Text;
            InputBox.Clear();
            await SendAsync(text);
            ScrollToEnd();
        }

        // Enter — отправка; Shift+Enter — перенос строки
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Enter && Keyboard.FocusedElement == InputBox)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                {
                    e.Handled = true;
                    Send_Click(this, new RoutedEventArgs());
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_sub != null) _sub.Dispose();
        }
    }

    // ===== PUBLIC MODELS =====
    public class MsgVm
    {
        public string Header { get; set; }
        public string Body { get; set; }
        public bool IsMine { get; set; }
    }

    public class ChatMessage
    {
        public string sender { get; set; }
        public string text { get; set; }
        public string translated { get; set; }
        public DateTime ts { get; set; }
    }

    // ===== PUBLIC TEMPLATE SELECTOR =====
    public class BubbleTemplateSelector : DataTemplateSelector
    {
        public DataTemplate LeftTemplate { get; set; }
        public DataTemplate RightTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var vm = item as MsgVm;
            return (vm != null && vm.IsMine) ? RightTemplate : LeftTemplate;
        }
    }
}
