using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TaskCore.Logging
{
    public sealed class AsyncLogBuffer : IDisposable
    {
        private static readonly Lazy<AsyncLogBuffer> _instance = new(() => new AsyncLogBuffer());
        public static AsyncLogBuffer Instance => _instance.Value;

        private readonly Channel<LogEntry> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _consumerTask;

        private AsyncLogBuffer()
        {
            var options = new BoundedChannelOptions(10000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            };

            _channel = Channel.CreateBounded<LogEntry>(options);
            _consumerTask = Task.Run(ProcessQueueAsync);
        }

        public bool TryEnqueue(in LogEntry entry)
        {
            return _channel.Writer.TryWrite(entry);
        }

        private async Task ProcessQueueAsync()
        {
            var reader = _channel.Reader;
            var categoryBuffers = new ConcurrentDictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    while (reader.TryRead(out var entry))
                    {
                        var formatted = entry.ToFormattedString();
                        var sb = categoryBuffers.GetOrAdd(entry.Category, _ => new StringBuilder());
                        sb.AppendLine(formatted);
                    }

                    // Flush all buffered categories to active session files asynchronously
                    foreach (var kvp in categoryBuffers)
                    {
                        var category = kvp.Key;
                        var sb = kvp.Value;

                        if (sb.Length == 0) continue;

                        var textToAppend = sb.ToString();
                        sb.Clear();

                        var filePath = LogRotationService.GetLogFilePath(category);
                        await File.AppendAllTextAsync(filePath, textToAppend, Encoding.UTF8, _cts.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
        }

        public void FlushSync()
        {
            try
            {
                while (_channel.Reader.TryRead(out var entry))
                {
                    var formatted = entry.ToFormattedString();
                    var filePath = LogRotationService.GetLogFilePath(entry.Category);
                    File.AppendAllText(filePath, formatted + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            FlushSync();
        }
    }
}
