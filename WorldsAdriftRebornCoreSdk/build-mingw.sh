#!/bin/bash
# Cross-compile CoreSdkDll.dll for Windows x64 using mingw-w64.
#
# The upstream project builds this with MSVC via WorldsAdriftRebornCoreSdk.vcxproj
# and a private vcpkg protobuf NuGet package. This script is an alternative build
# path for Linux, using the distro's mingw-w64 protobuf.
#
# Output: build-mingw/CoreSdkDll.dll (+ the runtime DLLs it imports)
set -euo pipefail

cd "$(dirname "$0")"
SRC="$PWD"
OUT="$SRC/build-mingw"
rm -rf "$OUT"; mkdir -p "$OUT/obj"

CXX=x86_64-w64-mingw32-g++
CC=x86_64-w64-mingw32-gcc
export PKG_CONFIG_PATH=/usr/x86_64-w64-mingw32/lib/pkgconfig
export PKG_CONFIG_SYSROOT_DIR=/
PROTO_CFLAGS=$(pkg-config --cflags protobuf)
PROTO_LIBS=$(pkg-config --libs protobuf)

echo "==> generating protobuf sources"
mkdir -p "$OUT/gen"
protoc --proto_path="$SRC" --cpp_out="$OUT/gen" "$SRC"/*.proto
ls "$OUT/gen"

echo "==> creating MSVC compatibility shims"
# Dispatcher.cpp includes <corecrt_malloc.h>, an MSVC UCRT header with no mingw
# equivalent. Shimming it here keeps the upstream sources untouched.
mkdir -p "$OUT/compat"
cat > "$OUT/compat/corecrt_malloc.h" <<'SHIM'
#pragma once
/* MSVC UCRT header with no mingw-w64 equivalent; malloc.h provides the same
   allocation entry points (malloc/free/_aligned_malloc). */
#include <malloc.h>
SHIM

# The generated headers are included as "Foo.pb.h", so $OUT/gen must be on the
# include path ahead of the source dir.
INCLUDES="-I$OUT/gen -I$SRC -I$SRC/enet/include -I$OUT/compat"
DEFINES="-DWIN32 -DNDEBUG -DWORLDSADRIFTREBORNCORESDK_EXPORTS -D_WINDOWS -D_USRDLL"

echo "==> compiling enet (C)"
# win32.c is the Windows platform layer; unix.c is deliberately excluded.
for f in callbacks compress host list packet peer protocol win32; do
    $CC -c "$SRC/enet/$f.c" -o "$OUT/obj/enet_$f.o" \
        -I"$SRC/enet/include" -DWIN32 -DNDEBUG -O2 -w
done

echo "==> compiling generated protobuf sources (C++)"
for f in "$OUT"/gen/*.pb.cc; do
    b=$(basename "$f" .cc)
    $CXX -c "$f" -o "$OUT/obj/pb_$b.o" $INCLUDES $DEFINES $PROTO_CFLAGS -O2 -std=c++17 -w
done

echo "==> compiling CoreSdk sources (C++)"
for f in Connection ConnectionFuture DeploymentListFuture Dispatcher enetLayer Exports Locator Logger; do
    $CXX -c "$SRC/$f.cpp" -o "$OUT/obj/$f.o" $INCLUDES $DEFINES $PROTO_CFLAGS -O2 -std=c++17 -w
done

echo "==> linking CoreSdkDll.dll"
# NOTE: the mingw runtime must be linked SHARED here. -static-libstdc++ fails to
# link because the abseil DLLs already export std::string symbols, producing
# "multiple definition of std::__cxx11::basic_string::..." against libstdc++.a.
# The collect step below bundles libstdc++-6.dll and libgcc_s_seh-1.dll with the
# protobuf/abseil DLLs, so the result is still self-contained.
$CXX -shared -o "$OUT/CoreSdkDll.dll" "$OUT"/obj/*.o \
    $PROTO_LIBS -lws2_32 -lwinmm \
    -Wl,--out-implib,"$OUT/CoreSdkDll.a"

echo "==> collecting runtime DLL dependencies"
# Copy only the DLLs actually imported, resolved transitively.
collect() {
    local dll="$1"
    x86_64-w64-mingw32-objdump -p "$dll" 2>/dev/null \
        | awk '/DLL Name:/ {print $3}' \
        | while read -r dep; do
            if [ -f "/usr/x86_64-w64-mingw32/bin/$dep" ] && [ ! -f "$OUT/$dep" ]; then
                cp "/usr/x86_64-w64-mingw32/bin/$dep" "$OUT/$dep"
                collect "$OUT/$dep"
            fi
        done
}
collect "$OUT/CoreSdkDll.dll"

echo "==> done"
file "$OUT/CoreSdkDll.dll"
echo "runtime DLLs bundled: $(ls "$OUT"/*.dll | grep -cv CoreSdkDll.dll)"
