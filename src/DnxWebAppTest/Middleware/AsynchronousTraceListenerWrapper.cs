using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DnxWebAppTest.Middleware
{
    public class AsynchronousTraceListenerWrapper : TraceListener
    {

        internal const int DefaultBufferSize = 30000;
        private static readonly Lazy<TraceListener> reportingTraceListener = new Lazy<TraceListener>(() => new DefaultTraceListener());
        private readonly TraceListener wrappedTraceListener;
        private readonly bool ownsWrappedTraceListener;
        private readonly BlockingCollection<Action<TraceListener>> requests;
        private readonly int maxDegreeOfParallelism;
        private readonly TimeSpan disposeTimeout;
        private readonly Task asyncProcessingTask;
        private CancellationTokenSource closeSource;
        private int closed;

        public AsynchronousTraceListenerWrapper(
            TraceListener wrappedTraceListener,
            bool ownsWrappedTraceListener = true,
            int? bufferSize = DefaultBufferSize,
            int? maxDegreeOfParallelism = null,
            TimeSpan? disposeTimeout = null)
        {
            //Guard.ArgumentNotNull(wrappedTraceListener, "wrappedTraceListener");
            //CheckBufferSize(bufferSize);
            //CheckMaxDegreeOfParallelism(maxDegreeOfParallelism);
            //CheckDisposeTimeout(disposeTimeout);

            this.wrappedTraceListener = wrappedTraceListener;
            this.ownsWrappedTraceListener = ownsWrappedTraceListener;
            this.disposeTimeout = disposeTimeout ?? Timeout.InfiniteTimeSpan;

            this.closeSource = new CancellationTokenSource();
            this.requests = bufferSize != null ? new BlockingCollection<Action<TraceListener>>(bufferSize.Value) : new BlockingCollection<Action<TraceListener>>();

            if (this.wrappedTraceListener.IsThreadSafe)
            {
                this.maxDegreeOfParallelism = maxDegreeOfParallelism.HasValue ? maxDegreeOfParallelism.Value : Environment.ProcessorCount;
                this.asyncProcessingTask = Task.Factory.StartNew(this.ProcessRequestsInParallel, CancellationToken.None, TaskCreationOptions.HideScheduler | TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            else
            {
                this.asyncProcessingTask = Task.Factory.StartNew(this.ProcessRequests, CancellationToken.None, TaskCreationOptions.HideScheduler | TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        private void ProcessRequests()
        {
            var token = this.closeSource.Token;
            var shouldLock = !(this.ownsWrappedTraceListener || this.wrappedTraceListener.IsThreadSafe);

            try
            {
                foreach (var request in this.requests.GetConsumingEnumerable(token))
                {
                    try
                    {
                        // It is possible that the trace listener is used elsewhere, so it must be careful about thread safety.
                        if (shouldLock)
                        {
                            lock (this.wrappedTraceListener)
                            {
                                request(this.wrappedTraceListener);
                            }
                        }
                        else
                        {
                            request(this.wrappedTraceListener);
                        }
                    }
                    catch (Exception e)
                    {
                        //reportingTraceListener.Value.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.ExceptionUnknownErrorPerformingAsynchronousOperation, this.Name, e));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // expected when the listener is closed
            }
        }

        private void ProcessRequestsInParallel()
        {
            var token = this.closeSource.Token;

            try
            {
                var partitioner = Partitioner.Create(this.requests.GetConsumingEnumerable(token), EnumerablePartitionerOptions.NoBuffering);
                Parallel.ForEach(
                    partitioner,
                    new ParallelOptions { CancellationToken = token, TaskScheduler = TaskScheduler.Default, MaxDegreeOfParallelism = this.maxDegreeOfParallelism },
                    request =>
                    {
                        try
                        {
                            request(this.wrappedTraceListener);
                        }
                        catch (Exception e)
                        {
                            //reportingTraceListener.Value.WriteLine(string.Format(CultureInfo.CurrentCulture, Resources.ExceptionUnknownErrorPerformingAsynchronousOperation, this.Name, e));
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                // expected when the listener is closed
            }
        }

    }
}
