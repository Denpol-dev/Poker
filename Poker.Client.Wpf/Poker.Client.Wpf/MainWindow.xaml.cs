using Microsoft.AspNetCore.SignalR.Client;
using Poker.Shared;
using System.Windows;

namespace Poker.Client.Wpf;

public partial class MainWindow : Window
{
    private PokerConnection? _conn;
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private async void ConnectJoin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _conn = new PokerConnection(_vm.ServerUrl);

            _conn.Hub.On<TableSnapshotDto>("TableSnapshot", snap =>
            {
                Dispatcher.Invoke(() => _vm.ApplyTableSnapshot(snap));
            });

            _conn.Hub.On<PrivateSnapshotDto>("PrivateSnapshot", snap =>
            {
                Dispatcher.Invoke(() => _vm.ApplyPrivateSnapshot(snap));
            });

            _conn.Hub.On<string>("Error", msg => Dispatcher.Invoke(() => _vm.AddLog("[ОШИБКА] " + msg)));
            _conn.Hub.On<string>("Info", msg => Dispatcher.Invoke(() => _vm.AddLog("[ИНФО] " + msg)));

            await _conn.StartAsync();
            await _conn.JoinRoomAsync(_vm.RoomId, _vm.PlayerName);

            _vm.AddLog($"Подключено. PlayerId={_conn.PlayerId}");
        }
        catch (Exception ex)
        {
            _vm.AddLog("[ИСКЛЮЧЕНИЕ] " + ex.Message);
        }
    }

    private async void StartHand_Click(object sender, RoutedEventArgs e)
    {
        if (_conn is null) return;
        await _conn.StartHandAsync(_conn.RoomId);
    }

    private async void Fold_Click(object sender, RoutedEventArgs e) => await Send(ActionType.Fold);
    private async void Check_Click(object sender, RoutedEventArgs e) => await Send(ActionType.Check);
    private async void Call_Click(object sender, RoutedEventArgs e) => await Send(ActionType.Call);

    private async void Raise_Click(object sender, RoutedEventArgs e)
    {
        if (_conn is null) return;
        if (!int.TryParse(RaiseBox.Text, out var amount)) amount = 0;
        await Send(ActionType.Raise, amount);
    }

    private Task Send(ActionType t, int? amount = null)
        => _conn is null ? Task.CompletedTask : _conn.SendActionAsync(t, amount);
}
