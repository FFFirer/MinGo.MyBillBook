using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using MinGo.MyBillBook.Core.DTOs;

namespace MinGo.MyBillBook.Client.Services;

/// <summary>
/// 全局通知中心状态：拉取通知、维护未读徽标、订阅 SignalR 事件、浏览器通知与运行跳转。
/// 由 MainLayout 初始化一次，铃铛与面板共享同一份状态。
/// </summary>
public sealed class NotificationStore : IDisposable
{
    private readonly HttpClient _http;
    private readonly NavigationManager _nav;
    private readonly IJSRuntime _js;
    private readonly List<InboxNotificationDto> _items = [];

    private HubConnection? _hub;
    private CancellationTokenSource? _cts;
    private SynchronizationContext? _sync;
    private int _started;
    private int _refreshing;

    public IReadOnlyList<InboxNotificationDto> Items => _items;
    public int UnreadCount { get; private set; }
    public bool PanelOpen { get; private set; }
    public bool Loading { get; private set; }
    public string BrowserPermission { get; private set; } = "default";
    public string? ActionError { get; private set; }

    /// <summary>状态变化通知；订阅方应通过 InvokeAsync 切回渲染上下文。</summary>
    public event Action? Changed;

    public NotificationStore(HttpClient http, NavigationManager nav, IJSRuntime js)
    {
        _http = http;
        _nav = nav;
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        _sync = SynchronizationContext.Current;

        Loading = true;
        NotifyChanged();
        await RefreshAsync();
        Loading = false;

        BrowserPermission = await GetBrowserPermissionAsync();
        await UpdateTitleBadgeAsync();
        await StartRealtimeAsync();

        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public async Task TogglePanelAsync()
    {
        PanelOpen = !PanelOpen;
        ActionError = null;
        if (PanelOpen)
            await RefreshAsync();
        NotifyChanged();
    }

    public void ClosePanel()
    {
        PanelOpen = false;
        ActionError = null;
        NotifyChanged();
    }

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;

        try
        {
            var data = await _http.GetFromJsonAsync<InboxNotificationListDto>(
                "api/inbox/notifications?take=50");

            if (data is null)
                return;

            _items.Clear();
            _items.AddRange(data.Items);
            UnreadCount = data.UnreadCount;
            await UpdateTitleBadgeAsync();
            NotifyChanged();
        }
        catch
        {
            // 通知不是关键路径，网络抖动时保留上一次数据。
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    public async Task MarkReadAsync(InboxNotificationDto notification)
    {
        if (notification.IsRead)
            return;

        try
        {
            var resp = await _http.PostAsync($"api/inbox/notifications/{notification.Id}/read", null);
            if (!resp.IsSuccessStatusCode)
            {
                ActionError = $"标记已读失败：{await ReadErrorAsync(resp)}";
                NotifyChanged();
                return;
            }

            var result = await resp.Content.ReadFromJsonAsync<UnreadResponse>();
            ApplyRead(notification.Id);
            UnreadCount = result?.UnreadCount ?? Math.Max(0, UnreadCount - 1);
            await UpdateTitleBadgeAsync();
            NotifyChanged();
        }
        catch (Exception ex)
        {
            ActionError = $"标记已读异常：{ex.Message}";
            NotifyChanged();
        }
    }

    public async Task MarkAllReadAsync()
    {
        try
        {
            var resp = await _http.PostAsync("api/inbox/notifications/read-all", null);
            if (!resp.IsSuccessStatusCode)
            {
                ActionError = $"全部已读失败：{await ReadErrorAsync(resp)}";
                NotifyChanged();
                return;
            }

            for (var i = 0; i < _items.Count; i++)
                _items[i] = _items[i] with { IsRead = true, ReadAt = DateTime.Now };
            UnreadCount = 0;
            await UpdateTitleBadgeAsync();
            NotifyChanged();
        }
        catch (Exception ex)
        {
            ActionError = $"全部已读异常：{ex.Message}";
            NotifyChanged();
        }
    }

    /// <summary>标记已读并跳到对应运行的详情。</summary>
    public async Task OpenRunAsync(InboxNotificationDto notification)
    {
        await MarkReadAsync(notification);
        PanelOpen = false;
        NotifyChanged();

        if (notification.RunId is { } runId)
            _nav.NavigateTo($"/inbox?run={runId}");
        else
            _nav.NavigateTo("/inbox");
    }

    /// <summary>失败告警的一键重试：重新排队后打开运行详情。</summary>
    public async Task RetryRunAsync(InboxNotificationDto notification)
    {
        if (notification.RunId is not { } runId)
            return;

        try
        {
            var resp = await _http.PostAsync($"api/inbox/runs/{runId}/retry", null);
            if (!resp.IsSuccessStatusCode)
            {
                ActionError = $"重试失败：{await ReadErrorAsync(resp)}";
                NotifyChanged();
                return;
            }

            await MarkReadAsync(notification);
            PanelOpen = false;
            NotifyChanged();
            _nav.NavigateTo($"/inbox?run={runId}");
        }
        catch (Exception ex)
        {
            ActionError = $"重试异常：{ex.Message}";
            NotifyChanged();
        }
    }

    public async Task EnableBrowserNotificationsAsync()
    {
        try
        {
            BrowserPermission = await _js.InvokeAsync<string>("mbbNotifications.request");
        }
        catch
        {
            BrowserPermission = "unsupported";
        }
        NotifyChanged();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_hub is not null)
            _ = _hub.DisposeAsync();
    }

    private async Task StartRealtimeAsync()
    {
        try
        {
            _hub = new HubConnectionBuilder()
                .WithUrl(_nav.ToAbsoluteUri("/hubs/notifications"))
                .WithAutomaticReconnect()
                .Build();

            _hub.On<InboxNotificationDto, int>("InboxNotificationCreated", (notification, unread) =>
                OnUi(() =>
                {
                    _items.RemoveAll(n => n.Id == notification.Id);
                    _items.Insert(0, notification);
                    if (_items.Count > 100)
                        _items.RemoveRange(100, _items.Count - 100);
                    UnreadCount = unread;
                    NotifyChanged();
                    _ = UpdateTitleBadgeAsync();
                    _ = ShowBrowserNotificationAsync(notification);
                }));

            _hub.On<int, int>("InboxNotificationsRead", (notificationId, unread) =>
                OnUi(() =>
                {
                    if (notificationId == 0)
                    {
                        for (var i = 0; i < _items.Count; i++)
                            _items[i] = _items[i] with { IsRead = true };
                    }
                    else
                    {
                        ApplyRead(notificationId);
                    }

                    UnreadCount = unread;
                    NotifyChanged();
                    _ = UpdateTitleBadgeAsync();
                }));

            _hub.Reconnected += connectionId =>
            {
                _ = RefreshAsync();
                return Task.CompletedTask;
            };

            await _hub.StartAsync();
        }
        catch
        {
            // SignalR 不是关键路径：断线时由 30 秒轮询兜底。
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(30_000, ct);
                await RefreshAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<string> GetBrowserPermissionAsync()
    {
        try
        {
            return await _js.InvokeAsync<string>("mbbNotifications.permission");
        }
        catch
        {
            return "unsupported";
        }
    }

    private async Task UpdateTitleBadgeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("mbbNotifications.setBadge", UnreadCount);
        }
        catch
        {
            // 浏览器不支持时忽略。
        }
    }

    private async Task ShowBrowserNotificationAsync(InboxNotificationDto notification)
    {
        try
        {
            if (BrowserPermission != "granted")
                return;

            var url = notification.RunId is { } runId
                ? $"/inbox?run={runId}"
                : "/inbox";
            await _js.InvokeVoidAsync("mbbNotifications.show",
                notification.Title, notification.Message, url);
        }
        catch
        {
            // 通知权限或浏览器 API 不可用时忽略。
        }
    }

    private void ApplyRead(int notificationId)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == notificationId)
            {
                _items[i] = _items[i] with { IsRead = true, ReadAt = DateTime.Now };
                break;
            }
        }
    }

    private void NotifyChanged() => Changed?.Invoke();

    private void OnUi(Action action)
    {
        if (_sync is not null && SynchronizationContext.Current != _sync)
            _sync.Post(_ => action(), null);
        else
            action();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? $"HTTP {(int)resp.StatusCode}";
            return body.Length > 0 ? body : $"HTTP {(int)resp.StatusCode}";
        }
        catch
        {
            return $"HTTP {(int)resp.StatusCode}";
        }
    }

    private sealed record UnreadResponse(int UnreadCount);
}
