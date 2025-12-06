using Dalamud.Utility;
using K4os.Compression.LZ4.Legacy;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public partial class FileDownloadManager : DisposableMediatorSubscriberBase
{
    private readonly Dictionary<string, FileDownloadStatus> _downloadStatus;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly List<ThrottledStream> _activeDownloadStreams;
    private readonly MareConfigService _mareConfig;
    private readonly SemaphoreSlim _downloadSemaphore; 

    public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, MareConfigService mareConfig) : base(logger, mediator)
    {
        _downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
        _orchestrator = orchestrator;
        _fileDbManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _mareConfig = mareConfig;
        _activeDownloadStreams = [];
        
        _downloadSemaphore = new SemaphoreSlim(_mareConfig.Current.ParallelDownloads); 

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any()) return;
            var newLimit = _orchestrator.DownloadLimitPerSlot();
            Logger.LogTrace("Setting new Download Speed Limit to {newLimit}", newLimit);
            foreach (var stream in _activeDownloadStreams)
            {
                stream.BandwidthLimit = newLimit;
            }
        });
    }

    public List<DownloadFileTransfer> CurrentDownloads { get; private set; } = [];

    public List<FileTransfer> ForbiddenTransfers => _orchestrator.ForbiddenTransfers;

    public bool IsDownloading => !CurrentDownloads.Any();

    public static void MungeBuffer(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; ++i)
        {
            buffer[i] ^= 42;
        }
    }

    public void ClearDownload()
    {
        CurrentDownloads.Clear();
        _downloadStatus.Clear();
    }

    public async Task DownloadFiles(GameObjectHandler gameObject, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
    {
        Mediator.Publish(new HaltScanMessage(nameof(DownloadFiles)));
        try
        {
            await DownloadFilesInternal(gameObject, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            ClearDownload();
        }
        finally
        {
            Mediator.Publish(new DownloadFinishedMessage(gameObject));
            Mediator.Publish(new ResumeScanMessage(nameof(DownloadFiles)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
        foreach (var stream in _activeDownloadStreams.ToList())
        {
            try
            {
                stream.Dispose();
            }
            catch
            {
                // do nothing
                //
            }
        }
        base.Dispose(disposing);
    }

    private static byte MungeByte(int byteOrEof)
    {
        if (byteOrEof == -1)
        {
            throw new EndOfStreamException();
        }

        return (byte)(byteOrEof ^ 42);
    }

    private static (string fileHash, long fileLengthBytes) ReadBlockFileHeader(FileStream fileBlockStream)
    {
        List<char> hashName = [];
        List<char> fileLength = [];
        var separator = (char)MungeByte(fileBlockStream.ReadByte());
        if (separator != '#') throw new InvalidDataException("Data is invalid, first char is not #");

        bool readHash = false;
        while (true)
        {
            int readByte = fileBlockStream.ReadByte();
            if (readByte == -1)
                throw new EndOfStreamException();

            var readChar = (char)MungeByte(readByte);
            if (readChar == ':')
            {
                readHash = true;
                continue;
            }
            if (readChar == '#') break;
            if (!readHash) hashName.Add(readChar);
            else fileLength.Add(readChar);
        }
        return (string.Join("", hashName), long.Parse(string.Join("", fileLength)));
    }

    private async Task DownloadAndMungeFileHttpClient(string downloadGroup, Guid requestId, List<DownloadFileTransfer> fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        Logger.LogDebug("GUID {requestId} on server {uri} for files {files}", requestId, fileTransfer[0].DownloadUri, string.Join(", ", fileTransfer.Select(c => c.Hash).ToList()));

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        _downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer[0].DownloadUri, requestId);

        Logger.LogDebug("Downloading {requestUrl} for request {id}", requestUrl, requestId);
        try
        {
            response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
            }
        }

        ThrottledStream? stream = null;
        try
        {
            var fileStream = File.Create(tempPath);
            await using (fileStream.ConfigureAwait(false))
            {
                var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 65536 : 8196;
                var buffer = new byte[bufferSize];

                var bytesRead = 0;
                var limit = _orchestrator.DownloadLimitPerSlot();
                Logger.LogTrace("Starting Download of {id} with a speed limit of {limit} to {tempPath}", requestId, limit, tempPath);
                stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);
                _activeDownloadStreams.Add(stream);
                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    MungeBuffer(buffer.AsSpan(0, bytesRead));

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                    progress.Report(bytesRead);
                }

                Logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, tempPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                if (!tempPath.IsNullOrEmpty())
                    File.Delete(tempPath);
            }
            catch
            {
                // ignore if file deletion fails
            }
            throw;
        }
        finally
        {
            if (stream != null)
            {
                _activeDownloadStreams.Remove(stream);
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
    
    private async Task DownloadAndExtractParallel(
        string downloadGroup, 
        Guid requestId, 
        List<DownloadFileTransfer> fileTransfer, 
        List<FileReplacementData> fileReplacement, 
        IProgress<long> progress, 
        IProgress<int> fileProgress, 
        CancellationToken ct)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 10, 
            CancellationToken = ct
        };

        try
        {
            await Parallel.ForEachAsync(fileTransfer, parallelOptions, async (transfer, token) =>
            {
                
                await _downloadSemaphore.WaitAsync(token);
                
                HttpResponseMessage? response = null;
                ThrottledStream? currentThrottledStream = null;

                // 临时文件路径
                var tempFilePath = _fileDbManager.GetCacheFilePath(transfer.Hash, "tmp");

                try
                {
                    // --- 阶段 1: 下载并解开传输层混淆 ---
                    var requestUrl = MareFiles.CacheGetSingleFullPath(transfer.DownloadUri, transfer.Hash);
                    response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, token,
                        HttpCompletionOption.ResponseHeadersRead, requestId).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    var limit = _orchestrator.DownloadLimitPerSlot();

                    await using (var fileStream = File.Create(tempFilePath))
                    {
                        currentThrottledStream =
                            new ThrottledStream(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
                                limit);

                        lock (_activeDownloadStreams) { _activeDownloadStreams.Add(currentThrottledStream); }
                        
                        var buffer = new byte[65536];
                        int bytesRead;
                        while ((bytesRead =
                                   await currentThrottledStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                        {
                            MungeBuffer(buffer.AsSpan(0, bytesRead));

                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                            progress.Report(bytesRead);
                        }
                    }

                    // --- 阶段 2: 读取临时文件并解压 ---
                    await using (var readStream = File.OpenRead(tempFilePath))
                    {
                        // 现在文件已解开传输层混淆，Header 应该是明文 (#HASH:SIZE)
                        (string fileHash, long fileLengthBytes) = ReadBlockFileHeader(readStream);

                        if (!string.Equals(fileHash, transfer.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogWarning($"Hash mismatch: Expected {transfer.Hash}, got {fileHash}");
                        }

                        // 读取剩下的内容 (这些内容仍然处于内容层混淆状态)
                        byte[] compressedContent = new byte[fileLengthBytes];
                        int actualRead = await readStream.ReadAsync(compressedContent, token);

                        if (actualRead != fileLengthBytes)
                            throw new EndOfStreamException($"Expected {fileLengthBytes} bytes, but read {actualRead}");

                        // 解开内容层混淆
                        MungeBuffer(compressedContent);

                        // LZ4 解压
                        var decompressedData = LZ4Wrapper.Unwrap(compressedContent);

                        // --- 阶段 3: 写入最终文件 ---
                        var extension = fileReplacement
                            .FirstOrDefault(f => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
                            ?.GamePaths[0].Split(".")[^1] ?? "dat";

                        var finalFilePath = _fileDbManager.GetCacheFilePath(fileHash, extension);

                        await _fileCompactor.WriteAllBytesAsync(finalFilePath, decompressedData, token)
                            .ConfigureAwait(false);

                        PersistFileToStorage(fileHash, finalFilePath);
                    }

                    fileProgress.Report(1);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to process {hash}", transfer.Hash);
                    throw;
                }
                finally
                {
                    if (currentThrottledStream != null)
                    {
                        lock (_activeDownloadStreams) { _activeDownloadStreams.Remove(currentThrottledStream); }

                        await currentThrottledStream.DisposeAsync().ConfigureAwait(false);
                    }

                    response?.Dispose();

                    try
                    {
                        if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
                    }
                    catch { }
                    
                    _downloadSemaphore.Release();
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Parallel download for request {id} was cancelled.", requestId);
        }
    }

    private async Task ExtractBlockFile(string blockFile, List<FileReplacementData> fileReplacement)
    {
        FileStream? fileBlockStream = null;
        try
        {
            fileBlockStream = File.OpenRead(blockFile);
            while (fileBlockStream.Position < fileBlockStream.Length)
            {
                (string fileHash, long fileLengthBytes) = ReadBlockFileHeader(fileBlockStream);

                try
                {
                    var fileExtension = fileReplacement
                        .FirstOrDefault(f => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
                        ?.GamePaths[0].Split(".")[^1] ?? "dat";
                        
                    var filePath = _fileDbManager.GetCacheFilePath(fileHash, fileExtension);
                    
                    // 读取
                    byte[] compressedFileContent = new byte[fileLengthBytes];
                    var readBytes = await fileBlockStream.ReadAsync(compressedFileContent, CancellationToken.None).ConfigureAwait(false);
                    if (readBytes != fileLengthBytes) throw new EndOfStreamException();

                    // 解压
                    MungeBuffer(compressedFileContent);
                    var decompressedFile = LZ4Wrapper.Unwrap(compressedFileContent);
                    
                    // 写入
                    await _fileCompactor.WriteAllBytesAsync(filePath, decompressedFile, CancellationToken.None).ConfigureAwait(false);
                    PersistFileToStorage(fileHash, filePath);
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e, "Error during decompression of block part");
                }
            }
        }
        finally
        {
            if (fileBlockStream != null) await fileBlockStream.DisposeAsync();
            if (File.Exists(blockFile)) File.Delete(blockFile);
        }
    }

    public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        Logger.LogDebug("Download start: {id}", gameObjectHandler.Name);

        List<DownloadFileDto> downloadFileInfoFromService =
        [
            .. await FilesGetSizes(fileReplacement.Select(f => f.Hash).Distinct(StringComparer.Ordinal).ToList(), ct).ConfigureAwait(false),
        ];

        Logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (!_orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
            }
        }

        CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        return CurrentDownloads;
    }

    private async Task DownloadFilesInternal(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        var downloadGroups = CurrentDownloads.GroupBy(f => f.DownloadUri.Host + ":" + f.DownloadUri.Port, StringComparer.Ordinal);

        foreach (var downloadGroup in downloadGroups)
        {
            // 初始化状态
            _downloadStatus[downloadGroup.Key] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = downloadGroup.Sum(c => c.Total),
                TotalFiles = downloadGroup.Count(),
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

        await Parallel.ForEachAsync(downloadGroups, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadGroups.Count(),
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            // 1. 向服务器发送请求获取 RequestID
            var requestIdResponse = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);
            
            Guid requestId = Guid.Parse((await requestIdResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false)).Trim('"'));
            Logger.LogDebug("GUID {requestId} for {n} files", requestId, fileGroup.Count());

            try
            {
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
                await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.Downloading;

                // 定义进度回调
                Progress<long> progress = new((bytesDownloaded) => {
                    if (_downloadStatus.TryGetValue(fileGroup.Key, out var v)) v.TransferredBytes += bytesDownloaded;
                });
                Progress<int> fileProgress = new((filesDownloaded) => {
                    if (_downloadStatus.TryGetValue(fileGroup.Key, out var v)) v.TransferredFiles += filesDownloaded;
                });

                if (true)
                {
                    // === 新的并行处理逻辑 ===
                    await DownloadAndExtractParallel(fileGroup.Key, requestId, [.. fileGroup], fileReplacement, progress, fileProgress, token).ConfigureAwait(false);
                }
                else
                {
                    // === 旧的 Block 下载逻辑 ===
                    var blockFile = _fileDbManager.GetCacheFilePath(requestId.ToString("N"), "blk");
                    await DownloadAndMungeFileHttpClient(fileGroup.Key, requestId, [.. fileGroup], blockFile, progress, token).ConfigureAwait(false);
                    
                    // 下载完 Block 后手动解压
                    _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.Decompressing;
                    await ExtractBlockFile(blockFile, fileReplacement);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during download of {id}", requestId);
                ClearDownload();
            }
            finally
            {
                _orchestrator.ReleaseDownloadSlot();
            }
        }).ConfigureAwait(false);

        Logger.LogDebug("Download end: {id}", gameObjectHandler);
        ClearDownload();
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.ServerFilesGetSizesFullPath(_orchestrator.FilesCdnUri!), hashes, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    }

    private void PersistFileToStorage(string fileHash, string filePath)
    {
        var fi = new FileInfo(filePath);
        Func<DateTime> RandomDayInThePast()
        {
            DateTime start = new(1995, 1, 1, 1, 1, 1, DateTimeKind.Local);
            Random gen = new();
            int range = (DateTime.Today - start).Days;
            return () => start.AddDays(gen.Next(range));
        }

        fi.CreationTime = RandomDayInThePast().Invoke();
        fi.LastAccessTime = DateTime.Today;
        fi.LastWriteTime = RandomDayInThePast().Invoke();
        try
        {
            var entry = _fileDbManager.CreateCacheEntry(filePath);
            if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogError("Hash mismatch after extracting, got {hash}, expected {expectedHash}, deleting file", entry.Hash, fileHash);
                File.Delete(filePath);
                _fileDbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error creating cache entry");
        }
    }

    private async Task WaitForDownloadReady(List<DownloadFileTransfer> downloadFileTransfer, Guid requestId, CancellationToken downloadCt)
    {
        bool alreadyCancelled = false;
        try
        {
            CancellationTokenSource localTimeoutCts = new();
            localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);

            while (!_orchestrator.IsDownloadReady(requestId))
            {
                try
                {
                    await Task.Delay(250, composite.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    if (downloadCt.IsCancellationRequested) throw;

                    var req = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer[0].DownloadUri, requestId),
                        downloadFileTransfer.Select(c => c.Hash).ToList(), downloadCt).ConfigureAwait(false);
                    req.EnsureSuccessStatusCode();
                    localTimeoutCts.Dispose();
                    composite.Dispose();
                    localTimeoutCts = new();
                    localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
                }
            }

            localTimeoutCts.Dispose();
            composite.Dispose();

            Logger.LogDebug("Download {requestId} ready", requestId);
        }
        catch (TaskCanceledException)
        {
            try
            {
                await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                alreadyCancelled = true;
            }
            catch
            {
                // ignore whatever happens here
            }

            throw;
        }
        finally
        {
            if (downloadCt.IsCancellationRequested && !alreadyCancelled)
            {
                try
                {
                    await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore whatever happens here
                }
            }
            _orchestrator.ClearDownloadRequest(requestId);
        }
    }
}