using Avalonia.Threading;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Avalonia.Services;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using System.Collections.ObjectModel;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>메인 셸(WF-01) — WPF 헤드 이식. 카드형 다운로드 큐 + 상태바 + 실시간 로그.
/// 변경점: CollectionViewSource → 수동 필터 컬렉션(ItemsView/LogView), Clipboard → ClipboardService,
/// 칩 활성 Tag 문자열 → bool(Classes.on 바인딩).</summary>
internal sealed class ShellViewModel : Screen
{
    private readonly IDownloadQueueService _queue;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;
    private readonly IWindowManager _windowManager;
    private readonly BatchUrlParser _parser;
    private readonly ClipboardService _clipboard;
    private readonly Services.UpdateCoordinator? _updateCoordinator;
    private string _urlInput = string.Empty;
    private bool _showLog;
    private QueueFilter _activeFilter = QueueFilter.All;
    private string _searchText = string.Empty;
    private readonly Dictionary<Guid, QueueFilter> _bucketCache = new();
    private string _logFilter = "All";

    public ShellViewModel(
        IDownloadQueueService queue,
        ISettingsService settings,
        IAppLogger logger,
        IWindowManager windowManager,
        BatchUrlParser parser,
        ClipboardService clipboard,
        Services.UpdateCoordinator? updateCoordinator = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowManager = windowManager;
        _parser = parser;
        _clipboard = clipboard;
        _updateCoordinator = updateCoordinator;
        DisplayName = "샤샤룽 다운로더";

        Items = new ObservableCollection<DownloadItemViewModel>();
        ItemsView = new ObservableCollection<DownloadItemViewModel>();
        LogLines = new ObservableCollection<string>();
        LogView = new ObservableCollection<string>();

        foreach (var entry in _logger.Recent)
            AppendLog(entry.Format());

        _queue.ItemChanged += OnItemChanged;
        _logger.Logged += OnLogged;
        UpdateStatusBar();
    }

    /// <summary>전체 항목(불변 계약 — 인덱스 접근용). 화면은 ItemsView를 바인딩한다.</summary>
    public ObservableCollection<DownloadItemViewModel> Items { get; }

    /// <summary>필터·검색 적용된 표시용 컬렉션(FR-U2.2).</summary>
    public ObservableCollection<DownloadItemViewModel> ItemsView { get; }

    public ObservableCollection<string> LogLines { get; }
    public ObservableCollection<string> LogView { get; }

    public string UrlInput
    {
        get => _urlInput;
        set { _urlInput = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(nameof(CanAddUrls)); }
    }

    public bool CanAddUrls => !string.IsNullOrWhiteSpace(UrlInput);

    public string StatusSummary { get; private set; } = string.Empty;
    public string FolderInfo { get; private set; } = string.Empty;
    public string ConcurrencyInfo { get; private set; } = string.Empty;
    public bool HasItems => Items.Count > 0;
    public bool ShowEmptyState => Items.Count == 0;

    // ── 상태 필터 + 검색 (FR-U2.2) ──

    public QueueFilter ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (_activeFilter == value)
                return;
            _activeFilter = value;
            NotifyOfPropertyChange();
            NotifyFilterTags();
            RefreshItemsView();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (_searchText == next)
                return;
            _searchText = next;
            NotifyOfPropertyChange();
            RefreshItemsView();
        }
    }

    public void FilterAll() => ActiveFilter = QueueFilter.All;
    public void FilterActive() => ActiveFilter = QueueFilter.Active;
    public void FilterWaiting() => ActiveFilter = QueueFilter.Waiting;
    public void FilterDone() => ActiveFilter = QueueFilter.Done;
    public void FilterFailed() => ActiveFilter = QueueFilter.Failed;

    // 칩 활성 표시(Classes.on 바인딩)
    public bool FilterAllOn => _activeFilter == QueueFilter.All;
    public bool FilterActiveOn => _activeFilter == QueueFilter.Active;
    public bool FilterWaitingOn => _activeFilter == QueueFilter.Waiting;
    public bool FilterDoneOn => _activeFilter == QueueFilter.Done;
    public bool FilterFailedOn => _activeFilter == QueueFilter.Failed;

    private void NotifyFilterTags()
    {
        NotifyOfPropertyChange(nameof(FilterAllOn));
        NotifyOfPropertyChange(nameof(FilterActiveOn));
        NotifyOfPropertyChange(nameof(FilterWaitingOn));
        NotifyOfPropertyChange(nameof(FilterDoneOn));
        NotifyOfPropertyChange(nameof(FilterFailedOn));
    }

    internal bool MatchesFilter(DownloadItemViewModel card)
    {
        if (_activeFilter != QueueFilter.All && BucketOf(card.Status) != _activeFilter)
            return false;
        if (string.IsNullOrWhiteSpace(_searchText))
            return true;
        return card.TitleFull.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || card.OriginalUrl.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    internal static QueueFilter BucketOf(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Merging => QueueFilter.Active,
        DownloadStatus.Queued or DownloadStatus.Analyzing or DownloadStatus.Ready or DownloadStatus.Paused
            => QueueFilter.Waiting,
        DownloadStatus.Completed => QueueFilter.Done,
        _ => QueueFilter.Failed,
    };

    /// <summary>표시 컬렉션 재구성 — 필터/검색/버킷 변화 시에만(NFR-U4: 진행률 경로 호출 금지).</summary>
    private void RefreshItemsView()
    {
        ItemsView.Clear();
        foreach (var card in Items)
            if (MatchesFilter(card))
                ItemsView.Add(card);
    }

    // ── 상태바 카운트 (FR-U2.3) ──

    public int DownloadingCount { get; private set; }
    public int WaitingCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int QueueCount { get; private set; }

    // ── 테마 1클릭 토글 (FR-U2.4) ──

    public void ToggleTheme()
    {
        var next = ThemeController.IsEffectiveLight(_settings.Current.Theme)
            ? AppTheme.Dark
            : AppTheme.Light;
        _settings.Current.Theme = next;
        ThemeController.Apply(next);
        _ = _settings.SaveAsync();
        NotifyOfPropertyChange(nameof(ThemeTooltip));
        NotifyOfPropertyChange(nameof(ShowMoonIcon));
        NotifyOfPropertyChange(nameof(ShowSunIcon));
        _logger.Info("UI", $"테마 전환: {next}");
    }

    public bool ShowMoonIcon => ThemeController.IsEffectiveLight(_settings.Current.Theme);
    public bool ShowSunIcon => !ShowMoonIcon;
    public string ThemeTooltip => ShowMoonIcon ? "다크 모드로 전환" : "라이트 모드로 전환";

    // ── 빈 상태 CTA (FR-U2.5) ──

    public async Task PasteFromClipboard()
    {
        try
        {
            var text = await _clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
                EnqueueText(text);
        }
        catch (Exception ex)
        {
            _logger.Warning("UI", $"클립보드 읽기 실패: {ex.GetType().Name}");
        }
    }

    internal void EnqueueText(string text)
    {
        var result = _queue.Enqueue(text);
        if (result.Rejected.Count > 0)
            _logger.Warning("UI", $"미지원 URL {result.Rejected.Count}건 제외");
        if (result.SkippedOverCapacity > 0)
            _logger.Warning("UI", $"큐 용량 초과로 {result.SkippedOverCapacity}건 제외");
    }

    private int CheckedReadyCount => Items.Count(i => i.IsChecked && i.CanStartItem);
    private int CheckedCount => Items.Count(i => i.IsChecked);

    public string StartCheckedLabel => $"선택 받기 ({CheckedReadyCount})";
    public bool CanStartChecked => CheckedReadyCount > 0;

    public void StartChecked()
    {
        foreach (var card in Items.Where(i => i.IsChecked && i.CanStartItem).ToList())
            _queue.Start(card.Id);
    }

    public bool? SelectAllState
    {
        get
        {
            if (Items.Count == 0)
                return false;
            var check = Items.Count(i => i.IsChecked);
            return check == 0 ? false : check == Items.Count ? true : null;
        }
        set
        {
            var target = value ?? false;
            foreach (var card in Items)
                card.IsChecked = target;
            NotifySelection();
        }
    }

    public string SelectionSummary => $"{CheckedCount}/{Items.Count} 선택";
    public string DeleteCheckedLabel => $"선택 삭제 ({CheckedCount})";
    public bool CanDeleteChecked => CheckedCount > 0;
    public bool HasSelection => CheckedCount > 0;

    internal Func<string, string, Task<bool>>? ConfirmInteraction { get; set; }

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var vm = new ConfirmDialogViewModel(title, message);
        await _windowManager.ShowDialogAsync(vm);
        return vm.Confirmed;
    }

    private static string Shorten(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";

    public void PauseChecked()
    {
        foreach (var card in Items.Where(i => i.IsChecked && i.CanPauseItem).ToList())
            _queue.Pause(card.Id);
    }

    public void ClearSelection()
    {
        foreach (var card in Items)
            card.IsChecked = false;
        NotifySelection();
    }

    public async Task DeleteChecked()
    {
        var targets = Items.Where(i => i.IsChecked).ToList();
        if (targets.Count == 0)
            return;

        var active = targets.Count(t => t.IsActive);
        var message = $"체크된 {targets.Count}개 항목을 목록에서 삭제할까요?"
            + (active > 0 ? $"\n진행 중인 항목 {active}건은 다운로드가 취소됩니다." : string.Empty)
            + "\n이미 받은 파일은 삭제되지 않습니다.";
        if (!await (ConfirmInteraction ?? ShowConfirmAsync)("선택 삭제", message))
            return;

        foreach (var card in targets)
            _queue.Remove(card.Id);
        _logger.Info("UI", $"선택 삭제 {targets.Count}건 (진행 중 {active}건 포함)");
    }

    public bool ShowLog
    {
        get => _showLog;
        set { _showLog = value; NotifyOfPropertyChange(); }
    }

    public void AddUrls()
    {
        if (string.IsNullOrWhiteSpace(UrlInput))
            return;
        EnqueueText(UrlInput);
        UrlInput = string.Empty;
    }

    public void ToggleLog() => ShowLog = !ShowLog;

    public void ClearLog()
    {
        LogLines.Clear();
        LogView.Clear();
    }

    // ── 로그 드로어 레벨 필터 + 복사 (FR-U3.3) ──

    public void LogFilterAll() => SetLogFilter("All");
    public void LogFilterWarn() => SetLogFilter("Warn");
    public void LogFilterError() => SetLogFilter("Error");

    public bool LogFilterAllOn => _logFilter == "All";
    public bool LogFilterWarnOn => _logFilter == "Warn";
    public bool LogFilterErrorOn => _logFilter == "Error";

    private void SetLogFilter(string filter)
    {
        if (_logFilter == filter)
            return;
        _logFilter = filter;
        NotifyOfPropertyChange(nameof(LogFilterAllOn));
        NotifyOfPropertyChange(nameof(LogFilterWarnOn));
        NotifyOfPropertyChange(nameof(LogFilterErrorOn));
        RefreshLogView();
    }

    internal bool MatchesLogFilter(string line) => _logFilter switch
    {
        "Warn" => line.Contains("[Warning", StringComparison.Ordinal) || line.Contains("[Error", StringComparison.Ordinal),
        "Error" => line.Contains("[Error", StringComparison.Ordinal),
        _ => true,
    };

    private void RefreshLogView()
    {
        LogView.Clear();
        foreach (var line in LogLines)
            if (MatchesLogFilter(line))
                LogView.Add(line);
    }

    public async Task CopyLog()
    {
        try
        {
            await _clipboard.SetTextAsync(string.Join(Environment.NewLine, LogLines.Where(MatchesLogFilter)));
            _logger.Info("UI", "로그 복사됨");
        }
        catch (Exception ex)
        {
            _logger.Warning("UI", $"로그 복사 실패: {ex.GetType().Name}");
        }
    }

    public void StartAll() => _queue.StartAll();
    public void PauseAll() => _queue.PauseAll();
    public void ResumeAll() => _queue.ResumeAll();

    public async Task OpenSettings()
    {
        try
        {
            _logger.Info("UI", "설정 창 열기");
            await _windowManager.ShowDialogAsync(new SettingsViewModel(_settings));
            _logger.Info("UI", "설정 창 닫힘");
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"설정 창 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    public async Task ShowAbout()
    {
        try
        {
            await _windowManager.ShowDialogAsync(new AboutViewModel(_updateCoordinator));
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"정보 창 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>앱 내 로그인 창(FR-L4) — 쿠키 저장 시 설정 반영 후 자동 재시도.</summary>
    private async Task OpenLoginBrowser(DownloadItemViewModel card)
    {
        try
        {
            _logger.Info("UI", "로그인 창 열기");
            var vm = new LoginBrowserViewModel(card.OriginalUrl, _logger);
            await _windowManager.ShowDialogAsync(vm);
            if (!vm.CookiesSaved)
                return;

            _settings.Current.CookieSource = CookieSource.CookieFile;
            _settings.Current.CookieFilePath = LoginBrowserViewModel.DefaultCookieFilePath;
            await _settings.SaveAsync();
            _queue.Retry(card.Id);
            _logger.Info("UI", "로그인 쿠키 적용 — 항목 재시도");
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"로그인 창 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>인앱 플레이어 창(§9) — 완료 항목 목록을 플레이리스트로 넘겨 이전/다음 이동 지원.</summary>
    private async Task OpenPlayerAsync(DownloadItemViewModel card)
    {
        if (_windowManager is null || card.OutputPath is null)
            return;
        try
        {
            var playlist = Items
                .Where(i => i.CanPlayItem && i.OutputPath is not null)
                .Select(i => (Title: i.TitleFull, Path: ResolveMediaPath(i.OutputPath!)))
                .Where(p => p.Path is not null)
                .Select(p => new PlayerItem(p.Title, p.Path!))
                .ToList();
            if (playlist.Count == 0)
            {
                _logger.Warning("UI", "재생할 파일을 찾지 못했습니다(경로 확인 필요)");
                return;
            }
            var resolved = ResolveMediaPath(card.OutputPath);
            var index = playlist.FindIndex(p => p.Path == resolved);
            var isDark = !ThemeController.IsEffectiveLight(_settings.Current.Theme);
            var vm = new PlayerViewModel(playlist, Math.Max(index, 0), _logger, isDark);
            _logger.Info("UI", $"플레이어 열기: {Path.GetFileName(card.OutputPath)}");
            await _windowManager.ShowWindowAsync(vm);
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"플레이어 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>저장 경로가 실파일과 다르면 파일명 끝 "[id]" 토큰으로 실제 파일을 찾는다.</summary>
    internal static string? ResolveMediaPath(string path)
    {
        if (File.Exists(path))
            return path;
        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        if (directory is null || !Directory.Exists(directory))
            return null;
        var lb = name.LastIndexOf('[');
        var rb = name.LastIndexOf(']');
        if (lb < 0 || rb <= lb)
            return null;
        var idToken = name[lb..(rb + 1)];
        return Directory.EnumerateFiles(directory, $"*{idToken}*{ext}").FirstOrDefault();
    }

    public async Task OpenAddLinks()
    {
        try
        {
            var dialog = new AddLinksViewModel(_parser);
            var confirmed = await _windowManager.ShowDialogAsync(dialog);
            if (confirmed == true && !string.IsNullOrWhiteSpace(dialog.Result))
                _queue.Enqueue(dialog.Result);
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"일괄 추가 창 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    private void OnItemChanged(object? sender, DownloadItem item)
    {
        OnUi(() =>
        {
            var existing = Items.FirstOrDefault(i => i.Id == item.Id);
            if (existing is null)
            {
                if (_queue.Items.Any(q => q.Id == item.Id))
                {
                    var card = new DownloadItemViewModel(item, _queue, _settings.Current.AnalysisRetryCount, _logger);
                    card.PropertyChanged += OnCardPropertyChanged;
                    card.LoginFixRequested = c => _ = OpenLoginBrowser(c);
                    card.PlayRequested = c => _ = OpenPlayerAsync(c);
                    card.ConfirmRemove = c => ShowConfirmAsync("항목 삭제",
                        $"'{Shorten(c.Title)}'을(를) 목록에서 삭제할까요?\n이미 받은 파일은 삭제되지 않습니다.");
                    Items.Add(card);
                    if (MatchesFilter(card))
                        ItemsView.Add(card);
                }
            }
            else if (!_queue.Items.Any(q => q.Id == item.Id))
            {
                existing.PropertyChanged -= OnCardPropertyChanged;
                Items.Remove(existing);
                ItemsView.Remove(existing);
            }
            else
            {
                existing.Refresh(item);
            }

            // 버킷이 바뀐 항목만 필터 뷰 갱신 — 진행률 이벤트에는 반응하지 않는다(NFR-U4)
            var inQueue = _queue.Items.Any(q => q.Id == item.Id);
            var bucketChanged = false;
            if (!inQueue)
            {
                bucketChanged = _bucketCache.Remove(item.Id);
            }
            else
            {
                var bucket = BucketOf(item.Status);
                if (!_bucketCache.TryGetValue(item.Id, out var prev) || prev != bucket)
                {
                    _bucketCache[item.Id] = bucket;
                    bucketChanged = true;
                }
            }
            if (bucketChanged && (_activeFilter != QueueFilter.All || !string.IsNullOrWhiteSpace(_searchText)))
                RefreshItemsView();

            UpdateStatusBar();
            NotifyOfPropertyChange(nameof(HasItems));
            NotifyOfPropertyChange(nameof(ShowEmptyState));
            NotifyStartChecked();
        });
    }

    private void OnCardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(DownloadItemViewModel.IsChecked) or nameof(DownloadItemViewModel.CanStartItem))
            NotifyStartChecked();
    }

    private void NotifyStartChecked() => NotifySelection();

    private void NotifySelection()
    {
        NotifyOfPropertyChange(nameof(StartCheckedLabel));
        NotifyOfPropertyChange(nameof(CanStartChecked));
        NotifyOfPropertyChange(nameof(SelectAllState));
        NotifyOfPropertyChange(nameof(SelectionSummary));
        NotifyOfPropertyChange(nameof(DeleteCheckedLabel));
        NotifyOfPropertyChange(nameof(CanDeleteChecked));
        NotifyOfPropertyChange(nameof(HasSelection));
    }

    private void OnLogged(object? sender, LogEntry entry)
    {
        OnUi(() => AppendLog(entry.Format()));
    }

    private void AppendLog(string line)
    {
        LogLines.Add(line);
        while (LogLines.Count > 500)
            LogLines.RemoveAt(0);
        if (MatchesLogFilter(line))
        {
            LogView.Add(line);
            while (LogView.Count > 500)
                LogView.RemoveAt(0);
        }
    }

    private void UpdateStatusBar()
    {
        var items = _queue.Items;
        var downloading = items.Count(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Merging);
        var waiting = items.Count(i => i.Status is DownloadStatus.Queued or DownloadStatus.Analyzing or DownloadStatus.Ready or DownloadStatus.Paused);
        var completed = items.Count(i => i.Status == DownloadStatus.Completed);
        var failed = items.Count(i => i.Status is DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Unavailable);

        DownloadingCount = downloading;
        WaitingCount = waiting;
        CompletedCount = completed;
        FailedCount = failed;
        QueueCount = items.Count;

        StatusSummary = $"진행 {downloading} · 대기 {waiting} · 완료 {completed} · 실패 {failed}";
        FolderInfo = _settings.Current.DownloadFolder;
        ConcurrencyInfo = $"동시 {_settings.Current.MaxConcurrent} · 큐 {items.Count}/{_settings.Current.MaxQueueItems}";

        NotifyOfPropertyChange(nameof(DownloadingCount));
        NotifyOfPropertyChange(nameof(WaitingCount));
        NotifyOfPropertyChange(nameof(CompletedCount));
        NotifyOfPropertyChange(nameof(FailedCount));
        NotifyOfPropertyChange(nameof(QueueCount));
        NotifyOfPropertyChange(nameof(StatusSummary));
        NotifyOfPropertyChange(nameof(FolderInfo));
        NotifyOfPropertyChange(nameof(ConcurrencyInfo));
    }

    private static void OnUi(Action action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            Dispatcher.UIThread.Post(action);
        else
            action();
    }
}
