using System.Text.Json;
using DXO.Configuration;

namespace DXO.Services.Authorization;

/// <summary>
/// File-based implementation of approved user list service.
/// Supports hot-reload via FileSystemWatcher.
/// </summary>
public class FileApprovedListService : IApprovedListService, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger<FileApprovedListService> _logger;
    private ApprovedListOptions _cachedList;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private FileSystemWatcher? _fileWatcher;
    private DateTime _lastReloadTime = DateTime.MinValue;
    private readonly TimeSpan _reloadDebounce = TimeSpan.FromSeconds(1);

    public FileApprovedListService(IWebHostEnvironment env, ILogger<FileApprovedListService> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(env.ContentRootPath, "approved-users.json");
        _cachedList = new ApprovedListOptions();
        
        // Initial load
        LoadApprovedListAsync().GetAwaiter().GetResult();
        
        // Set up file watcher for hot reload
        SetupFileWatcher();
    }

    public async Task<bool> IsUserApprovedAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalizedEmail = NormalizeEmail(email);
        
        // Check wildcard first
        if (_cachedList.IsWildcard)
        {
            _logger.LogDebug("Wildcard enabled - approving user: {Email}", normalizedEmail);
            return true;
        }

        // Check if email is in the list
        var isApproved = _cachedList.ApprovedUsers
            .Any(u => NormalizeEmail(u) == normalizedEmail);

        _logger.LogDebug("User {Email} approval check: {Result}", normalizedEmail, isApproved);
        return isApproved;
    }

    public Task<bool> IsWildcardEnabledAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_cachedList.IsWildcard);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await LoadApprovedListAsync(ct);
    }

    private async Task LoadApprovedListAsync(CancellationToken ct = default)
    {
        await _reloadLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Approved users file not found at {Path}. Defaulting to deny all (unless wildcard was previously loaded).", _filePath);
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);
            var options = JsonSerializer.Deserialize<ApprovedListOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (options == null)
            {
                _logger.LogError("Failed to deserialize approved users file. Keeping previous configuration.");
                return;
            }

            _cachedList = options;
            _lastReloadTime = DateTime.UtcNow;

            _logger.LogInformation(
                "Approved users list loaded. Wildcard: {Wildcard}, User count: {Count}",
                _cachedList.IsWildcard,
                _cachedList.ApprovedUsers.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading approved users file from {Path}", _filePath);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void SetupFileWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            var fileName = Path.GetFileName(_filePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                return;

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Created += OnFileChanged;
            _fileWatcher.Renamed += OnFileChanged;

            _logger.LogInformation("File watcher set up for approved-users.json hot reload");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set up file watcher for approved-users.json. Hot reload disabled.");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: only reload if enough time has passed since last reload
        var timeSinceLastReload = DateTime.UtcNow - _lastReloadTime;
        if (timeSinceLastReload < _reloadDebounce)
            return;

        _logger.LogInformation("Approved users file changed. Reloading...");
        
        // Fire and forget - we don't want to block the file watcher thread
        Task.Run(async () =>
        {
            try
            {
                // Small delay to ensure file write is complete
                await Task.Delay(100);
                await LoadApprovedListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during hot reload of approved users file");
            }
        });
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _reloadLock?.Dispose();
    }
}
