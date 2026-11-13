# Environment B startup comparison (2026-11-13)

Environment A/B identify the two measured setups. Hardware, conditions and source revisions are recorded below.
Source: f2d9ce7, no code changes. Dedicated verification build, three sequential starts, exit code 0. No full suite or performance changes.

Material SHA256: ed948fb91e7b24abbc634808f51c19cdfa9174cf0370776326bcb28138ab97bd (matches environment A report).

Environment B conditions: i7-13700KF / RTX 4080; FFmpeg 6.0-full_build-www.gyan.dev; Realtek speakers; canvas 1920x1080; external output off; hardware gain 0; device initialized before play. Environment A conditions differ: FFmpeg 8.1, NVIDIA HDMI audio, canvas 1080x1920. OS cache not cleared. Three trials are diagnostic samples, not a statistical benchmark.

Milliseconds from play_request, rounded:

| Metric | Environment B first | Environment B retry 1 | Environment B retry 2 | Environment A first | Environment A retry 1 | Environment A retry 2 |
|---|---:|---:|---:|---:|---:|---:|
| Still presentation API return | 135 | 79 | 84 | 1695 | 624 | 616 |
| Playback presentation API return | 209 | 146 | 149 | 2308 | 1218 | 1208 |
| WASAPI Start return | 290 | 184 | 188 | 3426 | 1782 | 1762 |
| Audio FFmpeg start request to Process.Start return | 4 | 12 | 4 | 501 | 499 | 491 |

Environment B audio internal error after start: +3.65, -0.32, -7.81 ms; zero reseeks. Physical output timing and long-term synchronization were not measured.

Finding: large environment-A-specific delay exists before Process.Start returns. This does not isolate OS process creation, security inspection, executable build/location, or scheduling. FFmpeg version differs, so CPU/Boost cannot be identified as the cause. Both PCs also follow the same sequential still/stream preparation path.

Next bounded diagnostic: in environment A, measure the same .NET launch path for ffmpeg -version a few times, separating Process.Start return from process exit. Then compare the same FFmpeg binary/version on both PCs if needed. Do not change security settings. No additional tests performed here.

Raw JSON remains outside the repository under the Codex workspace work/startup-comparison/run-1. No generated binaries or raw device settings added to Git.

## Single-buffer playback change (2026-11-13, uncommitted)

PlayAsync now consumes and presents the first frame from its continuous playback buffer before starting the clock. The separate restart still decode is removed. LoadAsync and SeekAsync still presentation are unchanged.

Validation: verification build succeeded; preview-performance passed (6 measurements including HAP/H.264 pause/seek/replay); playback-integration 11/11 passed; startup harness 3/3 completed, zero reseeks. No full suite executed.

Restart trials contain no video_still_decode_begin event. Request to WASAPI Start was 106/108ms (previous local baseline 184/188ms). First attempt was 283ms versus prior 290ms; initial selection still decoding remains. These are small samples, not guaranteed latency bounds. Environment A improvement remains unmeasured.

Measurement caveat: video_first_present records the buffer's first frame before clock start on restarts. video_playback_first_present is emitted by RunAsync on a later changed frame, so after this change it is not the first presentation from the playback buffer. Do not compare that marker alone as equivalent first-frame latency across versions. Raw transitions preserve both markers and video_clock_start.

## Parallel manual audio preparation (2026-11-13, uncommitted)

PreviewPlayer publishes a preparation request with the playback session identity and cancellation token when PlayAsync starts preparing its stream. PlaybackCoordinator forwards it without advancing the clock. AudioOutputService may inspect/decode this source, but cannot begin a WASAPI epoch or write PCM until the advancing playhead for the same session authorizes playback. The existing bounded PCM queue limits predecoded data. Cancelled requests and replacement identities invalidate preparation. The preparation permission remains present through the final busy-state transition to avoid resetting the decoder between preparing and playing.

Scope: manual stream preparation only. Initial LoadAsync selection analysis/still preparation is not overlapped, audio metadata is not shared across analyzers, and no LTC lookahead is added. Paused playback retains its existing resume path.

Final verification build succeeded. audio-service 8/8 (extended existing cases verify prepared PCM does not enter the hardware queue, same decoder is used after permission, cancellation releases decoder), audio-connected 8/8, playback-integration 13/13 with the supplied H.264 material. Startup harness: 3/3, preparation counters 1/2/3, reseeks 0/0/0. No full suite, endurance or physical AV timing tests.

| Trial | Request to video clock ms | Request to WASAPI Start ms | Audio start after video clock ms |
|---|---:|---:|---:|
| First | 201.2 | 219.1 | 17.9 |
| Restart 1 | 69.3 | 72.9 | 3.6 |
| Restart 2 | 72.1 | 73.5 | 1.4 |

Restart PCM was available about 39ms before video clock start. First-trial PCM arrived about 4.5ms after clock start. Internal errors after start: +2.50/+1.67/+3.97ms. These three samples are not guaranteed latency bounds. Environment A effect is not measured yet. Existing local FFmpeg 6.0/Realtek/1920x1080 conditions still differ from the environment A.

An intermediate measurement exposed a redundant decoder restart during the busy-to-playing transition; this was fixed before the final tests above. Intermediate samples are not combined with final results. Final raw logs remain outside Git under work/startup-comparison/audio-preparation-final.

## Supplied environment A FFmpeg 8.1 executable (2026-11-13)

Source HEAD 838bf1f. Received environment A executable in Downloads; used its absolute path without changing PATH or installed FFmpeg. Size 223360000 bytes; SHA256 d1e2a156261ecc675081943197a85f08f2868784a0af499171ede89353edad31. Environment A hash was not recorded, so byte identity is based on user-supplied provenance and matching size/version, not independently verified hashes from both PCs.

Temporary net10.0/Release console, SDK 10.0.203, runtime 10.0.7. Working directory repository root. UseShellExecute=false, CreateNoWindow=true, both outputs asynchronously drained. Stopwatch timestamps directly surround Process.Start. Parallel workers use LongRunning tasks, CountdownEvent and a shared ManualResetEventSlim. Six alternating sequential/parallel pairs, twelve starts total, no extra warmup. All return codes 0 and version 8.1-full_build-www.gyan.dev. Build and file hashing excluded from measured intervals. OS cache not cleared; hash read occurred before launch. Timeout covers child lifetime after Start; the synchronous Start call itself cannot be interrupted by that token.

| Order | Mode | A Start ms | B Start ms | A return-to-exit ms | B return-to-exit ms | Pair ms | Start overlap ms |
|---|---|---:|---:|---:|---:|---:|---:|
| 1 | sequential | 382.055 | 1.585 | 36.939 | 24.797 | 445.620 | 0 |
| 2 | parallel | 2.761 | 1.450 | 27.594 | 29.080 | 30.530 | 1.444 |
| 3 | sequential | 1.381 | 1.396 | 23.598 | 23.605 | 50.004 | 0 |
| 4 | parallel | 2.536 | 1.356 | 22.669 | 23.622 | 25.210 | 1.351 |
| 5 | sequential | 1.341 | 1.406 | 23.873 | 24.001 | 50.648 | 0 |
| 6 | parallel | 2.769 | 1.426 | 22.497 | 24.035 | 25.461 | 1.423 |

The first Start took 382ms, subsequent eleven 1.34-2.77ms. All three parallel Start intervals overlap (entry gaps 0.0032-0.0061ms). The recurring environment A 490/974ms pattern did not reproduce with the supplied 8.1 binary here. Version alone is insufficient to explain the difference. First-start cost could include runtime/JIT, OS or security/cache effects; this test does not isolate them. Different placement (Downloads vs environment A WinGet package), PC environment and independently authored harness remain uncontrolled. No specific OS/security cause is established.

No app rebuild/playback, environment changes or further diagnosis. Temporary code, JSON and binary remain outside Git in the Codex workspace work/process-launch-local and Downloads. Report only; not committed/pushed.
