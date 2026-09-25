# NKitStream

A block-based buffering layer for sequential data sources. `BlockBufferedStream` wraps any forward-only source and adds **peek**, **seek**, **rewind** and **explicit memory release** — all without requiring the source itself to support seeking.

## Design

Reads pass straight through to the source by default with **zero overhead** — no blocks allocated, no copies made. When you know a rewind is coming, call `BeginBuffering()` to start caching reads into fixed-size blocks rented from `ArrayPool<byte>.Shared`. Call `EndBuffering()` when done; cached data ahead of the read cursor drains automatically, then the stream returns to pass-through mode.

Three cursors track the stream state:

| Cursor       | Property           | Description                                                    |
|--------------|--------------------|----------------------------------------------------------------|
| **Position** | `Position`         | Current read head (absolute byte offset).                      |
| **Floor**    | `Floor`            | Earliest retained offset. Seeking before this throws.          |
| **Buffered** | `Buffered`         | One past the highest byte fetched from the source into cache.  |

`Floor` is the single safety gate — there are no separate mode checks on `Seek` or `Peek`. In pass-through mode `Floor` tracks `Position`, so backward seeks naturally fail. In buffered mode `Floor` stays where you last released, allowing full backward access to cached data.

## Getting started

Subclass `BlockBufferedStream` and implement `ReadSourceAsync`:

```csharp
public class MyStream : BlockBufferedStream
{
    private readonly Stream _inner;

    public MyStream(Stream inner, int blockSize = 65536)
        : base(blockSize)
    {
        _inner = inner;
    }

    protected override async ValueTask<int> ReadSourceAsync(
        Memory<byte> buffer, CancellationToken ct)
    {
        return await _inner.ReadAsync(buffer, ct);
    }
}
```

That's it. All caching, spanning reads, and memory management are handled by the base class.

### Enabling synchronous reads

The async API (`ReadAsync`, `PeekAsync`) is always available. To also use the synchronous `Read(Span<byte>)` and `Peek(Span<byte>)` methods, override `ReadSource`:

```csharp
public class MySyncStream : BlockBufferedStream
{
    private readonly Stream _inner;

    public MySyncStream(Stream inner, int blockSize = 65536)
        : base(blockSize)
    {
        _inner = inner;
    }

    protected override async ValueTask<int> ReadSourceAsync(
        Memory<byte> buffer, CancellationToken ct)
    {
        return await _inner.ReadAsync(buffer, ct);
    }

    protected override int ReadSource(Span<byte> buffer)
    {
        return _inner.Read(buffer);
    }
}
```

If `ReadSource` is not overridden, calling `Read` or `Peek` throws `NotSupportedException`.

## Features

### Pass-through reads (default)

When buffering is off, reads go directly to the source. No blocks are allocated and no data is copied through an intermediate buffer.

```csharp
using var stream = new MyStream(source);

var buf = new byte[4096];
int n = await stream.ReadAsync(buf, ct);
// Data came straight from the source — zero overhead.

// Synchronous equivalent (requires ReadSource override):
int n2 = stream.Read(buf);
```

In pass-through mode `Floor` advances with `Position` after every read, so backward seeks are not possible. This is by design — no data is cached, so there is nothing to re-read.

### Peek

Read bytes at the current position without advancing it. `Position` is never changed by `PeekAsync` or `Peek`.

```csharp
var header = new byte[4];
int n = await stream.PeekAsync(header, ct);
// stream.Position is unchanged — the same bytes come back on the next read.

// Synchronous equivalent:
int n2 = stream.Peek(header);
```

**Partial / end-of-source:** If fewer bytes remain than `buffer.Length`, peek returns only what is available. If the source is already exhausted and no cached data covers `Position`, it returns 0.

**Pass-through mode:** Because the source is forward-only, peeked data must be cached somewhere. `PeekAsync` temporarily allocates blocks to hold the data. Those blocks are auto-released on the very next `ReadAsync` call (which reads from the cache and then frees it), so the stream returns to zero-block pass-through immediately.

**Buffered mode:** The peeked data lives in the normal block cache and follows the same lifetime as any other buffered read — it stays until `Release`, `EndBuffering`, or `Dispose`.

### Buffered mode with rewind

Enable caching when you need to re-read data:

```csharp
stream.BeginBuffering();

long mark = stream.Mark();       // snapshot Position
await stream.ReadAsync(buf, ct); // cached in blocks
stream.Rewind(mark);             // restore Position — next read serves cached data
await stream.ReadAsync(buf, ct); // same bytes, no source hit

stream.EndBuffering();
// Blocks behind Position are released. Blocks ahead drain automatically,
// then the stream returns to pass-through mode.
```

### Seek

Move the read cursor to any absolute offset at or above `Floor`:

```csharp
stream.Seek(1024);
// Next ReadAsync fetches from offset 1024.
// If the data is cached it comes from blocks; otherwise from the source.
```

Forward seeks beyond `Buffered` are allowed — data between the old `Buffered` and the seek target is fetched from the source on the next read. Backward seeks are allowed as long as the target is at or above `Floor`.

### Explicit memory release

When you know earlier data will never be needed again, release it to free the underlying `ArrayPool` blocks:

```csharp
stream.Release(offset);
// Blocks whose data is entirely before `offset` are returned to the pool.
// Floor advances to `offset`.
```

This keeps memory bounded even when buffering large regions.

> **Block-aligned trimming.** Only blocks whose *entire* range falls below `offset` are freed. A block that straddles the offset is kept. For example, with a 64-byte block size, `Release(100)` frees the block at [0–64) but keeps the block at [64–128) because part of it is still above 100.

Calling `Release` with the same or lower offset a second time is a no-op.

### Seek-ahead-and-rewind pattern

A common pattern for format parsers — peek at data further in the stream, then rewind and continue sequentially:

```csharp
stream.BeginBuffering();

var header = new byte[50];
await stream.ReadAsync(header, ct);

long mark = stream.Mark();          // 50

stream.Seek(200);                    // jump ahead
var preview = new byte[20];
await stream.ReadAsync(preview, ct); // read at 200

stream.Rewind(mark);                 // back to 50
// Continue sequential reading — cached data serves first, then source resumes.
```

### Multiple buffering cycles

You can toggle buffering on and off multiple times. Each `EndBuffering()` drains remaining cached blocks; the next `BeginBuffering()` starts a fresh caching window:

```csharp
// Cycle 1: pass-through
await stream.ReadAsync(buf1, ct);

// Cycle 2: buffered
stream.BeginBuffering();
long m = stream.Mark();
await stream.ReadAsync(buf2, ct);
stream.Rewind(m);
stream.EndBuffering();

// Drain, then back to pass-through
await stream.ReadAsync(buf3, ct);

// Cycle 3: buffer again
stream.BeginBuffering();
// ...
```

## Behaviour reference

This section is a quick-look reference for edge cases and automatic vs manual actions.

### End-of-source and partial reads

The sync (`Read`/`Peek`) and async (`ReadAsync`/`PeekAsync`) methods behave identically in all cases below.

| Scenario | Read returns | Peek returns | `IsSourceExhausted` |
|----------|-------------|-------------|---------------------|
| Source has enough data | Up to `buffer.Length` bytes (may be fewer per source behaviour) | Same as Read but `Position` unchanged | `false` |
| Source has fewer bytes than requested | The number of bytes actually available (short read) | Same (short read) | Set to `true` on the read that gets 0 from the source |
| Source is fully consumed | `0` | `0` | `true` |
| Empty buffer passed | `0` (no source call made) | `0` | Unchanged |

Both `Read` and `ReadAsync` may return fewer bytes than `buffer.Length` even when the source is not exhausted — callers should always loop until 0 is returned or the desired count is reached.

### Position advancement

| Method | Advances `Position`? |
|--------|----------------------|
| `ReadAsync` / `Read` | Yes — by the number of bytes returned |
| `PeekAsync` / `Peek` | **No** — never changes `Position` |
| `Seek` | Yes — sets `Position` to the given offset |
| `Rewind` | Yes — equivalent to `Seek(mark)` |
| `Mark` | No — returns current `Position` as a `long` |

### Floor tracking (when can I seek/rewind backward?)

| Mode | `Floor` behaviour | Backward seek possible? |
|------|-------------------|-------------------------|
| **Pass-through** (default) | Auto-advances to `Position` after every `ReadAsync` / `Read` | **No** — `Floor == Position`, so any backward `Seek` throws `InvalidOperationException` |
| **Buffered** | Stays where last set by `Release` (or 0 if never released) | **Yes** — as long as the target is ≥ `Floor` |
| **Draining** (after `EndBuffering` with cached blocks ahead) | Auto-advances to `Position` after each `ReadAsync` / `Read` | Only within the not-yet-drained cached range (shrinks on each read) |

### When is memory freed?

| Trigger | What happens | Automatic or manual? |
|---------|--------------|----------------------|
| `ReadAsync` / `Read` in **pass-through** mode | `Floor` advances to `Position`; blocks behind it are returned to `ArrayPool` | **Automatic** |
| `ReadAsync` / `Read` **draining** cached blocks after `EndBuffering` | Same — blocks are freed as `Position` passes them | **Automatic** |
| `PeekAsync` / `Peek` in **pass-through** mode | Blocks are allocated to hold peeked data; freed automatically on the *next* read | **Automatic** (deferred to next read) |
| Any read/peek in **buffered** mode | Blocks are **retained** — nothing is freed | N/A — must release manually |
| `Release(offset)` | Blocks entirely before `offset` freed; `Floor` set to `offset` | **Manual** |
| `EndBuffering()` | Calls `Release(Position)` — frees blocks behind current position | **Automatic** (one-time on call) |
| `Dispose()` | All remaining blocks returned to `ArrayPool` | **Manual** (or `using`) |

### Error conditions

| Action | Condition | Exception |
|--------|-----------|-----------|
| `Seek(offset)` | `offset < Floor` | `InvalidOperationException` |
| `Rewind(mark)` | `mark < Floor` | `InvalidOperationException` |
| `Read` / `Peek` (sync) | `ReadSource` not overridden by subclass | `NotSupportedException` |
| Any method after `Dispose` | Stream disposed | `ObjectDisposedException` |
| Constructor | `blockSize` ≤ 0 | `ArgumentOutOfRangeException` |

`BeginBuffering()` when already buffering and `EndBuffering()` when not buffering are both safe no-ops.
`Release()` with the same or lower offset is a no-op.
`Dispose()` called twice is a no-op.

### Block alignment

`Buffered` may overshoot the number of bytes you requested because `ensureBufferedAsync` fills in block-sized chunks. For example, reading 100 bytes with a 64-byte block size buffers up to offset 128 (two full blocks). This is an implementation detail — `ReadAsync` and `PeekAsync` still return only the bytes you asked for (or fewer).

### Seek beyond `Buffered`

`Seek` only moves the cursor — it does **not** fetch data from the source. The gap between the old `Buffered` and the new `Position` is filled lazily on the next `ReadAsync` or `PeekAsync`. Any source data between the old `Buffered` and the seek target that was never read is **skipped** — it is consumed from the source (to advance the source's internal position) but the bytes are stored in blocks.

## API reference

### Source contract

| Member | Kind | Description |
|--------|------|-------------|
| `ReadSourceAsync(Memory<byte>, CancellationToken)` | `abstract` | Supply the next bytes from the underlying source. Must not seek. Return 0 to signal end of data. **Must** be implemented. |
| `ReadSource(Span<byte>)` | `virtual` | Synchronous equivalent. Override to enable `Read` and `Peek`. Default throws `NotSupportedException`. |

### Properties

| Property          | Type   | Description                                      |
|-------------------|--------|--------------------------------------------------|
| `Position`        | `long` | Current read cursor (absolute byte offset).      |
| `Floor`           | `long` | Earliest retained offset.                        |
| `Buffered`        | `long` | One past the highest byte fetched into cache.    |
| `IsSourceExhausted` | `bool` | True once the source returned 0.              |
| `IsBuffering`     | `bool` | True between `BeginBuffering` / `EndBuffering`.  |

### Methods

| Method | Description |
|--------|-------------|
| `ReadAsync(Memory<byte>, CancellationToken)` | Read and advance `Position`. Returns 0 at end of stream. |
| `Read(Span<byte>)` | Synchronous equivalent of `ReadAsync`. Requires `ReadSource` override. |
| `PeekAsync(Memory<byte>, CancellationToken)` | Read without advancing `Position`. |
| `Peek(Span<byte>)` | Synchronous equivalent of `PeekAsync`. Requires `ReadSource` override. |
| `Seek(long offset)` | Move `Position` to `offset`. Throws if `offset < Floor`. |
| `Mark()` | Return current `Position` as a lightweight `long` token. |
| `Rewind(long mark)` | Restore `Position` to a previous mark. Throws if `mark < Floor`. |
| `BeginBuffering()` | Start caching reads into blocks. No-op if already buffering. |
| `EndBuffering()` | Stop caching. Releases blocks behind `Position`; blocks ahead drain on subsequent reads. No-op if not buffering. |
| `Release(long offset)` | Free blocks before `offset` and advance `Floor`. |
| `Dispose()` | Return all pooled blocks. Idempotent. |

### Constructor

```csharp
protected BlockBufferedStream(int blockSize = 65536)
```

`blockSize` must be a positive integer. It controls the granularity of `ArrayPool<byte>` rentals. Every block is exactly this size — blocks do not grow. The default of 65536 (64 KB) is a good general-purpose choice.

## Memory management

Blocks are rented from `ArrayPool<byte>.Shared` and returned when:

- `Release(offset)` is called — blocks whose data is entirely before `offset` are freed. **Manual.**
- `EndBuffering()` is called — immediately frees blocks behind `Position`. Blocks *ahead* of `Position` (from a prior seek-ahead or over-read) are retained and drained automatically by subsequent `ReadAsync` calls. **One-time automatic on call, then auto-drain.**
- `Dispose()` is called — all blocks returned regardless of cursors. **Manual** (use `using`).
- Every `ReadAsync` in **pass-through** mode — auto-releases blocks behind `Position`, ensuring the stream transitions cleanly back to zero-block pass-through once cached data is fully consumed. **Automatic.**

### Lifecycle example

```
pass-through          BeginBuffering()     reads cached     EndBuffering()     draining          pass-through
─────────────────────►──────────────────►────────────────►──────────────────►─────────────────►──────────────
no blocks              blocks allocated    blocks retained   Release(Pos)      auto-release      no blocks
Floor == Position      Floor unchanged     Floor unchanged   Floor = Position  Floor = Position  Floor == Position
```

## Thread safety

`BlockBufferedStream` is **not** thread-safe. All calls must be serialized by the caller.
