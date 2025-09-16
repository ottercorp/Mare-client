using MareSynchronos.API.Routes;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;

namespace MareSynchronos.WebAPI.SignalR;

public sealed class TokenProvider : IDisposable, IMediatorSubscriber
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TokenProvider> _logger;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ConcurrentDictionary<JwtIdentifier, string> _tokenCache = new();

    public TokenProvider(ILogger<TokenProvider> logger, ServerConfigurationManager serverManager, DalamudUtilService dalamudUtil, MareMediator mareMediator, HttpClient httpClient)
    {
        _logger = logger;
        _serverManager = serverManager;
        _dalamudUtil = dalamudUtil;
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Mediator = mareMediator;
        _httpClient = httpClient;
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });
    }

    public MareMediator Mediator { get; }

    private JwtIdentifier? _lastJwtIdentifier;

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    public async Task<string> GetNewToken(bool isRenewal, JwtIdentifier identifier, CancellationToken ct)
    {
        Uri tokenUri;
        string response = string.Empty;
        HttpResponseMessage result;

        try
        {
            if (!isRenewal)
            {
                _logger.LogDebug("GetNewToken: Requesting");
                {
                    tokenUri = MareAuth.AuthWithOauthFullPath(new Uri(_serverManager.CurrentApiUrl
                        .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                        .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
                    request.Content = new FormUrlEncodedContent([
                        new KeyValuePair<string, string>("uid", identifier.UID),
                        new KeyValuePair<string, string>("charaIdent", identifier.CharaHash),
                        new KeyValuePair<string, string>("nameWithWorld", identifier.NameWithWorld),
                        new KeyValuePair<string, string>("machineId", DalamudUtilService.GetDeviceId().GetHash256()),
                        new KeyValuePair<string, string>("aidHash",  identifier.AidHash)
                        ]);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identifier.SecretKeyOrOAuth);
                    _logger.LogInformation("Sending OAuth Request to server with auth {auth}", string.Join("", identifier.SecretKeyOrOAuth.Take(10)));
                    result = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogDebug("GetNewToken: Renewal");

                tokenUri = MareAuth.RenewTokenFullPath(new Uri(_serverManager.CurrentApiUrl
                    .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                    .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                HttpRequestMessage request = new(HttpMethod.Get, tokenUri.ToString());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenCache[identifier]);
                result = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            }

            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            _tokenCache[identifier] = response;
        }
        catch (HttpRequestException ex)
        {
            _tokenCache.TryRemove(identifier, out _);

            _logger.LogError(ex, "GetNewToken: Failure to get token : {response} : ", response);

            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (isRenewal)
                    Mediator.Publish(new NotificationMessage("更新令牌时发生错误", "无法更新你的令牌. 请手动连接到Mare.",
                    NotificationType.Error));
                else
                    Mediator.Publish(new NotificationMessage("生成令牌时发生错误", "无法生成你的令牌. 查看Mare主界面 /mare 查看错误详情.",
                    NotificationType.Error));
                Mediator.Publish(new DisconnectedMessage());
                throw new MareAuthFailureException(response);
            }
            if (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                Mediator.Publish(new NotificationMessage(isRenewal ?"更新令牌时发生错误" : "生成令牌时发生错误", ex.Message,
                    NotificationType.Error));
                throw new MareAuthFailureException(response);
            }

            throw;
        }

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(response);
        _logger.LogTrace("GetNewToken: JWT {token}", response);
        _logger.LogDebug("GetNewToken: Valid until {date}, ValidClaim until {date}", jwtToken.ValidTo,
                new DateTime(long.Parse(jwtToken.Claims.Single(c => string.Equals(c.Type, "expiration_date", StringComparison.Ordinal)).Value), DateTimeKind.Utc));
        var dateTimeMinus10 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
        var dateTimePlus10 = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10));
        var tokenTime = jwtToken.ValidTo.Subtract(TimeSpan.FromHours(6));
        if (tokenTime <= dateTimeMinus10 || tokenTime >= dateTimePlus10)
        {
            _tokenCache.TryRemove(identifier, out _);
            Mediator.Publish(new NotificationMessage("系统时间无效", "系统时间无效. " +
                "Mare需要准确的电脑时间来运行 " +
                "请正确设置你的电脑时区并同步时间.",
                NotificationType.Error));
            throw new InvalidOperationException($"JwtToken 晚于 DateTime.UtcNow, DateTime.UtcNow 可能有误. DateTime.UtcNow 为 {DateTime.UtcNow}, JwtToken.ValidTo 为 {jwtToken.ValidTo}");
        }
        return response;
    }

    private async Task<JwtIdentifier?> GetIdentifier()
    {
        JwtIdentifier jwtIdentifier;
        try
        {
            var playerIdentifier = await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false);
            var nameWithWorld = await _dalamudUtil.GetPlayerNameWithWorldAsync().ConfigureAwait(false);
            var aidHash = await _dalamudUtil.GetPlayerAidHashedAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(playerIdentifier))
            {
                _logger.LogTrace("GetIdentifier: PlayerIdentifier was null, returning last identifier {identifier}", _lastJwtIdentifier);
                return _lastJwtIdentifier;
            }

            if (_serverManager.CurrentServer.UseOAuth2)
            {
                var (OAuthToken, UID) = _serverManager.GetOAuth2(out _)
                    ?? throw new InvalidOperationException("Requested OAuth2 but received null");

                jwtIdentifier = new(_serverManager.CurrentApiUrl,
                    playerIdentifier,
                    UID, OAuthToken, nameWithWorld.GetHash256(), aidHash);
            }
            else
            {
                var secretKey = _serverManager.GetSecretKey(out _)
                    ?? throw new InvalidOperationException("Requested SecretKey but received null");

                jwtIdentifier = new(_serverManager.CurrentApiUrl,
                                    playerIdentifier,
                                    string.Empty,
                                    secretKey,
                                    nameWithWorld.GetHash256(), aidHash);
            }
            _lastJwtIdentifier = jwtIdentifier;
        }
        catch (Exception ex)
        {
            if (_lastJwtIdentifier == null)
            {
                _logger.LogError("GetIdentifier: No last identifier found, aborting");
                return null;
            }

            _logger.LogWarning(ex, "GetIdentifier: Could not get JwtIdentifier for some reason or another, reusing last identifier {identifier}", _lastJwtIdentifier);
            jwtIdentifier = _lastJwtIdentifier;
        }

        _logger.LogDebug("GetIdentifier: Using identifier {identifier}", jwtIdentifier);
        return jwtIdentifier;
    }

    public async Task<string?> GetToken()
    {
        JwtIdentifier? jwtIdentifier = await GetIdentifier().ConfigureAwait(false);
        if (jwtIdentifier == null) return null;

        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            return token;
        }

        throw new InvalidOperationException("No token present");
    }

    public async Task<string?> GetOrUpdateToken(CancellationToken ct)
    {
        JwtIdentifier? jwtIdentifier = await GetIdentifier().ConfigureAwait(false);
        if (jwtIdentifier == null) return null;

        bool renewal = false;
        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromMinutes(5)) > DateTime.UtcNow)
            {
                return token;
            }

            _logger.LogDebug("GetOrUpdate: Cached token requires renewal, token valid to: {valid}, UtcTime is {utcTime}", jwt.ValidTo, DateTime.UtcNow);
            renewal = true;
        }
        else
        {
            _logger.LogDebug("GetOrUpdate: Did not find token in cache, requesting a new one");
        }

        _logger.LogTrace("GetOrUpdate: Getting new token");
        return await GetNewToken(renewal, jwtIdentifier, ct).ConfigureAwait(false);
    }

    public async Task<bool> TryUpdateOAuth2LoginTokenAsync(ServerStorage currentServer, bool forced = false)
    {
        var oauth2 = _serverManager.GetOAuth2(out _);
        if (oauth2 == null) return false;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(oauth2.Value.OAuthToken);
        if (!forced)
        {
            if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromDays(7)) > DateTime.Now)
                return true;

            if (jwt.ValidTo < DateTime.UtcNow)
                return false;
        }

        var tokenUri = MareAuth.RenewOAuthTokenFullPath(new Uri(currentServer.ServerUri
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauth2.Value.OAuthToken);
        _logger.LogInformation("Sending Request to server with auth {auth}", string.Join("", oauth2.Value.OAuthToken.Take(10)));
        var result = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (!result.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not renew OAuth2 Login token, error code {error}", result.StatusCode);
            currentServer.OAuthToken = null;
            _serverManager.Save();
            return false;
        }

        var newToken = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
        currentServer.OAuthToken = newToken;
        _serverManager.Save();

        return true;
    }
}