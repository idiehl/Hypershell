import {
  command,
  createLibrary,
  example,
  parameter,
} from "../catalog-utils.mjs";

const commands = [
  command({
    name: "CancellationTokenSource.CancelAfter",
    description: "Schedule a cooperative cancellation request after a delay.",
    definition:
      "Starts or resets a timer that calls Cancel on the source after the requested duration. Work must observe the associated token for cancellation to take effect.",
    moduleName: "System.Threading",
    action: "Cancel",
    object: "Cancellation source",
    template: "{source}.CancelAfter({delay});",
    parameters: [
      parameter("source", "source", "A CancellationTokenSource instance.", "CancellationTokenSource"),
      parameter("delay", "delay", "The delay before cancellation is requested.", "TimeSpan"),
    ],
    examples: [
      example(
        "Apply a two-second operation budget",
        String.raw`using var cancellation = new CancellationTokenSource();
cancellation.CancelAfter(TimeSpan.FromSeconds(2));
try
{
    await Task.Delay(TimeSpan.FromSeconds(10), cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation budget expired.");
}`,
        "The delay observes the token and throws after the source's timer requests cancellation.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource.cancelafter",
    keywords: ["cancellation", "timeout", "token", "CancelAfter"],
  }),
  command({
    name: "CancellationTokenSource.CreateLinkedTokenSource",
    description: "Create a token canceled when any source token is canceled.",
    definition:
      "Creates a new source linked to one or more existing tokens. Its token observes cancellation from any linked source and can also be canceled independently.",
    moduleName: "System.Threading",
    action: "Create",
    object: "Cancellation source",
    template: "using var linked = CancellationTokenSource.CreateLinkedTokenSource({tokens});",
    parameters: [
      parameter("tokens", "tokens", "One or more CancellationToken values.", "CancellationToken[]"),
    ],
    examples: [
      example(
        "Combine caller cancellation and a local timeout",
        String.raw`using var caller = new CancellationTokenSource();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    caller.Token,
    timeout.Token);
try
{
    await Task.Delay(TimeSpan.FromSeconds(5), linked.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Caller canceled or timeout expired.");
}`,
        "The linked token is canceled by whichever source requests cancellation first.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.cancellationtokensource.createlinkedtokensource",
    keywords: ["cancellation", "linked token", "timeout"],
  }),
  command({
    name: "Task.WaitAsync",
    description: "Wait for a task with a timeout or cancellation boundary.",
    definition:
      "Returns a task that mirrors the original operation when it completes in time, but throws TimeoutException or OperationCanceledException when the selected boundary wins.",
    moduleName: "System.Threading.Tasks",
    action: "Wait",
    object: "Task",
    template: "var result = await {task}.WaitAsync({timeout});",
    parameters: [
      parameter("task", "task", "The Task<T> to await.", "Task<T>"),
      parameter("timeout", "timeout", "The maximum wait duration.", "TimeSpan"),
    ],
    examples: [
      example(
        "Bound an asynchronous operation",
        String.raw`var operation = Task.Run(async () =>
{
    await Task.Delay(25);
    return "ready";
});
var result = await operation.WaitAsync(TimeSpan.FromSeconds(1));
Console.WriteLine(result);`,
        "The original task completes before the one-second boundary, so its result is returned.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync",
    keywords: ["Task", "timeout", "cancellation", "await"],
  }),
  command({
    name: "Parallel.ForEachAsync",
    description: "Process sequence elements concurrently with bounded parallelism.",
    definition:
      "Invokes an asynchronous delegate for each source element while respecting ParallelOptions such as MaxDegreeOfParallelism and CancellationToken.",
    moduleName: "System.Threading.Tasks.Parallel",
    action: "Run",
    object: "Parallel loop",
    template:
      "await Parallel.ForEachAsync({source}, {options}, async ({item}, cancellationToken) =>\n{\n    {body}\n});",
    parameters: [
      parameter("source", "source", "The sequence to process.", "IEnumerable<T>"),
      parameter("options", "options", "Parallelism and cancellation settings.", "ParallelOptions"),
      parameter("item", "item", "The current-element identifier.", "identifier"),
      parameter("body", "body", "Asynchronous statements for one element.", "C# statements"),
    ],
    examples: [
      example(
        "Run a bounded set of simulated requests",
        String.raw`var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 3
};
await Parallel.ForEachAsync(
    Enumerable.Range(1, 8),
    options,
    async (item, cancellationToken) =>
    {
        await Task.Delay(20, cancellationToken);
        Console.WriteLine($"Completed {item}");
    });`,
        "At most three delegate bodies run at the same time while every input is eventually processed.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.tasks.parallel.foreachasync",
    keywords: ["parallel", "foreach", "async", "bounded concurrency"],
  }),
  command({
    name: "SemaphoreSlim.WaitAsync",
    description: "Asynchronously acquire one semaphore permit.",
    definition:
      "Waits until the semaphore count is positive, then atomically decrements it. Successful acquisition must be paired with Release, normally in a finally block.",
    moduleName: "System.Threading.SemaphoreSlim",
    action: "Synchronize",
    object: "Semaphore",
    template: "await {semaphore}.WaitAsync({cancellationToken});",
    parameters: [
      parameter("semaphore", "semaphore", "The SemaphoreSlim to acquire.", "SemaphoreSlim"),
      parameter("cancellationToken", "cancellationToken", "A token that can cancel the wait.", "CancellationToken"),
    ],
    examples: [
      example(
        "Protect a bounded asynchronous section",
        String.raw`using var semaphore = new SemaphoreSlim(1, 1);
await semaphore.WaitAsync();
try
{
    await Task.Delay(20);
    Console.WriteLine("Exclusive section completed.");
}
finally
{
    semaphore.Release();
}`,
        "The permit is always returned even if the protected operation throws.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim.waitasync",
    keywords: ["semaphore", "lock", "async", "throttle"],
  }),
  command({
    name: "SemaphoreSlim.Release",
    description: "Return one or more permits to a semaphore.",
    definition:
      "Increases the semaphore count and allows waiting operations to acquire newly available permits. Releasing beyond the configured maximum throws SemaphoreFullException.",
    moduleName: "System.Threading.SemaphoreSlim",
    action: "Release",
    object: "Semaphore",
    template: "{semaphore}.Release();",
    parameters: [
      parameter("semaphore", "semaphore", "A previously acquired SemaphoreSlim.", "SemaphoreSlim"),
    ],
    examples: [
      example(
        "Pair release with successful acquisition",
        String.raw`using var gate = new SemaphoreSlim(2, 2);
await gate.WaitAsync();
try
{
    Console.WriteLine($"Permits while held: {gate.CurrentCount}");
}
finally
{
    gate.Release();
}`,
        "Release occurs exactly once after WaitAsync succeeds.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim.release",
    keywords: ["semaphore", "release", "permit", "throttle"],
  }),
  command({
    name: "ConcurrentDictionary<TKey,TValue>.GetOrAdd",
    description: "Get an existing value or atomically publish a generated value.",
    definition:
      "Returns the value already associated with a key or invokes a factory and attempts to add its result. Under contention, the factory may run more than once even though only one value is stored.",
    moduleName: "System.Collections.Concurrent",
    action: "Get",
    object: "Concurrent dictionary",
    template: "var value = {dictionary}.GetOrAdd({key}, {valueFactory});",
    parameters: [
      parameter("dictionary", "dictionary", "A ConcurrentDictionary<TKey,TValue>.", "ConcurrentDictionary<TKey,TValue>"),
      parameter("key", "key", "The lookup key.", "TKey"),
      parameter("valueFactory", "valueFactory", "A function that creates a candidate value.", "Func<TKey,TValue>"),
    ],
    examples: [
      example(
        "Cache a computed label",
        String.raw`var cache = new ConcurrentDictionary<int, string>();
var value = cache.GetOrAdd(42, key => $"item-{key}");
Console.WriteLine(value);`,
        "The key receives one published value while GetOrAdd returns that value to the caller.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd",
    keywords: ["ConcurrentDictionary", "cache", "atomic", "GetOrAdd"],
  }),
  command({
    name: "ConcurrentDictionary<TKey,TValue>.AddOrUpdate",
    description: "Atomically add a new value or update the current value.",
    definition:
      "Uses one factory for a missing key and another for an existing key, retrying when concurrent changes invalidate an attempted update. Factories may execute multiple times.",
    moduleName: "System.Collections.Concurrent",
    action: "Add",
    object: "Concurrent dictionary",
    template:
      "var value = {dictionary}.AddOrUpdate({key}, {addValueFactory}, {updateValueFactory});",
    parameters: [
      parameter("dictionary", "dictionary", "A ConcurrentDictionary<TKey,TValue>.", "ConcurrentDictionary<TKey,TValue>"),
      parameter("key", "key", "The key to add or update.", "TKey"),
      parameter("addValueFactory", "addValueFactory", "A factory for a missing key.", "Func<TKey,TValue>"),
      parameter("updateValueFactory", "updateValueFactory", "A factory receiving the key and current value.", "Func<TKey,TValue,TValue>"),
    ],
    examples: [
      example(
        "Increment a concurrent counter",
        String.raw`var counts = new ConcurrentDictionary<string, int>();
foreach (var name in new[] { "build", "test", "build" })
{
    counts.AddOrUpdate(name, 1, (_, current) => current + 1);
}
Console.WriteLine(counts["build"]);`,
        "The add path starts a key at one, while the update path increments its current value.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.addorupdate",
    keywords: ["ConcurrentDictionary", "atomic", "counter", "AddOrUpdate"],
  }),
  command({
    name: "ConcurrentDictionary<TKey,TValue>.TryRemove",
    description: "Atomically remove a key and retrieve its value.",
    definition:
      "Attempts to remove the key-value pair visible at the time of the operation, returning false when the key is absent.",
    moduleName: "System.Collections.Concurrent",
    action: "Release",
    object: "Concurrent dictionary",
    template: "var removed = {dictionary}.TryRemove({key}, out var value);",
    parameters: [
      parameter("dictionary", "dictionary", "A ConcurrentDictionary<TKey,TValue>.", "ConcurrentDictionary<TKey,TValue>"),
      parameter("key", "key", "The key to remove.", "TKey"),
    ],
    examples: [
      example(
        "Consume a pending work item",
        String.raw`var pending = new ConcurrentDictionary<int, string>(
    new[] { new KeyValuePair<int, string>(7, "package") });
if (pending.TryRemove(7, out var step))
{
    Console.WriteLine(step);
}`,
        "The successful operation removes and returns the same key-value pair atomically.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.tryremove",
    keywords: ["ConcurrentDictionary", "remove", "atomic"],
  }),
  command({
    name: "ConcurrentQueue<T>.Enqueue",
    description: "Add an item to a thread-safe FIFO queue.",
    definition:
      "Places an item at the tail of a ConcurrentQueue<T> without requiring caller-managed locking.",
    moduleName: "System.Collections.Concurrent",
    action: "Add",
    object: "Concurrent queue",
    template: "{queue}.Enqueue({item});",
    parameters: [
      parameter("queue", "queue", "A ConcurrentQueue<T> instance.", "ConcurrentQueue<T>"),
      parameter("item", "item", "The item to append.", "T"),
    ],
    examples: [
      example(
        "Queue work from several tasks",
        String.raw`var queue = new ConcurrentQueue<int>();
await Task.WhenAll(
    Enumerable.Range(1, 4)
        .Select(value => Task.Run(() => queue.Enqueue(value))));
Console.WriteLine($"Queued: {queue.Count}");`,
        "ConcurrentQueue accepts additions from multiple tasks without an external lock.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentqueue-1.enqueue",
    keywords: ["ConcurrentQueue", "FIFO", "enqueue", "thread safe"],
  }),
  command({
    name: "ConcurrentQueue<T>.TryDequeue",
    description: "Try to remove the oldest item from a thread-safe queue.",
    definition:
      "Atomically removes and returns the head item when available, or returns false when the queue is empty.",
    moduleName: "System.Collections.Concurrent",
    action: "Get",
    object: "Concurrent queue",
    template: "var available = {queue}.TryDequeue(out var item);",
    parameters: [
      parameter("queue", "queue", "A ConcurrentQueue<T> instance.", "ConcurrentQueue<T>"),
    ],
    examples: [
      example(
        "Drain queued work",
        String.raw`var queue = new ConcurrentQueue<string>(
    new[] { "build", "test", "package" });
while (queue.TryDequeue(out var step))
{
    Console.WriteLine(step);
}`,
        "Each successful call removes the oldest remaining value until the queue is empty.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentqueue-1.trydequeue",
    keywords: ["ConcurrentQueue", "FIFO", "dequeue", "thread safe"],
  }),
  command({
    name: "Channel.CreateBounded<T>",
    description: "Create an asynchronous producer-consumer channel with capacity.",
    definition:
      "Creates a channel whose buffer obeys a maximum capacity and configured full-mode behavior, enabling backpressure between producers and consumers.",
    moduleName: "System.Threading.Channels",
    action: "Create",
    object: "Channel",
    template: "var channel = Channel.CreateBounded<{type}>({capacity});",
    parameters: [
      parameter("type", "type", "The message type.", "type name"),
      parameter("capacity", "capacity", "The maximum buffered item count.", "int"),
    ],
    examples: [
      example(
        "Create a small backpressure buffer",
        String.raw`var channel = Channel.CreateBounded<string>(2);
await channel.Writer.WriteAsync("build");
await channel.Writer.WriteAsync("test");
Console.WriteLine(await channel.Reader.ReadAsync());`,
        "The channel holds at most two unread strings, so additional producers wait once it is full.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.channels.channel.createbounded",
    keywords: ["channel", "producer consumer", "backpressure", "queue"],
  }),
  command({
    name: "ChannelWriter<T>.WriteAsync",
    description: "Asynchronously write an item to a channel.",
    definition:
      "Waits for capacity when required, then writes one item unless the channel has completed or cancellation is requested.",
    moduleName: "System.Threading.Channels",
    action: "Write",
    object: "Channel",
    template: "await {writer}.WriteAsync({item}, {cancellationToken});",
    parameters: [
      parameter("writer", "writer", "A ChannelWriter<T>.", "ChannelWriter<T>"),
      parameter("item", "item", "The item to publish.", "T"),
      parameter("cancellationToken", "cancellationToken", "A token that can cancel the wait.", "CancellationToken"),
    ],
    examples: [
      example(
        "Publish and complete a channel",
        String.raw`var channel = Channel.CreateUnbounded<int>();
await channel.Writer.WriteAsync(42);
channel.Writer.Complete();
Console.WriteLine(await channel.Reader.ReadAsync());`,
        "The writer publishes one value and then signals that no later values will arrive.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.channels.channelwriter-1.writeasync",
    keywords: ["channel", "writer", "producer", "async"],
  }),
  command({
    name: "ChannelReader<T>.ReadAllAsync",
    description: "Asynchronously enumerate channel items until completion.",
    definition:
      "Returns an IAsyncEnumerable<T> that waits for new items and ends after the writer completes and every buffered item has been consumed.",
    moduleName: "System.Threading.Channels",
    action: "Read",
    object: "Channel",
    template: "await foreach (var item in {reader}.ReadAllAsync({cancellationToken}))\n{\n    {body}\n}",
    parameters: [
      parameter("reader", "reader", "A ChannelReader<T>.", "ChannelReader<T>"),
      parameter("cancellationToken", "cancellationToken", "A token that can cancel enumeration.", "CancellationToken"),
      parameter("body", "body", "Statements that process each item.", "C# statements"),
    ],
    examples: [
      example(
        "Consume every published value",
        String.raw`var channel = Channel.CreateUnbounded<string>();
await channel.Writer.WriteAsync("build");
await channel.Writer.WriteAsync("test");
channel.Writer.Complete();
await foreach (var step in channel.Reader.ReadAllAsync())
{
    Console.WriteLine(step);
}`,
        "Enumeration ends naturally after completion because every buffered item has been read.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.channels.channelreader-1.readallasync",
    keywords: ["channel", "reader", "consumer", "IAsyncEnumerable"],
  }),
  command({
    name: "Interlocked.Increment",
    description: "Atomically increment a shared integer.",
    definition:
      "Adds one to a variable as a single atomic operation and returns the new value, preventing lost updates between threads.",
    moduleName: "System.Threading.Interlocked",
    action: "Synchronize",
    object: "Shared value",
    template: "var updated = Interlocked.Increment(ref {location});",
    parameters: [
      parameter("location", "location", "The int or long storage location to update.", "ref int"),
    ],
    examples: [
      example(
        "Count completions across tasks",
        String.raw`var completed = 0;
await Task.WhenAll(
    Enumerable.Range(1, 100)
        .Select(_ => Task.Run(() => Interlocked.Increment(ref completed))));
Console.WriteLine(completed);`,
        "All one hundred increments are retained even though several tasks update the same variable concurrently.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.interlocked.increment",
    keywords: ["Interlocked", "atomic", "increment", "counter"],
  }),
  command({
    name: "Interlocked.Exchange",
    description: "Atomically replace a shared value.",
    definition:
      "Stores a new value in a location as one atomic operation and returns the value that was previously present.",
    moduleName: "System.Threading.Interlocked",
    action: "Synchronize",
    object: "Shared value",
    template: "var previous = Interlocked.Exchange(ref {location}, {value});",
    parameters: [
      parameter("location", "location", "The shared storage location.", "ref T"),
      parameter("value", "value", "The replacement value.", "T"),
    ],
    examples: [
      example(
        "Publish a one-time state change",
        String.raw`var state = 0;
var previous = Interlocked.Exchange(ref state, 1);
Console.WriteLine($"Previous={previous}; Current={state}");`,
        "The caller receives the old value while the shared location changes atomically.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.interlocked.exchange",
    keywords: ["Interlocked", "atomic", "exchange", "state"],
  }),
  command({
    name: "Interlocked.CompareExchange",
    description: "Atomically replace a value only when it matches an expected value.",
    definition:
      "Compares a shared location with a comparand and stores a replacement only when equal, returning the location's original value either way.",
    moduleName: "System.Threading.Interlocked",
    action: "Compare",
    object: "Shared value",
    template:
      "var observed = Interlocked.CompareExchange(ref {location}, {value}, {comparand});",
    parameters: [
      parameter("location", "location", "The shared storage location.", "ref T"),
      parameter("value", "value", "The replacement value.", "T"),
      parameter("comparand", "comparand", "The expected current value.", "T"),
    ],
    examples: [
      example(
        "Perform a one-time initialization transition",
        String.raw`var state = 0;
var observed = Interlocked.CompareExchange(
    ref state,
    value: 1,
    comparand: 0);
Console.WriteLine(observed == 0 ? "Won initialization" : "Already initialized");`,
        "Only a caller observing state zero can atomically change it to one.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.interlocked.compareexchange",
    keywords: ["Interlocked", "atomic", "compare exchange", "CAS"],
  }),
  command({
    name: "PeriodicTimer.WaitForNextTickAsync",
    description: "Asynchronously wait for the next periodic timer tick.",
    definition:
      "Waits until the next timer period, cancellation, or disposal. Only one consumer may wait on a PeriodicTimer at a time.",
    moduleName: "System.Threading.PeriodicTimer",
    action: "Wait",
    object: "Periodic timer",
    template: "var ticked = await {timer}.WaitForNextTickAsync({cancellationToken});",
    parameters: [
      parameter("timer", "timer", "A PeriodicTimer instance.", "PeriodicTimer"),
      parameter("cancellationToken", "cancellationToken", "A token that can cancel the wait.", "CancellationToken"),
    ],
    examples: [
      example(
        "Run three short periodic ticks",
        String.raw`using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
for (var tick = 1; tick <= 3; tick++)
{
    if (!await timer.WaitForNextTickAsync()) break;
    Console.WriteLine($"Tick {tick}");
}`,
        "Each iteration waits asynchronously for the next period and stops early if the timer is disposed.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.threading.periodictimer.waitfornexttickasync",
    keywords: ["timer", "periodic", "tick", "async"],
  }),
  command({
    name: "Lazy<T>.Value",
    description: "Get a lazily initialized value.",
    definition:
      "Returns the cached value, invoking the configured value factory on first access according to the selected thread-safety mode.",
    moduleName: "System.Lazy<T>",
    action: "Get",
    object: "Lazy value",
    template: "var value = {lazy}.Value;",
    parameters: [
      parameter("lazy", "lazy", "A Lazy<T> instance.", "Lazy<T>"),
    ],
    examples: [
      example(
        "Initialize expensive data on demand",
        String.raw`var lazyValue = new Lazy<string>(() =>
{
    Console.WriteLine("Factory ran.");
    return string.Join(",", Enumerable.Range(1, 5));
});
Console.WriteLine("Before access.");
Console.WriteLine(lazyValue.Value);
Console.WriteLine(lazyValue.Value);`,
        "The factory runs during the first Value access and its result is reused by the second.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.lazy-1.value",
    keywords: ["Lazy", "deferred", "initialization", "cache"],
  }),
];

export default createLibrary({
  id: "csharp-concurrency",
  displayName: ".NET concurrency and coordination",
  description:
    "Dependency-free .NET 8 APIs for cancellation, task boundaries, bounded parallelism, synchronization, concurrent collections, channels, atomics, and timers.",
  category: "Concurrency",
  keywords: ["async", "parallel", "threading", "channels", "synchronization"],
  objectDescriptions: {
    "Cancellation source":
      "A CancellationTokenSource owns a cooperative cancellation signal observed through one or more CancellationToken values.",
    Channel:
      "A Channel<T> is an asynchronous producer-consumer queue with separate reader and writer interfaces and optional backpressure.",
    "Concurrent dictionary":
      "ConcurrentDictionary<TKey,TValue> provides thread-safe key-value operations, including compound atomic updates.",
    "Concurrent queue":
      "ConcurrentQueue<T> is a thread-safe first-in, first-out collection.",
    "Lazy value":
      "Lazy<T> defers value construction until first access and then caches the result.",
    "Parallel loop":
      "A parallel loop processes several source elements concurrently while respecting configured resource and cancellation limits.",
    "Periodic timer":
      "PeriodicTimer provides an asynchronous stream of time-based ticks to a single consumer.",
    Semaphore:
      "SemaphoreSlim controls how many concurrent operations may hold a permit to enter a protected or throttled region.",
    "Shared value":
      "A shared value is storage accessed by more than one thread or task and therefore requires atomic or synchronized updates.",
    Task:
      "A Task represents asynchronous work that eventually completes successfully, fails, or is canceled.",
  },
  commands,
});
