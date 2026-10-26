#if ANDROID
using Microsoft.Extensions.Logging;
namespace StudySpace.App.Services;

// Release diagnostics contain event codes/types only; callers must not log study material or paths.
public sealed class AndroidLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)=>new AndroidLogger(categoryName);
    public void Dispose() { }
    private sealed class AndroidLogger(string category):ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Warning && level<LogLevel.None;
        public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
        {
            if(IsEnabled(level)) Android.Util.Log.WriteLine(level>=LogLevel.Error ? Android.Util.LogPriority.Error : Android.Util.LogPriority.Warn,
                "StudySpace",$"{category}: {formatter(state,null)}");
        }
    }
}
#endif
