#pragma once
#include <iostream>
#include <fstream>
#include <iomanip>
#include <cstdlib>

/// LOGGING ON THE GAME'S MAIN THREAD IS A FRAME-TIME HAZARD.
///
/// Every exported WorkerProtocol_* entry point runs on the Unity MAIN thread -
/// the SpatialOS op pump is driven synchronously from ConnectionLifecycle.Update.
/// So anything logged from here is a blocking write() inside the frame.
///
/// Debug() writes the line TWICE (std::cerr, which is unit-buffered, and the
/// file with std::endl, which flushes) - two write() syscalls per line, on the
/// main thread. That is fine for the handful of connect/error lines it exists
/// for. It is NOT fine per network op: the per-op trail ran at ~5.3 KB/s and
/// ~100 lines/s, and on a nearly-full btrfs a single one of those writes was
/// measured blocking the whole engine for 40-392 ms inside
/// btrfs_reserve_bytes. That is the "spatial >> asset with all counters at 0"
/// spike signature in the stutter probe: time in the op pump doing no op work.
///
/// Hence the split:
///   Debug() - lifecycle and errors. Low volume, still flushed per line so the
///             log survives a crash. This is what join-failure triage reads.
///   Trace() - per-op chatter. OFF unless WAREBORN_CORESDK_TRACE=1, and even
///             then buffered rather than flushed per line. Guard hot call
///             sites with TraceEnabled() so the string is never built either.
class Logger
{
    static Logger* logger;

    std::ofstream output;
    bool trace;             // per-op tracing armed? (WAREBORN_CORESDK_TRACE=1)
    unsigned pending;       // trace lines written but not yet flushed

    Logger(Logger& other) = delete;
    void operator=(const Logger&) = delete;

    Logger() : trace(false), pending(0) {
        output.open("CoreSdk_OutputLog.txt", std::ios::out);
        const char* t = std::getenv("WAREBORN_CORESDK_TRACE");
        trace = (t != nullptr && t[0] == '1');
        output << "[INFO] CoreSdk per-op trace "
               << (trace ? "ENABLED (frame-time cost: this logs on the main thread)"
                         : "disabled - set WAREBORN_CORESDK_TRACE=1 to enable")
               << std::endl;
    }

    ~Logger() {
        output.close();
    }

public:
    static Logger* GetLogger() {
        if (!logger) {
            logger = new Logger();
        }
        return logger;
    }

    /// Cheap enough to guard a string concatenation with.
    static bool TraceEnabled() {
        return GetLogger()->trace;
    }

    void debug(std::string msg) {
        std::cerr << msg << std::endl;
        // endl flushes, which also drains any buffered trace lines, so the
        // file stays in order and `pending` is settled.
        output << msg << std::endl;
        pending = 0;
    }

    /// Per-op chatter. No cerr copy, no per-line flush.
    void traceLine(const std::string& msg) {
        if (!trace) {
            return;
        }
        output << msg << '\n';
        if (++pending >= 256) {
            output.flush();
            pending = 0;
        }
    }

    static void Trace(const std::string& msg) {
        GetLogger()->traceLine(msg);
    }

    static void toHex(char c) {
        std::cerr << std::hex << std::setw(2) << std::setfill('0') << (int)static_cast<unsigned char>(c) << " ";
    }

    static void Debug(std::string msg) {
        GetLogger()->debug(msg);
    }

    static void Hexify(char* buffer, int length) {
        for (int i = 0; i < length; i++) {
            GetLogger()->toHex(buffer[i]);
        }
        GetLogger()->debug("");
    }

    static void printAddr(void* addr) {
        std::cerr << addr << std::endl;
    }

    static void PrintAddress(void* addr) {
        GetLogger()->printAddr(addr);
    }
};

