#!/bin/bash
# Native Linux build of the CoreSdk shim: libCoreSdkDll.so.
#
# WHY THIS EXISTS. The relay bot (tools/relaybot) only needs the shim's ENet
# transport exports (ENet_EXP_*); it does its own protobuf framing in C#. The
# shim itself is normally mingw-cross-compiled to CoreSdkDll.dll and run under
# Wine (build-mingw.sh) - building the SAME sources natively lets the bot run
# on plain Linux .NET with no Wine in the measurement path, which matters
# because the bot's whole job is timing.
#
# Differences from build-mingw.sh, all forced by the platform:
#   - enet's unix.c platform layer instead of win32.c, no -DWIN32;
#   - __declspec(dllexport) is MSVC/mingw-only, defined away (default ELF
#     visibility already exports everything);
#   - system protobuf + protoc instead of the mingw package;
#   - the corecrt_malloc.h shim is reused verbatim.
#
# Output: build-native/libCoreSdkDll.so, next to this script.
# .NET's DllImport("CoreSdkDll") probes libCoreSdkDll.so on Linux, so the
# managed P/Invoke declarations work unchanged against DLL and .so.
set -euo pipefail

cd "$(dirname "$0")"
SDK="$(cd ../../WorldsAdriftRebornCoreSdk && pwd)"
OUT="$PWD/build-native"
rm -rf "$OUT"; mkdir -p "$OUT/obj"

echo "==> generating protobuf sources (system protoc: $(protoc --version))"
mkdir -p "$OUT/gen"
protoc --proto_path="$SDK" --cpp_out="$OUT/gen" "$SDK"/*.proto

echo "==> creating MSVC compatibility shims"
mkdir -p "$OUT/compat"
cat > "$OUT/compat/corecrt_malloc.h" <<'SHIM'
#pragma once
/* MSVC UCRT header with no glibc equivalent; malloc.h provides the same
   allocation entry points. */
#include <malloc.h>
SHIM

PROTO_CFLAGS=$(pkg-config --cflags protobuf)
PROTO_LIBS=$(pkg-config --libs protobuf)

INCLUDES="-I$OUT/gen -I$SDK -I$SDK/enet/include -I$OUT/compat"
# __declspec(x) -> nothing: ELF exports by default. __cdecl -> nothing: it is
# the only calling convention on x86-64 SysV anyway. -fPIC everywhere: shared lib.
DEFINES="-DNDEBUG '-D__declspec(x)=' '-D__cdecl='"

echo "==> compiling enet (C, unix platform layer)"
for f in callbacks compress host list packet peer protocol unix; do
    gcc -c "$SDK/enet/$f.c" -o "$OUT/obj/enet_$f.o" \
        -I"$SDK/enet/include" -DNDEBUG -DHAS_FCNTL -DHAS_POLL \
        -DHAS_GETADDRINFO -DHAS_GETNAMEINFO -DHAS_GETHOSTBYNAME_R \
        -DHAS_GETHOSTBYADDR_R -DHAS_INET_PTON -DHAS_INET_NTOP \
        -DHAS_MSGHDR_FLAGS -DHAS_SOCKLEN_T -O2 -fPIC -w
done

echo "==> compiling generated protobuf sources (C++)"
for f in "$OUT"/gen/*.pb.cc; do
    b=$(basename "$f" .cc)
    eval g++ -c "$f" -o "$OUT/obj/pb_$b.o" $INCLUDES $DEFINES $PROTO_CFLAGS -O2 -fPIC -std=c++17 -w
done

echo "==> compiling CoreSdk sources (C++)"
for f in Connection ConnectionFuture DeploymentListFuture Dispatcher enetLayer Exports Locator Logger; do
    eval g++ -c "$SDK/$f.cpp" -o "$OUT/obj/$f.o" $INCLUDES $DEFINES $PROTO_CFLAGS -O2 -fPIC -std=c++17 -w
done

echo "==> linking libCoreSdkDll.so"
g++ -shared -o "$OUT/libCoreSdkDll.so" "$OUT"/obj/*.o $PROTO_LIBS

echo "==> done"
file "$OUT/libCoreSdkDll.so"
# Prove the exports the bot needs actually exist. NOT grep -q: under pipefail
# its early exit SIGPIPEs nm and the pipeline reports failure on a MATCH.
SYMS=$(nm -D --defined-only "$OUT/libCoreSdkDll.so")
for sym in ENet_EXP_Initialize ENet_EXP_Create_Host ENet_EXP_Connect \
           ENet_EXP_Poll ENet_EXP_Send ENet_EXP_Flush ENet_EXP_Destroy_Packet \
           ENet_EXP_Disconnect ENet_EXP_Deinitialize; do
    echo "$SYMS" | grep " $sym\$" > /dev/null \
        || { echo "MISSING EXPORT: $sym"; exit 1; }
done
echo "all ENet_EXP_* exports present"

# The game server also drives protobuf helpers directly and the complete
# WorkerProtocol surface through Improbable.WorkerSdkCsharp. Derive the required
# export names from the current sources so additions such as RemoveEntity cannot
# compile successfully yet disappear from the Linux shim.
SERVER_ENET_PB=$(grep -oE 'EntryPoint = "[^"]+"' \
    ../../WorldsAdriftRebornGameServer/DLLCommunication/EnetLayer.cs \
    | sed 's/.*"\(.*\)"/\1/' | sort -u)
WORKER_WAR=$(grep -oE 'WAR_SetGamePort|WorkerProtocol_[A-Za-z_]+' \
    "$SDK/Exports.h" | sort -u)
MISSING=0
for sym in $SERVER_ENET_PB $WORKER_WAR; do
    echo "$SYMS" | grep " $sym\$" > /dev/null \
        || { echo "MISSING GAME-SERVER EXPORT: $sym"; MISSING=$((MISSING+1)); }
done
[ "$MISSING" -eq 0 ] || { echo "$MISSING game-server exports MISSING"; exit 1; }
echo "all game-server exports present ($(echo "$SERVER_ENET_PB $WORKER_WAR" | wc -w) checked)"
