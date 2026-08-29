using Caliburn.Micro;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Queue;
using Multiplatform_Downloader.Core.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Multiplatform_Downloader.ViewModels;
/****************************************************************************
       Purpose      : 메인 셸(WF-01). 카드형 다운로드 큐 + 상태바 + 실시간 로그.
 ****************************************************************************/
internal sealed class ShellViewModel : Screen
{
    private readonly IDownloadQueueService _queue;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;
    private readonly IWindowManager _windowManager;
    private readonly BatchUrlParser _parser;
    private readonly Services.UpdateCoordinator? _updateCoordinator;
    private string _urlInput = string.Empty;
    private bool _showLog;
    private QueueFilter _activeFilter = QueueFilter.All;
    private string _searchText = string.Empty;
    private readonly ICollectionView _itemsView;
    private readonly ICollectionView _logView;
    private readonly Dictionary<Guid, QueueFilter> _bucketCache = new();
    private string _logFilter = "All";

    public ShellViewModel(
        IDownloadQueueService queue,
        ISettingsService settings,
        IAppLogger logger,
        IWindowManager windowManager,
        BatchUrlParser parser,
        Services.UpdateCoordinator? updateCoordinator = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowManager = windowManager;
        _parser = parser;
        _updateCoordinator = updateCoordinator;

        Items = new ObservableCollection<DownloadItemViewModel>();
        LogLines = new ObservableCollection<string>();

        // 필터·검색(FR-U2.2) — 기본 컬렉션 뷰에만 필터를 걸어 Items 계약(직접 인덱스 접근)은 불변.
        // Refresh()는 필터/검색/버킷 변화 시에만 호출한다(NFR-U4 — 진행률 갱신 경로에서는 호출 금지).
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = o => MatchesFilter((DownloadItemViewModel)o);
        _logView = CollectionViewSource.GetDefaultView(LogLines);
        _logView.Filter = o => MatchesLogFilter((string)o);

        foreach (var entry in _logger.Recent)
            LogLines.Add(entry.Format());

        _queue.ItemChanged += OnItemChanged;
        _logger.Logged += OnLogged;
        UpdateStatusBar();
    }

    public ObservableCollection<DownloadItemViewModel> Items { get; }
    public ObservableCollection<string> LogLines { get; }

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

    /// <summary>큐가 비었을 때 빈 상태 안내 패널을 노출한다.</summary>
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
            _itemsView.Refresh();
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
            _itemsView.Refresh();
        }
    }

    public void FilterAll() => ActiveFilter = QueueFilter.All;
    public void FilterActive() => ActiveFilter = QueueFilter.Active;
    public void FilterWaiting() => ActiveFilter = QueueFilter.Waiting;
    public void FilterDone() => ActiveFilter = QueueFilter.Done;
    public void FilterFailed() => ActiveFilter = QueueFilter.Failed;

    // 칩 활성 표시(ChipButton Tag="on" 트리거)
    public string? FilterAllTag => _activeFilter == QueueFilter.All ? "on" : null;
    public string? FilterActiveTag => _activeFilter == QueueFilter.Active ? "on" : null;
    public string? FilterWaitingTag => _activeFilter == QueueFilter.Waiting ? "on" : null;
    public string? FilterDoneTag => _activeFilter == QueueFilter.Done ? "on" : null;
    public string? FilterFailedTag => _activeFilter == QueueFilter.Failed ? "on" : null;

    private void NotifyFilterTags()
    {
        NotifyOfPropertyChange(nameof(FilterAllTag));
        NotifyOfPropertyChange(nameof(FilterActiveTag));
        NotifyOfPropertyChange(nameof(FilterWaitingTag));
        NotifyOfPropertyChange(nameof(FilterDoneTag));
        NotifyOfPropertyChange(nameof(FilterFailedTag));
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

    /// <summary>상태 → 필터 버킷. 시뮬 SUM 60건이 고정한 상태바 집계 매핑과 동일해야 한다(FR-U2.3).</summary>
    internal static QueueFilter BucketOf(DownloadStatus status) => status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Merging => QueueFilter.Active,
        DownloadStatus.Queued or DownloadStatus.Analyzing or DownloadStatus.Ready or DownloadStatus.Paused
            => QueueFilter.Waiting,
        DownloadStatus.Completed => QueueFilter.Done,
        _ => QueueFilter.Failed,
    };

    // ── 상태바 카운트 분해 (FR-U2.3) — StatusSummary 문자열은 트레이 툴팁용으로 유지 ──

    public int DownloadingCount { get; private set; }
    public int WaitingCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int QueueCount { get; private set; }

    // ── 테마 1클릭 토글 (FR-U2.4, 사용자 피드백 2026-08-03): 라이트 ↔ 다크.
    //    아이콘은 '전환될 대상'을 보여준다(라이트일 때 달, 다크일 때 해). System은 설정 창에서 선택.

    public void ToggleTheme()
    {
        var next = Services.ThemeService.IsEffectiveLight(_settings.Current.Theme)
            ? AppTheme.Dark
            : AppTheme.Light;
        _settings.Current.Theme = next;
        Services.ThemeService.Apply(next);
        _ = _settings.SaveAsync();
        NotifyOfPropertyChange(nameof(ThemeTooltip));
        NotifyOfPropertyChange(nameof(ShowMoonIcon));
        NotifyOfPropertyChange(nameof(ShowSunIcon));
        _logger.Info("UI", $"테마 전환: {next}");
    }

    /// <summary>현재 실효 라이트 → 달 아이콘(다크로 전환) 표시.</summary>
    public bool ShowMoonIcon => Services.ThemeService.IsEffectiveLight(_settings.Current.Theme);

    /// <summary>현재 실효 다크 → 해 아이콘(라이트로 전환) 표시.</summary>
    public bool ShowSunIcon => !ShowMoonIcon;

    public string ThemeTooltip => ShowMoonIcon ? "다크 모드로 전환" : "라이트 모드로 전환";

    // ── 빈 상태 CTA (FR-U2.5) ──

    public void PasteFromClipboard()
    {
        try
        {
            var text = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                EnqueueText(text);
        }
        catch (Exception ex)
        {
            _logger.Warning("UI", $"클립보드 읽기 실패: {ex.GetType().Name}");
        }
    }

    /// <summary>드롭·클립보드·입력창 공용 등록 경로 — AddUrls와 동일한 제외 사유 로깅.</summary>
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

    /// <summary>상단 일괄 다운로드 버튼 라벨 — 체크된 대기 항목 수 표시.</summary>
    public string StartCheckedLabel => $"선택 받기 ({CheckedReadyCount})";

    public bool CanStartChecked => CheckedReadyCount > 0;

    /// <summary>체크된 대기(Ready) 항목만 일괄 다운로드 시작한다.</summary>
    public void StartChecked()
    {
        foreach (var card in Items.Where(i => i.IsChecked && i.CanStartItem).ToList())
            _queue.Start(card.Id);
    }

    /// <summary>헤더 전체선택 체크박스(FR-D3.2). null=일부 선택(tri-state 표시).</summary>
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

    /// <summary>선택 항목 존재 여부 — 컨텍스트 선택 바 표시(FR-U4.1).</summary>
    public bool HasSelection => CheckedCount > 0;

    /// <summary>확인 대화상자 표시(title, message) — 테마 일치 커스텀 창. 테스트에서 페이크로 교체.</summary>
    internal Func<string, string, Task<bool>>? ConfirmInteraction { get; set; }

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var vm = new ConfirmDialogViewModel(title, message);
        await _windowManager.ShowDialogAsync(vm);
        return vm.Confirmed;
    }

    private static string Shorten(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>체크된 진행 중 항목 일괄 일시정지 — 컨텍스트 선택 바(FR-U4.1).</summary>
    public void PauseChecked()
    {
        foreach (var card in Items.Where(i => i.IsChecked && i.CanPauseItem).ToList())
            _queue.Pause(card.Id);
    }

    /// <summary>선택 전체 해제 — 컨텍스트 선택 바 닫기(FR-U4.1).</summary>
    public void ClearSelection()
    {
        foreach (var card in Items)
            card.IsChecked = false;
        NotifySelection();
    }

    /// <summary>체크 항목 일괄 삭제(FR-D3.4). 항상 확인 대화상자(테마 일치)를 거친다.</summary>
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
            _queue.Remove(card.Id); // Remove는 진행 중이면 취소 후 제거한다
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
    public void ClearLog() => LogLines.Clear();

    // ── 로그 드로어 레벨 필터 + 복사 (FR-U3.3) ──

    public void LogFilterAll() => SetLogFilter("All");
    public void LogFilterWarn() => SetLogFilter("Warn");
    public void LogFilterError() => SetLogFilter("Error");

    public string? LogFilterAllTag => _logFilter == "All" ? "on" : null;
    public string? LogFilterWarnTag => _logFilter == "Warn" ? "on" : null;
    public string? LogFilterErrorTag => _logFilter == "Error" ? "on" : null;

    private void SetLogFilter(string filter)
    {
        if (_logFilter == filter)
            return;
        _logFilter = filter;
        NotifyOfPropertyChange(nameof(LogFilterAllTag));
        NotifyOfPropertyChange(nameof(LogFilterWarnTag));
        NotifyOfPropertyChange(nameof(LogFilterErrorTag));
        _logView.Refresh();
    }

    /// <summary>LogEntry.Format의 "[Warning]"/"[Error  ]" 패딩 표기와 결합 — Format 변경 시 함께 수정.</summary>
    internal bool MatchesLogFilter(string line) => _logFilter switch
    {
        "Warn" => line.Contains("[Warning", StringComparison.Ordinal) || line.Contains("[Error", StringComparison.Ordinal),
        "Error" => line.Contains("[Error", StringComparison.Ordinal),
        _ => true,
    };

    public void CopyLog()
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, LogLines.Where(MatchesLogFilter)));
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

    // Caliburn 액션 코루틴은 async 예외를 조용히 삼키므로, 다이얼로그 실패는 여기서 직접 로깅한다.
    public async System.Threading.Tasks.Task OpenSettings()
    {
        try
        {
            _logger.Info("UI", "설정 창 열기");
            await _windowManager.ShowDialogAsync(new SettingsViewModel(_settings));
            _logger.Info("UI", "설정 창 닫힘");
            UpdateStatusBar(); // 폴더·동시수 변경 즉시 반영
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"설정 창 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    public async System.Threading.Tasks.Task ShowAbout()
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

    /// <summary>앱 내 로그인 창을 열고, 쿠키가 저장되면 설정에 반영 후 해당 항목을 자동 재시도한다(FR-L4).</summary>
    private async System.Threading.Tasks.Task OpenLoginBrowser(DownloadItemViewModel card)
    {
        try
        {
            _logger.Info("UI", "로그인 창 열기");
            var vm = new LoginBrowserViewModel(card.OriginalUrl, _logger);
            await _windowManager.ShowDialogAsync(vm);
            if (!vm.CookiesSaved)
                return;

            // 저장된 쿠키 파일을 설정에 반영 — 설정 UI에 그대로 보여 사용자가 끄거나 바꿀 수 있다
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

    /// <summary>인앱 플레이어 창(§9) — 완료 항목 목록을 플레이리스트로 넘겨 이전/다음 이동 지원.
    /// ShowWindowAsync를 반드시 await — fire-and-forget은 뷰 생성 예외(XAML 등)를 삼켜
    /// "클릭해도 무반응"이 된다(2026-08-03 IcoExternal 누락 사고).</summary>
    private async System.Threading.Tasks.Task OpenPlayerAsync(DownloadItemViewModel card)
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
            var vm = new PlayerViewModel(playlist, Math.Max(index, 0), _logger);
            _logger.Info("UI", $"플레이어 열기: {System.IO.Path.GetFileName(card.OutputPath)}");
            await _windowManager.ShowWindowAsync(vm); // 비모달 — 큐 조작과 병행
        }
        catch (Exception ex)
        {
            _logger.Error("UI", $"플레이어 열기 실패: {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>외부 앱 드래그 아웃 페이로드 수집(FR-DG2·DG3, 탐색기 관례):
    /// 시작 카드가 체크돼 있으면 체크된 드래그 가능 항목 전체, 아니면 그 카드 1개.
    /// 실존 파일만 담고([id] 재해석 포함), 0건이면 빈 목록 + 경고 로그(드래그 미시작).</summary>
    internal IReadOnlyList<string> CollectDragPaths(DownloadItemViewModel origin)
    {
        var cards = origin.IsChecked
            ? Items.Where(i => i.IsChecked && i.CanDragItem)
            : [origin];
        var paths = cards
            .Select(c => c.GetDraggablePath())
            .OfType<string>()
            .Distinct()
            .ToList();
        if (paths.Count == 0)
            _logger.Warning("UI", "드래그할 파일을 찾지 못했습니다(삭제/이동 여부 확인)");
        else
            _logger.Info("UI", $"드래그 아웃 시작: {paths.Count}개 파일");
        return paths;
    }

    /// <summary>저장된 출력 경로가 실제 파일과 다르면(과거 CP949 stdout 훼손 등)
    /// 파일명 끝의 "[id]" 토큰으로 같은 폴더에서 실제 파일을 찾는다.</summary>
    internal static string? ResolveMediaPath(string path)
    {
        if (System.IO.File.Exists(path))
            return path;
        var directory = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var ext = System.IO.Path.GetExtension(path);
        if (directory is null || !System.IO.Directory.Exists(directory))
            return null;
        var lb = name.LastIndexOf('[');
        var rb = name.LastIndexOf(']');
        if (lb < 0 || rb <= lb)
            return null;
        var idToken = name[lb..(rb + 1)];
        return System.IO.Directory.EnumerateFiles(directory, $"*{idToken}*{ext}").FirstOrDefault();
    }

    public async System.Threading.Tasks.Task OpenAddLinks()
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
                    card.PropertyChanged += OnCardPropertyChanged; // 체크 토글 → 선택 받기 라벨 갱신
                    card.LoginFixRequested = c => _ = OpenLoginBrowser(c); // [로그인] 해결(FR-L2)
                    card.PlayRequested = c => _ = OpenPlayerAsync(c); // [재생] 인앱 플레이어(§9)
                    card.ConfirmRemove = c => ShowConfirmAsync("항목 삭제",
                        $"'{Shorten(c.Title)}'을(를) 목록에서 삭제할까요?\n이미 받은 파일은 삭제되지 않습니다.");
                    Items.Add(card);
                }
            }
            else if (!_queue.Items.Any(q => q.Id == item.Id))
            {
                existing.PropertyChanged -= OnCardPropertyChanged;
                Items.Remove(existing); // 제거된 항목
            }
            else
            {
                existing.Refresh(item);
            }

            // 버킷(진행/대기/완료/실패)이 바뀐 항목만 필터 뷰 갱신 — 진행률 이벤트에는 반응하지 않는다(NFR-U4)
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
                _itemsView.Refresh();

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
        OnUi(() =>
        {
            LogLines.Add(entry.Format());
            while (LogLines.Count > 500)
                LogLines.RemoveAt(0);
        });
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

        // 문자열 요약은 트레이 툴팁용으로 유지(FR-U2.3) — 상태바는 카운트 4종을 바인딩한다
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

    private static void OnUi(System.Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }
}
