namespace JebaitedPlayer.Diagnostics;

/// <summary>Statička fasada nad jednim <see cref="DiagLogger"/>-om. Bez pokrenutog loggera svi pozivi su bezopasni no-op.</summary>
public static class DiagLog
{
    private static DiagLogger? _logger;

    public static DiagLogger? Current => _logger;

    public static string SessionId => _logger?.SessionId ?? string.Empty;

    public static void Start(string logsRoot, string version)
    {
        if (_logger != null)
        {
            return;
        }

        var logger = new DiagLogger(logsRoot, version);
        _logger = logger;
        logger.Start();
    }

    public static void Stop()
    {
        DiagLogger? logger = _logger;
        _logger = null;
        logger?.Stop();
    }

    /// <summary>Pad procesa: sačuvaj što je u redu, ali ostavi marker (neuredan kraj).</summary>
    public static void Abort() => _logger?.Abort();

    public static void Debug(string category, string type, string component, string message, params (string, object?)[] data) =>
        _logger?.Log(LogLevel.Debug, category, type, component, message, data);

    public static void Info(string category, string type, string component, string message, params (string, object?)[] data) =>
        _logger?.Log(LogLevel.Info, category, type, component, message, data);

    public static void Warn(string category, string type, string component, string message, params (string, object?)[] data) =>
        _logger?.Log(LogLevel.Warning, category, type, component, message, data);

    public static void Error(string category, string type, string component, string message, Exception? ex = null, params (string, object?)[] data) =>
        _logger?.Log(LogLevel.Error, category, type, component, message, ex, data);

    public static void Critical(string category, string type, string component, string message, Exception? ex = null, params (string, object?)[] data) =>
        _logger?.Log(LogLevel.Critical, category, type, component, message, ex, data);
}
